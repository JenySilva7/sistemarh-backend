using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IFuncionarioService
{
    Task<List<FuncionarioDto>> ListarAsync(StatusFuncionario? status, int? departamentoId);
    Task<FuncionarioDto> ObterPorIdAsync(int id);
    Task<FuncionarioDto> CriarAsync(FuncionarioCreateDto dto);
    // solicitanteId: a conta que faz a operação (do token). Contas RH/Admin
    // ligadas a um colaborador só têm o acesso alterado por um Admin.
    // null = chamada interna, sem usuário (ex.: testes de regra de negócio).
    Task<FuncionarioDto> AtualizarAsync(int id, FuncionarioUpdateDto dto, int? solicitanteId = null);
    Task<DesligamentoResultadoDto> DesligarAsync(int id, DesligarFuncionarioDto? dto, int? solicitanteId = null);
    Task<FuncionarioDto> DefinirAcessoAppAsync(int funcionarioId, string? senha, int? solicitanteId = null);
    Task<FuncionarioDto> AjustarSalarioAsync(int funcionarioId, AjusteSalarioDto dto, int responsavelUsuarioId);
    Task<FuncionarioDto> AjustarValeAlimentacaoAsync(int funcionarioId, AjusteValeAlimentacaoDto dto, int responsavelUsuarioId);
    Task<PromocaoResultadoDto> PromoverAsync(int funcionarioId, PromocaoDto dto, int responsavelUsuarioId);
    Task<List<AjusteRemuneracaoDto>> ListarAjustesRemuneracaoAsync(int funcionarioId);
}

/// <summary>
/// Cadastro completo de funcionários (CPF, cargo, departamento etc.) — o
/// lado administrativo/RH. Diferente de <see cref="AuthService"/>, que cuida
/// só da conta de login usada pelo app.
///
/// Toda operação que altera um funcionário roda numa transação e trava a
/// linha dele antes de ler (no MySQL): duas telas mexendo no mesmo
/// colaborador ao mesmo tempo passam a esperar uma pela outra, em vez de dar
/// deadlock (500) ou gravar o "valor anterior" errado no histórico. Se a
/// tela mandar a <c>versao</c> que tinha e ela já não for a atual, a
/// resposta é 409.
/// </summary>
public class FuncionarioService : IFuncionarioService
{
    /// <summary>Vale alimentação de quem é cadastrado sem informar o valor.</summary>
    public const decimal ValeAlimentacaoPadrao = 600m;

    public const string MensagemContaComMesmoEmail =
        "Já existe uma conta de acesso com este e-mail. Fale com o administrador.";
    private const string MensagemSemEmailParaAcesso =
        "Cadastre um e-mail para esse colaborador antes de criar o acesso ao app.";
    private const string MensagemCpfDuplicado = "Já existe um funcionário cadastrado com esse CPF.";
    private const string MensagemEmailDuplicado = "Já existe um funcionário cadastrado com esse e-mail.";
    public const string MensagemSoAdminAlteraAcesso = "Somente um administrador pode alterar o acesso de contas RH/Admin.";
    public const string MensagemSoAdminDesliga = "Somente um administrador pode desligar um colaborador com conta RH/Admin.";
    public const string MensagemUltimoAdmin = "Não é possível desligar o último administrador ativo do sistema.";
    public const string MensagemPromocaoSemMudanca = "A promoção precisa mudar o cargo ou o departamento.";
    public const string MensagemPromocaoSoSalario = "Para alterar só o salário, use Ajuste Salarial.";

    private readonly AppDbContext _db;
    private readonly PasswordHasher<Usuario> _passwordHasher = new();

    public FuncionarioService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<FuncionarioDto>> ListarAsync(StatusFuncionario? status, int? departamentoId)
    {
        Enums.ValidarFiltro(status, MensagensValidacao.MensagemStatusInvalido);

        var query = _db.Funcionarios
            .Include(f => f.Departamento)
            .Include(f => f.Cargo)
            .Include(f => f.Gerente)
            .AsQueryable();

        if (status is not null)
        {
            query = query.Where(f => f.Status == status);
        }
        if (departamentoId is not null)
        {
            query = query.Where(f => f.DepartamentoId == departamentoId);
        }

        var funcionarios = await query.OrderBy(f => f.Nome).ThenBy(f => f.Id).ToListAsync();
        var mapaContagens = await ContarSolicitacoesPorFuncionarioAsync();
        // O mapa só tem chave pra funcionário com conta vinculada — então
        // ContainsKey já responde se ele tem acesso ao app.
        return funcionarios
            .Select(f => ParaDto(f, mapaContagens.GetValueOrDefault(f.Id, 0), mapaContagens.ContainsKey(f.Id)))
            .ToList();
    }

    public async Task<FuncionarioDto> ObterPorIdAsync(int id)
    {
        var funcionario = await BuscarOuFalhar(id);
        var (total, possuiAcessoApp) = await ContarSolicitacoesAsync(id);
        return ParaDto(funcionario, total, possuiAcessoApp);
    }

    public async Task<FuncionarioDto> CriarAsync(FuncionarioCreateDto dto)
    {
        // Tudo é validado antes de gravar qualquer coisa, pra uma falha não
        // deixar o funcionário (ou o acesso ao app) criado pela metade.
        var nome = Textos.ValidarNomePessoa(dto.Nome);
        var cpf = Cpf.Normalizar(dto.Cpf);
        var email = Emails.NormalizarOpcional(dto.Email);
        var telefone = Telefones.NormalizarOpcional(dto.Telefone);
        var nascimento = Datas.ValidarNascimento(dto.DataNascimento);
        var admissao = Datas.ValidarAdmissao(dto.DataAdmissao, nascimento);
        var departamentoId = dto.DepartamentoId ?? throw new BusinessRuleException("Selecione o departamento.");
        var cargoId = dto.CargoId ?? throw new BusinessRuleException("Selecione o cargo.");

        var criarAcessoApp = !string.IsNullOrEmpty(dto.SenhaAcessoApp);
        if (criarAcessoApp)
        {
            if (email is null)
            {
                throw new BusinessRuleException(MensagemSemEmailParaAcesso);
            }
            Senhas.Validar(dto.SenhaAcessoApp);
        }

        await GarantirDepartamentoECargoExistemAsync(departamentoId, cargoId);

        // Com gestor, o cadastro é uma mudança de hierarquia: pega a trava
        // global e trava o gestor (compartilhado) ANTES de conferir se ele
        // está ativo. Sem isso, um desligamento dele ao mesmo tempo terminava
        // sem ver o novo subordinado, que ficava ativo sob um gestor desligado.
        await using var transacao = await _db.IniciarTransacaoAsync();
        await using var travaHierarquia = dto.GerenteId is null
            ? null
            : await _db.TravarGlobalAsync(AppDbContext.TravaHierarquia);
        await _db.TravarGestorParaLeituraAsync(dto.GerenteId);
        await ValidarGestorAsync(funcionarioId: null, dto.GerenteId);

        // Sem salário informado, o colaborador entra com o salário base do cargo.
        var salario = dto.Salario is null
            ? await SalarioBaseDoCargoAsync(cargoId)
            : Dinheiro.ValidarSalario(dto.Salario, "Informe o salário.");
        var vale = dto.ValeAlimentacao is null
            ? ValeAlimentacaoPadrao
            : Dinheiro.ValidarVale(dto.ValeAlimentacao, "Informe o vale alimentação.");

        // Compara só os dígitos: "529.982.247-25" e "52998224725" são o mesmo CPF.
        var cpfDigitos = Cpf.SomenteDigitos(cpf);
        var cpfJaExiste = await _db.Funcionarios.AnyAsync(f =>
            f.Cpf.Replace(".", "").Replace("-", "").Replace(" ", "") == cpfDigitos);
        if (cpfJaExiste)
        {
            throw new ConflictException(MensagemCpfDuplicado);
        }
        if (email is not null && await _db.Funcionarios.AnyAsync(f => f.Email == email))
        {
            throw new ConflictException(MensagemEmailDuplicado);
        }
        // Conta de acesso com esse e-mail que não é deste colaborador (ele
        // ainda nem existe): NÃO é ligada automaticamente — era assim que um
        // intruso que se cadastrasse antes virava o "acesso" do colaborador.
        if (criarAcessoApp && await _db.Usuarios.AnyAsync(u => u.Email == email))
        {
            throw new ConflictException(MensagemContaComMesmoEmail);
        }

        var funcionario = new Funcionario
        {
            Nome = nome,
            Cpf = cpf,
            Email = email,
            Telefone = telefone,
            DataNascimento = nascimento,
            DataAdmissao = admissao,
            Status = StatusFuncionario.Ativo,
            DepartamentoId = departamentoId,
            CargoId = cargoId,
            GerenteId = dto.GerenteId,
            Salario = salario,
            ValeAlimentacao = vale,
            CriadoEm = DateTime.UtcNow,
        };

        if (criarAcessoApp)
        {
            var conta = await NovaContaAsync(funcionario, email!);
            conta.SenhaHash = _passwordHasher.HashPassword(conta, dto.SenhaAcessoApp!);
            _db.Usuarios.Add(conta);
        }

        // Um SaveChanges só: funcionário e acesso ao app são gravados juntos.
        _db.Funcionarios.Add(funcionario);
        await SalvarTratandoDuplicidadeAsync();
        await AppDbContext.ConfirmarAsync(transacao);

        return await ObterPorIdAsync(funcionario.Id);
    }

    public async Task<FuncionarioDto> DefinirAcessoAppAsync(int funcionarioId, string? senha, int? solicitanteId = null)
    {
        await using var transacao = await _db.IniciarTransacaoAsync();
        // A mesma trava de perfis da troca de perfil, depois a linha do
        // colaborador e a da conta — só então o perfil da conta é lido. Antes
        // só a linha do colaborador era travada: com a promoção a Admin em
        // andamento, o RH lia a conta como Funcionário, passava no 403 e
        // gravava a senha dele numa conta que já era Admin.
        await using var travaPerfis = await _db.TravarGlobalAsync(AppDbContext.TravaPerfis);
        await _db.TravarFuncionariosAsync(funcionarioId);
        await _db.TravarContaDoFuncionarioAsync(funcionarioId);

        var funcionario = await BuscarOuFalhar(funcionarioId);
        if (funcionario.Status != StatusFuncionario.Ativo)
        {
            throw new BusinessRuleException("Não é possível dar acesso ao app a um colaborador desligado.");
        }
        if (string.IsNullOrWhiteSpace(funcionario.Email))
        {
            throw new BusinessRuleException(MensagemSemEmailParaAcesso);
        }
        Senhas.Validar(senha);
        var email = Emails.Normalizar(funcionario.Email);

        var conta = await _db.Usuarios.FirstOrDefaultAsync(u => u.FuncionarioId == funcionarioId);
        if (conta is null)
        {
            // Uma conta com o mesmo e-mail que não é deste colaborador nunca é
            // "adotada": quem a criou continuaria logado como ele.
            if (await _db.Usuarios.AnyAsync(u => u.Email == email))
            {
                throw new ConflictException(MensagemContaComMesmoEmail);
            }
            conta = await NovaContaAsync(funcionario, email);
            _db.Usuarios.Add(conta);
        }
        else
        {
            // Conta RH/Admin ligada ao colaborador: só um Admin redefine a
            // senha — senão o RH definia a senha de uma conta Admin e entrava
            // como Admin (ou assumia a conta de outro RH).
            await ExigirAdminParaContaPrivilegiadaAsync(conta, solicitanteId, MensagemSoAdminAlteraAcesso);
            // Redefinir a senha derruba as sessões abertas com a senha antiga.
            conta.TokenVersao++;
        }

        conta.SenhaHash = _passwordHasher.HashPassword(conta, senha!);
        await SalvarTratandoDuplicidadeAsync();
        await AppDbContext.ConfirmarAsync(transacao);

        return await ObterPorIdAsync(funcionarioId);
    }

    public async Task<FuncionarioDto> AtualizarAsync(int id, FuncionarioUpdateDto dto, int? solicitanteId = null)
    {
        var nome = Textos.ValidarNomePessoa(dto.Nome);
        var email = Emails.NormalizarOpcional(dto.Email);
        var telefone = Telefones.NormalizarOpcional(dto.Telefone);
        var nascimento = Datas.ValidarNascimento(dto.DataNascimento);
        var departamentoId = dto.DepartamentoId ?? throw new BusinessRuleException("Selecione o departamento.");
        var cargoId = dto.CargoId ?? throw new BusinessRuleException("Selecione o cargo.");
        if (dto.GerenteId == id)
        {
            throw new BusinessRuleException("Um funcionário não pode ser gestor de si mesmo.");
        }

        await using var transacao = await _db.IniciarTransacaoAsync();
        // Com gestor, é mudança de hierarquia: a trava global vem primeiro.
        // Travar só a pessoa e o novo gestor não basta — com A→B e C→D já
        // gravados, "B→C" e "D→A" ao mesmo tempo travavam pares disjuntos e
        // fechavam o ciclo A→B→C→D→A. Depois, as linhas da pessoa e do novo
        // gestor (o desligamento desse gestor ao mesmo tempo espera).
        await using var travaHierarquia = dto.GerenteId is null
            ? null
            : await _db.TravarGlobalAsync(AppDbContext.TravaHierarquia);
        // A edição pode trocar o e-mail de login da conta ligada: pega a
        // trava de perfis (depois da de hierarquia, a ordem de sempre) e,
        // depois das linhas de colaborador, a linha da conta, antes de ler o
        // perfil dela. Sem isso, a troca de e-mail feita pelo RH durante a
        // promoção da conta a Admin passava no 403 e era gravada na conta já
        // promovida.
        await using var travaPerfis = await _db.TravarGlobalAsync(AppDbContext.TravaPerfis);
        await _db.TravarFuncionariosAsync(id, dto.GerenteId);
        await _db.TravarContaDoFuncionarioAsync(id);

        var funcionario = await BuscarOuFalhar(id);
        Concorrencia.ConferirVersao(funcionario, dto.Versao);

        if (nascimento is DateTime n && funcionario.DataAdmissao < n.AddYears(14))
        {
            throw new BusinessRuleException(
                $"A data de nascimento precisa ser pelo menos 14 anos antes da admissão ({Datas.Formatar(funcionario.DataAdmissao)}).");
        }
        await GarantirDepartamentoECargoExistemAsync(departamentoId, cargoId);
        if (dto.GerenteId != funcionario.GerenteId)
        {
            await ValidarGestorAsync(id, dto.GerenteId);
        }

        if (email is not null && await _db.Funcionarios.AnyAsync(f => f.Email == email && f.Id != id))
        {
            throw new ConflictException(MensagemEmailDuplicado);
        }

        // O e-mail do cadastro é o login do app: se o colaborador tem acesso,
        // a conta muda junto (antes o login continuava só com o e-mail antigo).
        var conta = await _db.Usuarios.FirstOrDefaultAsync(u => u.FuncionarioId == id);
        if (conta is not null)
        {
            if (email is null)
            {
                throw new BusinessRuleException("Não é possível remover o e-mail de quem tem acesso ao app.");
            }
            if (conta.Email != email)
            {
                // Trocar o e-mail de login de uma conta RH/Admin é tomar a conta:
                // só um Admin pode.
                await ExigirAdminParaContaPrivilegiadaAsync(conta, solicitanteId, MensagemSoAdminAlteraAcesso);
                if (await _db.Usuarios.AnyAsync(u => u.Email == email && u.Id != conta.Id))
                {
                    throw new ConflictException("Já existe uma conta de acesso com este e-mail.");
                }
                conta.Email = email;
                conta.TokenVersao++;
            }
            conta.Nome = nome;
        }

        funcionario.Nome = nome;
        funcionario.Email = email;
        funcionario.Telefone = telefone;
        funcionario.DataNascimento = nascimento;
        funcionario.DepartamentoId = departamentoId;
        funcionario.CargoId = cargoId;
        funcionario.GerenteId = dto.GerenteId;

        await SalvarTratandoDuplicidadeAsync();
        await AppDbContext.ConfirmarAsync(transacao);
        return await ObterPorIdAsync(id);
    }

    public async Task<DesligamentoResultadoDto> DesligarAsync(int id, DesligarFuncionarioDto? dto, int? solicitanteId = null)
    {
        await using var transacao = await _db.IniciarTransacaoAsync();
        // Travas globais primeiro (hierarquia e, por causa da regra do último
        // Admin, perfis — sempre nessa ordem, a mesma das outras operações).
        // Depois: 1º quem sai (FOR UPDATE) — cadastros, edições e promoções
        // que põem alguém sob essa pessoa travam a mesma linha, então ou
        // terminam antes (e o subordinado novo é visto e realocado abaixo) ou
        // esperam e a veem desligada; 2º o gestor dela (quem herda a equipe);
        // 3º os subordinados. Só depois disso os dados são lidos.
        await using var travaHierarquia = await _db.TravarGlobalAsync(AppDbContext.TravaHierarquia);
        await using var travaPerfis = await _db.TravarGlobalAsync(AppDbContext.TravaPerfis);
        await _db.TravarFuncionariosAsync(id);
        await TravarGestorAtualAsync(id);
        await _db.TravarSubordinadosAsync(id);
        // Por último, a conta ligada (o desligamento sobe a TokenVersao dela
        // e confere o perfil pro 403 e pra regra do último Admin).
        await _db.TravarContaDoFuncionarioAsync(id);

        var funcionario = await BuscarOuFalhar(id);
        if (funcionario.Status == StatusFuncionario.Desligado)
        {
            throw new BusinessRuleException("Esse funcionário já está desligado.");
        }
        Concorrencia.ConferirVersao(funcionario, dto?.Versao);

        // Desligar derruba o acesso da conta ligada. Se ela é RH/Admin, só um
        // Admin pode — e nunca o último Admin ativo (senão um RH deixava o
        // sistema sem nenhum Admin que consiga entrar).
        var contaLigada = await _db.Usuarios.FirstOrDefaultAsync(u => u.FuncionarioId == id);
        if (contaLigada is not null)
        {
            await ExigirAdminParaContaPrivilegiadaAsync(contaLigada, solicitanteId, MensagemSoAdminDesliga);
            if (contaLigada.Perfil == PerfilUsuario.Admin
                && await UsuarioService.ContarAdminsAtivosAsync(_db, excetoUsuarioId: contaLigada.Id) == 0)
            {
                throw new BusinessRuleException(MensagemUltimoAdmin);
            }
        }
        // É uma data de calendário (como a admissão): sem data informada, vale
        // o dia de hoje em Brasília — UtcNow viraria o dia seguinte depois das 21h.
        var dataDesligamento = Datas.ValidarDesligamento(dto?.DataDesligamento, funcionario.DataAdmissao);

        // Os subordinados diretos vão, na MESMA transação, pro gestor de quem
        // está saindo (se ele estiver ativo) ou ficam sem gestor. Antes o
        // painel fazia isso com um PUT por pessoa e, se um falhasse no meio,
        // metade da equipe ficava pendurada num gestor desligado.
        var novoGestor = await GestorQueHerdaAsync(funcionario);
        var subordinados = await SubordinadosAtivosAsync(id);
        foreach (var subordinado in subordinados)
        {
            subordinado.GerenteId = novoGestor?.Id == subordinado.Id ? null : novoGestor?.Id;
        }

        funcionario.Status = StatusFuncionario.Desligado;
        funcionario.DataDesligamento = dataDesligamento;

        // O token do app de quem foi desligado para de valer na hora.
        if (contaLigada is not null)
        {
            contaLigada.TokenVersao++;
        }

        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);

        return new DesligamentoResultadoDto
        {
            Funcionario = await ObterPorIdAsync(id),
            SubordinadosRealocados = subordinados.Count,
            NovoGestorNome = subordinados.Count > 0 ? novoGestor?.Nome : null,
        };
    }

    // ── Remuneração ─────────────────────────────────────────────────────
    // Os três ajustes aplicam o novo valor no cadastro na hora (a data de
    // vigência fica só registrada, e por isso não pode ser futura) e gravam
    // uma linha no histórico, no mesmo SaveChanges — nunca fica valor
    // alterado sem o registro de quem alterou.

    public async Task<FuncionarioDto> AjustarSalarioAsync(int funcionarioId, AjusteSalarioDto dto, int responsavelUsuarioId)
    {
        await using var transacao = await _db.IniciarTransacaoAsync();
        await _db.TravarFuncionariosAsync(funcionarioId);

        var funcionario = await BuscarAtivoParaAjusteAsync(funcionarioId);
        Concorrencia.ConferirVersao(funcionario, dto.Versao);
        var novoSalario = Dinheiro.ValidarSalario(dto.NovoSalario);
        if (novoSalario == funcionario.Salario)
        {
            throw new BusinessRuleException(Dinheiro.MensagemIgualAoAtual);
        }
        var dataVigencia = Datas.ValidarVigencia(dto.DataVigencia, funcionario.DataAdmissao);
        var motivo = Textos.Validar(dto.Motivo, Textos.Motivo);

        _db.AjustesRemuneracao.Add(new AjusteRemuneracao
        {
            FuncionarioId = funcionario.Id,
            Tipo = TipoAjusteRemuneracao.Salario,
            ValorAnterior = funcionario.Salario,
            ValorNovo = novoSalario,
            DataVigencia = dataVigencia,
            Motivo = motivo,
            ResponsavelUsuarioId = responsavelUsuarioId,
            CriadoEm = DateTime.UtcNow,
        });
        funcionario.Salario = novoSalario;

        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);
        return await ObterPorIdAsync(funcionarioId);
    }

    public async Task<FuncionarioDto> AjustarValeAlimentacaoAsync(int funcionarioId, AjusteValeAlimentacaoDto dto, int responsavelUsuarioId)
    {
        await using var transacao = await _db.IniciarTransacaoAsync();
        await _db.TravarFuncionariosAsync(funcionarioId);

        var funcionario = await BuscarAtivoParaAjusteAsync(funcionarioId);
        Concorrencia.ConferirVersao(funcionario, dto.Versao);
        var novoValor = Dinheiro.ValidarVale(dto.NovoValor);
        if (novoValor == funcionario.ValeAlimentacao)
        {
            throw new BusinessRuleException(Dinheiro.MensagemIgualAoAtual);
        }
        var dataVigencia = Datas.ValidarVigencia(dto.DataVigencia, funcionario.DataAdmissao);
        var motivo = Textos.Validar(dto.Motivo, Textos.Motivo);

        _db.AjustesRemuneracao.Add(new AjusteRemuneracao
        {
            FuncionarioId = funcionario.Id,
            Tipo = TipoAjusteRemuneracao.ValeAlimentacao,
            ValorAnterior = funcionario.ValeAlimentacao,
            ValorNovo = novoValor,
            DataVigencia = dataVigencia,
            Motivo = motivo,
            ResponsavelUsuarioId = responsavelUsuarioId,
            CriadoEm = DateTime.UtcNow,
        });
        funcionario.ValeAlimentacao = novoValor;

        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);
        return await ObterPorIdAsync(funcionarioId);
    }

    /// <summary>
    /// Troca cargo e/ou departamento (e o salário, que não pode cair). Se o
    /// departamento muda, os subordinados diretos que ficam no departamento
    /// antigo passam pro gestor anterior da pessoa promovida — antes um
    /// gestor promovido pra outra área levava a equipe antiga junto.
    /// </summary>
    public async Task<PromocaoResultadoDto> PromoverAsync(int funcionarioId, PromocaoDto dto, int responsavelUsuarioId)
    {
        await using var transacao = await _db.IniciarTransacaoAsync();
        // Promoção mexe na hierarquia (novo gestor, equipe que muda de gestor):
        // trava global primeiro. Depois, antes de ler: a pessoa e o novo
        // gestor, na ordem do Id; o gestor atual, que pode herdar a equipe; e
        // os subordinados.
        await using var travaHierarquia = await _db.TravarGlobalAsync(AppDbContext.TravaHierarquia);
        await _db.TravarFuncionariosAsync(funcionarioId, dto.NovoGerenteId);
        await TravarGestorAtualAsync(funcionarioId);
        await _db.TravarSubordinadosAsync(funcionarioId);

        var funcionario = await BuscarAtivoParaAjusteAsync(funcionarioId);
        Concorrencia.ConferirVersao(funcionario, dto.Versao);
        var novoSalario = Dinheiro.ValidarSalario(dto.NovoSalario);
        var dataVigencia = Datas.ValidarVigencia(dto.DataVigencia, funcionario.DataAdmissao);
        var motivo = Textos.Validar(dto.Motivo, Textos.Motivo);

        var novoCargoId = dto.NovoCargoId ?? throw new BusinessRuleException("Selecione o novo cargo.");
        var novoDepartamentoId = dto.NovoDepartamentoId ?? throw new BusinessRuleException("Selecione o novo departamento.");
        var novoCargo = await _db.Cargos.FindAsync(novoCargoId)
            ?? throw new NotFoundException("Cargo informado não existe.");
        var novoDepartamento = await _db.Departamentos.FindAsync(novoDepartamentoId)
            ?? throw new NotFoundException("Departamento informado não existe.");

        // Promoção é troca de cargo e/ou departamento. Sem isso:
        //  - só o salário mudou → o caminho é o Ajuste Salarial;
        //  - nada mudou, ou só o gestor → 400 (pra trocar só o gestor, edite o cadastro).
        // Duas promoções iguais ao mesmo tempo: com a versão da tela, a segunda
        // já recebeu 409 acima.
        var mudaCargo = funcionario.CargoId != novoCargo.Id;
        var mudaDepartamento = funcionario.DepartamentoId != novoDepartamento.Id;
        if (!mudaCargo && !mudaDepartamento)
        {
            var mudaGestor = dto.NovoGerenteId is not null && dto.NovoGerenteId != funcionario.GerenteId;
            var soSalario = novoSalario != funcionario.Salario && !mudaGestor;
            throw new BusinessRuleException(soSalario ? MensagemPromocaoSoSalario : MensagemPromocaoSemMudanca);
        }
        if (novoSalario < funcionario.Salario)
        {
            throw new BusinessRuleException("Promoção não pode reduzir o salário.");
        }

        // Gestor informado é SEMPRE validado (ativo, sem ciclo), mesmo que seja
        // o atual: antes, repetir o gestor atual já desligado passava.
        var novoGerenteId = dto.NovoGerenteId ?? funcionario.GerenteId;
        if (dto.NovoGerenteId is not null)
        {
            await ValidarGestorAsync(funcionarioId, dto.NovoGerenteId);
        }

        var realocados = 0;
        if (mudaDepartamento)
        {
            var herdeiro = await GestorQueHerdaAsync(funcionario);
            var departamentoAntigoId = funcionario.DepartamentoId;
            var equipeAntiga = (await SubordinadosAtivosAsync(funcionarioId))
                .Where(s => s.DepartamentoId == departamentoAntigoId)
                .ToList();
            foreach (var subordinado in equipeAntiga)
            {
                subordinado.GerenteId = herdeiro?.Id == subordinado.Id ? null : herdeiro?.Id;
            }
            realocados = equipeAntiga.Count;
        }

        _db.AjustesRemuneracao.Add(new AjusteRemuneracao
        {
            FuncionarioId = funcionario.Id,
            Tipo = TipoAjusteRemuneracao.Promocao,
            ValorAnterior = funcionario.Salario,
            ValorNovo = novoSalario,
            CargoAnteriorId = funcionario.CargoId,
            CargoNovoId = novoCargo.Id,
            DepartamentoAnteriorId = funcionario.DepartamentoId,
            DepartamentoNovoId = novoDepartamento.Id,
            DataVigencia = dataVigencia,
            Motivo = motivo,
            ResponsavelUsuarioId = responsavelUsuarioId,
            CriadoEm = DateTime.UtcNow,
        });

        // Troca a navegação junto com o Id: o funcionário veio com Cargo e
        // Departamento carregados, e os dois precisam apontar pro mesmo lugar.
        funcionario.CargoId = novoCargo.Id;
        funcionario.Cargo = novoCargo;
        funcionario.DepartamentoId = novoDepartamento.Id;
        funcionario.Departamento = novoDepartamento;
        funcionario.Salario = novoSalario;
        funcionario.GerenteId = novoGerenteId;

        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);

        return new PromocaoResultadoDto
        {
            Funcionario = await ObterPorIdAsync(funcionarioId),
            SubordinadosRealocados = realocados,
        };
    }

    /// <summary>Histórico de remuneração do funcionário, mais recentes primeiro.</summary>
    public async Task<List<AjusteRemuneracaoDto>> ListarAjustesRemuneracaoAsync(int funcionarioId)
    {
        var funcionarioExiste = await _db.Funcionarios.AnyAsync(f => f.Id == funcionarioId);
        if (!funcionarioExiste)
        {
            throw new NotFoundException("Funcionário não encontrado.");
        }

        return await ListarAjustesSemConferirAsync(_db, funcionarioId);
    }

    /// <summary>Usado também pelo /api/me (o colaborador vendo o próprio histórico).</summary>
    public static async Task<List<AjusteRemuneracaoDto>> ListarAjustesSemConferirAsync(AppDbContext db, int funcionarioId)
    {
        // Id no desempate: dois ajustes gravados no mesmo instante continuam
        // na ordem em que foram feitos.
        var ajustes = await db.AjustesRemuneracao
            .Include(a => a.CargoAnterior)
            .Include(a => a.CargoNovo)
            .Include(a => a.DepartamentoAnterior)
            .Include(a => a.DepartamentoNovo)
            .Include(a => a.ResponsavelUsuario)
            .Where(a => a.FuncionarioId == funcionarioId)
            .OrderByDescending(a => a.CriadoEm)
            .ThenByDescending(a => a.Id)
            .ToListAsync();

        return ajustes.Select(ParaAjusteDto).ToList();
    }

    private async Task<Funcionario> BuscarAtivoParaAjusteAsync(int funcionarioId)
    {
        var funcionario = await BuscarOuFalhar(funcionarioId);
        if (funcionario.Status == StatusFuncionario.Desligado)
        {
            throw new BusinessRuleException("Não é possível ajustar a remuneração de um colaborador desligado.");
        }
        return funcionario;
    }

    private async Task<decimal> SalarioBaseDoCargoAsync(int cargoId)
    {
        var salarioBase = await _db.Cargos.Where(c => c.Id == cargoId).Select(c => c.SalarioBase).SingleAsync();
        if (salarioBase < Dinheiro.SalarioMinimo || salarioBase > Dinheiro.SalarioMaximo)
        {
            throw new BusinessRuleException("Informe o salário: o cargo escolhido não tem um salário base válido.");
        }
        return decimal.Round(salarioBase, 2);
    }

    private async Task GarantirDepartamentoECargoExistemAsync(int departamentoId, int cargoId)
    {
        var departamentoExiste = await _db.Departamentos.AnyAsync(d => d.Id == departamentoId);
        if (!departamentoExiste)
        {
            throw new NotFoundException("Departamento informado não existe.");
        }

        var cargoExiste = await _db.Cargos.AnyAsync(c => c.Id == cargoId);
        if (!cargoExiste)
        {
            throw new NotFoundException("Cargo informado não existe.");
        }
    }

    // ── Hierarquia ──────────────────────────────────────────────────────
    // Gestor de outro departamento é permitido (a Diretoria gerencia as
    // outras áreas), mas ele precisa existir, estar ativo e não pode estar
    // abaixo do próprio colaborador — A gestor de B e B gestor de A deixava
    // gente "solta" no Organograma e fazia o desligamento falhar no meio.

    private async Task ValidarGestorAsync(int? funcionarioId, int? gerenteId)
    {
        if (gerenteId is null)
        {
            return;
        }
        if (funcionarioId == gerenteId)
        {
            throw new BusinessRuleException("Um funcionário não pode ser gestor de si mesmo.");
        }

        var statusDoGerente = await _db.Funcionarios.AsNoTracking()
            .Where(f => f.Id == gerenteId)
            .Select(f => (StatusFuncionario?)f.Status)
            .FirstOrDefaultAsync();
        if (statusDoGerente is null)
        {
            throw new NotFoundException("Gestor informado não existe.");
        }
        if (statusDoGerente != StatusFuncionario.Ativo)
        {
            throw new BusinessRuleException("O gestor escolhido está desligado. Escolha um gestor ativo.");
        }

        if (funcionarioId is int id && CadeiaPassaPor(await MapaDeGestoresAsync(), gerenteId.Value, id))
        {
            throw new BusinessRuleException(
                "Esse gestor está abaixo deste colaborador na hierarquia: a escolha criaria um ciclo de gestores.");
        }
    }

    /// <summary>
    /// Quem herda a equipe de alguém que sai (desligamento) ou muda de área
    /// (promoção): o gestor dessa pessoa, se existir, estiver ativo e não
    /// estiver abaixo dela (ciclo antigo no banco). Senão, ninguém.
    /// </summary>
    private async Task<Funcionario?> GestorQueHerdaAsync(Funcionario funcionario)
    {
        if (funcionario.GerenteId is not int gerenteId || gerenteId == funcionario.Id)
        {
            return null;
        }

        var gerente = await _db.Funcionarios.FirstOrDefaultAsync(f => f.Id == gerenteId);
        if (gerente is null || gerente.Status != StatusFuncionario.Ativo)
        {
            return null;
        }

        return CadeiaPassaPor(await MapaDeGestoresAsync(), gerenteId, funcionario.Id) ? null : gerente;
    }

    /// <summary>
    /// Trava o gestor atual de alguém cuja linha JÁ está travada (então o
    /// GerenteId lido aqui não muda mais até o fim da transação).
    /// </summary>
    private async Task TravarGestorAtualAsync(int funcionarioId)
    {
        if (!_db.Database.IsRelational())
        {
            return;
        }
        var gerenteAtualId = await _db.Funcionarios.AsNoTracking()
            .Where(f => f.Id == funcionarioId)
            .Select(f => f.GerenteId)
            .FirstOrDefaultAsync();
        await _db.TravarFuncionariosAsync(gerenteAtualId);
    }

    private Task<List<Funcionario>> SubordinadosAtivosAsync(int gerenteId) =>
        _db.Funcionarios
            .Where(f => f.GerenteId == gerenteId && f.Id != gerenteId && f.Status == StatusFuncionario.Ativo)
            .ToListAsync();

    private Task<Dictionary<int, int?>> MapaDeGestoresAsync() =>
        _db.Funcionarios.AsNoTracking()
            .Select(f => new { f.Id, f.GerenteId })
            .ToDictionaryAsync(f => f.Id, f => f.GerenteId);

    /// <summary>Sobe a cadeia de gestores a partir de <paramref name="inicioId"/> e diz se ela passa por <paramref name="alvoId"/>.</summary>
    private static bool CadeiaPassaPor(Dictionary<int, int?> gestorDe, int inicioId, int alvoId)
    {
        var visitados = new HashSet<int>();
        int? atual = inicioId;
        while (atual is int id && visitados.Add(id))
        {
            if (id == alvoId)
            {
                return true;
            }
            atual = gestorDe.GetValueOrDefault(id);
        }
        return false;
    }

    // ── Acesso ao app ───────────────────────────────────────────────────

    /// <summary>
    /// Conta RH/Admin ligada a um colaborador: senha, e-mail de login e
    /// desligamento só por um Admin (403 pro RH). Sem isso, um RH redefinia
    /// a senha de uma conta Admin e entrava como Admin. O perfil de quem pede
    /// é lido do banco, não do token.
    /// </summary>
    private async Task ExigirAdminParaContaPrivilegiadaAsync(Usuario conta, int? solicitanteId, string mensagem)
    {
        if (conta.Perfil == PerfilUsuario.Funcionario || solicitanteId is null)
        {
            return;
        }

        var perfilDoSolicitante = await _db.Usuarios.AsNoTracking()
            .Where(u => u.Id == solicitanteId)
            .Select(u => (PerfilUsuario?)u.Perfil)
            .FirstOrDefaultAsync();
        if (perfilDoSolicitante != PerfilUsuario.Admin)
        {
            throw new ForbiddenException(mensagem);
        }
    }

    /// <summary>Conta nova de perfil Funcionario, já ligada ao colaborador (a senha é definida por quem chama).</summary>
    private async Task<Usuario> NovaContaAsync(Funcionario funcionario, string email)
    {
        var cargo = funcionario.Cargo ?? await _db.Cargos.FindAsync(funcionario.CargoId);
        var departamento = funcionario.Departamento ?? await _db.Departamentos.FindAsync(funcionario.DepartamentoId);

        return new Usuario
        {
            Nome = funcionario.Nome,
            Email = email,
            Perfil = PerfilUsuario.Funcionario,
            Cargo = cargo?.Nome ?? string.Empty,
            Departamento = departamento?.Nome ?? string.Empty,
            Funcionario = funcionario,
            CriadoEm = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Grava e, se um índice único segurar uma duplicata que passou na
    /// conferência (dois envios ao mesmo tempo), devolve 409 com a mensagem
    /// certa em vez de 500.
    /// </summary>
    private async Task SalvarTratandoDuplicidadeAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ErrosBanco.IndiceDuplicado(ex) is { } indice)
        {
            throw new ConflictException(indice switch
            {
                "IX_Funcionarios_Cpf" => MensagemCpfDuplicado,
                "IX_Funcionarios_Email" => MensagemEmailDuplicado,
                "IX_Usuarios_Email" => MensagemContaComMesmoEmail,
                _ => ErrosBanco.MensagemDuplicado,
            });
        }
    }

    /// <summary>
    /// Conta as ausências de qualquer Usuario vinculado a esse funcionário, e
    /// já aproveita a mesma consulta pra saber se existe algum vinculado.
    /// </summary>
    private async Task<(int Total, bool PossuiAcessoApp)> ContarSolicitacoesAsync(int funcionarioId)
    {
        var contagensPorUsuario = await _db.Usuarios
            .Where(u => u.FuncionarioId == funcionarioId)
            .Select(u => u.Ausencias.Count)
            .ToListAsync();

        return (contagensPorUsuario.Sum(), contagensPorUsuario.Count > 0);
    }

    /// <summary>Mesma contagem acima, mas pra todos os funcionários de uma vez (evita 1 query por linha ao listar).</summary>
    private async Task<Dictionary<int, int>> ContarSolicitacoesPorFuncionarioAsync()
    {
        var contagens = await _db.Usuarios
            .Where(u => u.FuncionarioId != null)
            .Select(u => new { FuncionarioId = u.FuncionarioId!.Value, Total = u.Ausencias.Count })
            .ToListAsync();

        return contagens
            .GroupBy(c => c.FuncionarioId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Total));
    }

    private async Task<Funcionario> BuscarOuFalhar(int id)
    {
        var funcionario = await _db.Funcionarios
            .Include(f => f.Departamento)
            .Include(f => f.Cargo)
            .Include(f => f.Gerente)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (funcionario is null)
        {
            throw new NotFoundException("Funcionário não encontrado.");
        }

        return funcionario;
    }

    public static FuncionarioDto ParaDto(Funcionario funcionario, int totalSolicitacoes, bool possuiAcessoApp) => new()
    {
        Id = funcionario.Id,
        Nome = funcionario.Nome,
        Cpf = funcionario.Cpf,
        Email = funcionario.Email,
        Telefone = funcionario.Telefone,
        DataNascimento = funcionario.DataNascimento,
        DataAdmissao = funcionario.DataAdmissao,
        DataDesligamento = funcionario.DataDesligamento,
        Status = funcionario.Status,
        DepartamentoId = funcionario.DepartamentoId,
        DepartamentoNome = funcionario.Departamento?.Nome ?? string.Empty,
        CargoId = funcionario.CargoId,
        CargoNome = funcionario.Cargo?.Nome ?? string.Empty,
        Salario = funcionario.Salario,
        ValeAlimentacao = funcionario.ValeAlimentacao,
        GerenteId = funcionario.GerenteId,
        GerenteNome = funcionario.Gerente?.Nome,
        TotalSolicitacoesAusencia = totalSolicitacoes,
        PossuiAcessoApp = possuiAcessoApp,
        Versao = funcionario.Versao,
    };

    private static AjusteRemuneracaoDto ParaAjusteDto(AjusteRemuneracao ajuste) => new()
    {
        Id = ajuste.Id,
        Tipo = ajuste.Tipo,
        ValorAnterior = ajuste.ValorAnterior,
        ValorNovo = ajuste.ValorNovo,
        CargoAnteriorNome = ajuste.CargoAnterior?.Nome,
        CargoNovoNome = ajuste.CargoNovo?.Nome,
        DepartamentoAnteriorNome = ajuste.DepartamentoAnterior?.Nome,
        DepartamentoNovoNome = ajuste.DepartamentoNovo?.Nome,
        DataVigencia = ajuste.DataVigencia,
        Motivo = ajuste.Motivo,
        ResponsavelNome = ajuste.ResponsavelUsuario?.Nome,
        CriadoEm = ajuste.CriadoEm,
    };
}
