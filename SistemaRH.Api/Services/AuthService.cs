using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IAuthService
{
    /// <summary>Cria uma conta de acesso. Só um Admin chama (ver AuthController).</summary>
    Task<UsuarioListaDto> RegistrarAsync(RegisterDto dto);
    /// <param name="ip">IP de quem tenta entrar (o limite de tentativas é por e-mail+IP e por IP).</param>
    Task<AuthResponseDto> LoginAsync(LoginDto dto, string? ip = null);
    Task<UsuarioDto> ObterUsuarioAsync(int usuarioId);

    /// <summary>Invalida todos os tokens já emitidos pra essa conta.</summary>
    Task LogoutAsync(int usuarioId);

    /// <summary>
    /// A própria pessoa troca a senha (confirmando a atual). Derruba as outras
    /// sessões e devolve um token novo pra esta continuar.
    /// </summary>
    /// <param name="ip">IP de quem pede: a senha atual errada conta no mesmo limite do login.</param>
    Task<TokenDto> TrocarSenhaAsync(int usuarioId, TrocarSenhaDto dto, string? ip = null);
}

/// <summary>
/// Concentra as regras de negócio do login/cadastro. O Controller só recebe
/// a requisição e chama esse serviço — toda a lógica "de verdade" mora aqui.
///
/// Sobre a senha: nunca comparamos "senha digitada == senha salva". Usamos o
/// <see cref="PasswordHasher{TUser}"/> do ASP.NET Core, que:
///   1. No cadastro, transforma a senha num hash (um "resumo" irreversível,
///      com um "sal" aleatório misturado) e salva só esse hash.
///   2. No login, pega a senha digitada, aplica o mesmo algoritmo de hash e
///      compara o resultado com o hash salvo — sem nunca descobrir a senha
///      original de volta. Mesmo se o banco vazar, ninguém recupera a senha.
/// </summary>
public class AuthService : IAuthService
{
    public const string MensagemCredencialInvalida = "E-mail ou senha incorretos.";
    public const string MensagemAcessoDesativado = "Seu acesso foi desativado. Procure o RH.";
    // Começo da mensagem do 429: no login e na troca da própria senha (que
    // soma no mesmo limite, mas a pessoa não estava fazendo login).
    public const string LimiteDoLogin = "Muitas tentativas de login.";
    public const string LimiteDaSenhaAtual = "Muitas tentativas com a senha atual incorreta.";
    public const string MensagemSenhaAtualIncorreta = "Senha atual incorreta.";
    public const string MensagemSenhaAtualObrigatoria = "Informe a senha atual.";
    public const string MensagemNovaSenhaIgual = "A nova senha precisa ser diferente da atual.";

    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILimiteTentativasLogin _limiteTentativas;
    private readonly PasswordHasher<Usuario> _passwordHasher = new();

    // Hash de uma senha aleatória, calculado uma vez só. Quando o e-mail do
    // login não existe, a senha digitada é conferida contra ele mesmo assim:
    // a resposta leva o mesmo tempo de um e-mail que existe (antes, 20 ms
    // contra 60 ms denunciavam quais e-mails tinham conta).
    private static readonly Lazy<string> HashFicticio = new(() =>
        new PasswordHasher<Usuario>().HashPassword(new Usuario(), Guid.NewGuid().ToString("N")));

    public AuthService(AppDbContext db, IJwtTokenService jwtTokenService, ILimiteTentativasLogin? limiteTentativas = null)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _limiteTentativas = limiteTentativas ?? new LimiteTentativasLogin();
    }

    public async Task<UsuarioListaDto> RegistrarAsync(RegisterDto dto)
    {
        var nome = Textos.ValidarNomePessoa(dto.Nome);
        var email = Emails.Normalizar(dto.Email);
        Senhas.Validar(dto.Senha, "A senha");
        var perfil = dto.Perfil ?? PerfilUsuario.Funcionario;
        if (!Enum.IsDefined(perfil))
        {
            throw new BusinessRuleException("Perfil inválido.");
        }

        if (await _db.Usuarios.AnyAsync(u => u.Email == email))
        {
            throw new ConflictException("Já existe uma conta cadastrada com esse e-mail.");
        }

        // Só os campos do DTO entram: saldo do banco de horas, cargo e
        // departamento nunca vêm do cliente.
        var usuario = new Usuario
        {
            Nome = nome,
            Email = email,
            Perfil = perfil,
            CriadoEm = DateTime.UtcNow,
        };
        // A senha ainda não existe no objeto — o hasher usa "usuario" só como
        // referência de tipo, então é seguro calcular o hash antes de ter Id.
        usuario.SenhaHash = _passwordHasher.HashPassword(usuario, dto.Senha!);

        _db.Usuarios.Add(usuario);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ErrosBanco.IndiceDuplicado(ex) is not null)
        {
            // Dois cadastros iguais ao mesmo tempo: o índice único segurou.
            throw new ConflictException("Já existe uma conta cadastrada com esse e-mail.");
        }

        // Conta nova nunca nasce vinculada a um funcionário: o vínculo só é
        // feito pelo RH (FuncionarioService.DefinirAcessoAppAsync).
        return ParaListaDto(usuario, funcionario: null);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto, string? ip = null)
    {
        // De propósito, a mensagem de erro é igual tanto pra "usuário não existe"
        // quanto pra "senha errada". Se disséssemos "e-mail não encontrado",
        // alguém mal-intencionado conseguiria descobrir quais e-mails têm
        // conta só testando — isso se chama "user enumeration".
        // E-mail com acento ou caractere de controle nem é procurado: a
        // collation do banco ignora acento, então "zédalima@" entrava na
        // conta de "zedalima@".
        if (!Emails.TryNormalizar(dto.Email, out var email))
        {
            throw new BusinessRuleException(MensagemCredencialInvalida);
        }

        // A tentativa é reservada ANTES de conferir a senha: requisições em
        // paralelo não passam todas pela checagem antes de a primeira falha
        // ser contada (ver LimiteTentativasLogin).
        var origem = Origem(ip);
        await ReservarTentativaAsync(email, origem, LimiteDoLogin);

        Usuario usuario;
        var concluida = false;
        try
        {
            var senha = dto.Senha ?? string.Empty;
            var encontrado = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email);
            var resultado = encontrado is null
                ? Falhar(_passwordHasher.VerifyHashedPassword(new Usuario(), HashFicticio.Value, senha))
                : _passwordHasher.VerifyHashedPassword(encontrado, encontrado.SenhaHash, senha);
            if (encontrado is null || resultado == PasswordVerificationResult.Failed)
            {
                concluida = true;
                _limiteTentativas.RegistrarFalha(email, origem);
                throw new BusinessRuleException(MensagemCredencialInvalida);
            }

            concluida = true;
            _limiteTentativas.RegistrarSucesso(email, origem);
            usuario = encontrado;
        }
        finally
        {
            // Erro inesperado no meio (ex.: banco fora): a reserva é devolvida
            // sem contar como senha errada.
            if (!concluida)
            {
                _limiteTentativas.Cancelar(email, origem);
            }
        }

        // Só checado depois da senha, pra não revelar a quem não sabe a
        // senha que essa conta existe e foi desativada.
        var funcionario = await BuscarFuncionarioVinculadoAsync(usuario);
        if (funcionario?.Status == StatusFuncionario.Desligado)
        {
            throw new BusinessRuleException(MensagemAcessoDesativado);
        }

        return new AuthResponseDto
        {
            Token = _jwtTokenService.GerarToken(usuario),
            Usuario = await ParaDtoAsync(usuario, funcionario),
        };
    }

    public async Task<UsuarioDto> ObterUsuarioAsync(int usuarioId)
    {
        var usuario = await _db.Usuarios.FindAsync(usuarioId);
        if (usuario is null)
        {
            throw new NotFoundException("Usuário não encontrado.");
        }

        // O token de quem foi desligado já é recusado na autenticação (a
        // TokenVersao sobe no desligamento); fica aqui também como defesa extra.
        var funcionario = await BuscarFuncionarioVinculadoAsync(usuario);
        if (funcionario?.Status == StatusFuncionario.Desligado)
        {
            throw new ForbiddenException(MensagemAcessoDesativado);
        }

        return await ParaDtoAsync(usuario, funcionario);
    }

    public async Task LogoutAsync(int usuarioId)
    {
        var usuario = await _db.Usuarios.FindAsync(usuarioId)
            ?? throw new NotFoundException("Usuário não encontrado.");

        // Sobe a versão: todo token dessa conta (de qualquer aba ou aparelho)
        // deixa de valer na próxima requisição.
        usuario.TokenVersao++;
        await _db.SaveChangesAsync();
    }

    public async Task<TokenDto> TrocarSenhaAsync(int usuarioId, TrocarSenhaDto dto, string? ip = null)
    {
        if (string.IsNullOrEmpty(dto.SenhaAtual))
        {
            throw new BusinessRuleException(MensagemSenhaAtualObrigatoria);
        }
        Senhas.Validar(dto.NovaSenha, "A nova senha");
        // Comparar com o que foi digitado em "senha atual" basta: se ela
        // estiver certa, é a senha da conta; se estiver errada, a troca
        // para logo abaixo de qualquer jeito.
        if (dto.NovaSenha == dto.SenhaAtual)
        {
            throw new BusinessRuleException(MensagemNovaSenhaIgual);
        }

        var email = await _db.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Usuário não encontrado.");

        // A senha atual errada conta no mesmo limite do login (e-mail+IP e
        // IP): sem isso, quem pegasse um celular com a sessão aberta testava
        // senhas aqui à vontade. A reserva vem antes da transação, pra espera
        // da vez não segurar a linha da conta travada.
        var origem = Origem(ip);
        await ReservarTentativaAsync(email, origem, LimiteDaSenhaAtual);

        var concluida = false;
        try
        {
            await using var transacao = await _db.IniciarTransacaoAsync();
            // A conta é travada e relida: uma promoção ao mesmo tempo (que
            // troca a senha pela provisória) ou espera esta troca terminar e
            // sobrescreve a senha, ou termina antes e a senha atual digitada
            // aqui (a antiga) deixa de conferir.
            await _db.TravarUsuarioAsync(usuarioId);
            var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId)
                ?? throw new NotFoundException("Usuário não encontrado.");

            var funcionario = await BuscarFuncionarioVinculadoAsync(usuario);
            if (funcionario?.Status == StatusFuncionario.Desligado)
            {
                throw new ForbiddenException(MensagemAcessoDesativado);
            }

            if (_passwordHasher.VerifyHashedPassword(usuario, usuario.SenhaHash, dto.SenhaAtual) == PasswordVerificationResult.Failed)
            {
                concluida = true;
                _limiteTentativas.RegistrarFalha(email, origem);
                throw new BusinessRuleException(MensagemSenhaAtualIncorreta);
            }
            concluida = true;
            _limiteTentativas.RegistrarSucesso(email, origem);

            usuario.SenhaHash = _passwordHasher.HashPassword(usuario, dto.NovaSenha!);
            // Derruba as sessões abertas com a senha antiga (outros aparelhos,
            // ou quem a conhecia); quem trocou continua com o token novo.
            usuario.TokenVersao++;
            await _db.SaveChangesAsync();
            await AppDbContext.ConfirmarAsync(transacao);

            return new TokenDto { Token = _jwtTokenService.GerarToken(usuario) };
        }
        finally
        {
            if (!concluida)
            {
                _limiteTentativas.Cancelar(email, origem);
            }
        }
    }

    private static string Origem(string? ip) => string.IsNullOrWhiteSpace(ip) ? "desconhecido" : ip;

    /// <summary>Espera a vez no limite de tentativas; com bloqueio (ou fila parada), 429 com o tempo de espera.</summary>
    private async Task ReservarTentativaAsync(string email, string origem, string motivo)
    {
        if (await _limiteTentativas.ReservarAsync(email, origem) is not TimeSpan restante)
        {
            return;
        }
        // Menos de um minuto: é a fila que não andou (ou o fim de um
        // bloqueio), não "15 minutos" — antes, logins certos em paralelo
        // recebiam "tente em 15 minutos" sem bloqueio nenhum.
        if (restante < TimeSpan.FromMinutes(1))
        {
            throw new TooManyRequestsException($"{motivo} Aguarde alguns segundos e tente de novo.", restante);
        }
        var minutos = (int)Math.Ceiling(restante.TotalMinutes);
        throw new TooManyRequestsException($"{motivo} Tente novamente em {minutos} minuto(s).", restante);
    }

    /// <summary>Com e-mail inexistente, o hash fictício roda só pelo tempo gasto; o resultado é sempre falha.</summary>
    private static PasswordVerificationResult Falhar(PasswordVerificationResult _) => PasswordVerificationResult.Failed;

    private async Task<Funcionario?> BuscarFuncionarioVinculadoAsync(Usuario usuario)
    {
        if (usuario.FuncionarioId is not int funcionarioId)
        {
            return null;
        }

        return await _db.Funcionarios
            .Include(f => f.Cargo)
            .Include(f => f.Departamento)
            .Include(f => f.Gerente)
            .FirstOrDefaultAsync(f => f.Id == funcionarioId);
    }

    public static UsuarioListaDto ParaListaDto(Usuario usuario, Funcionario? funcionario) => new()
    {
        Id = usuario.Id,
        Nome = usuario.Nome,
        Email = usuario.Email,
        Perfil = usuario.Perfil,
        FuncionarioId = usuario.FuncionarioId,
        FuncionarioNome = funcionario?.Nome,
        FuncionarioDesligado = funcionario?.Status == StatusFuncionario.Desligado,
    };

    /// <summary>
    /// Monta o UsuarioDto, unificando os dois "banco de horas" do projeto:
    /// se essa conta estiver ligada a um <see cref="Funcionario"/>
    /// (<see cref="Usuario.FuncionarioId"/> preenchido), o saldo vem do
    /// extrato de verdade (<see cref="LancamentoBancoHoras"/>, somando
    /// créditos e subtraindo débitos). Senão, cai no campo "achatado"
    /// <see cref="Usuario.SaldoBancoHorasMinutos"/> — é o caso de contas
    /// antigas/demo que nunca foram ligadas a um funcionário.
    ///
    /// Pelo mesmo motivo, com vínculo, cargo/departamento/admissão/gestor
    /// vêm do cadastro do RH (fonte oficial), não dos campos da conta.
    /// </summary>
    private async Task<UsuarioDto> ParaDtoAsync(Usuario usuario, Funcionario? funcionario)
    {
        var saldoMinutos = usuario.FuncionarioId is int funcionarioId
            ? await LancamentoBancoHorasService.CalcularSaldoAsync(_db, funcionarioId)
            : usuario.SaldoBancoHorasMinutos;

        var totalSolicitacoes = await _db.Ausencias.CountAsync(a => a.UsuarioId == usuario.Id);

        return new UsuarioDto
        {
            Id = usuario.Id,
            Nome = usuario.Nome,
            Email = usuario.Email,
            Cargo = funcionario?.Cargo?.Nome ?? usuario.Cargo,
            Departamento = funcionario?.Departamento?.Nome ?? usuario.Departamento,
            SaldoBancoHorasMinutos = saldoMinutos,
            TotalSolicitacoesAusencia = totalSolicitacoes,
            FuncionarioId = funcionario?.Id,
            DataAdmissao = funcionario?.DataAdmissao,
            GerenteNome = funcionario?.Gerente?.Nome,
        };
    }
}
