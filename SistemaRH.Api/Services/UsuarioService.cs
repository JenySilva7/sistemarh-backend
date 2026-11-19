using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IUsuarioService
{
    Task<List<UsuarioListaDto>> ListarAsync();
    Task<UsuarioListaDto> AlterarPerfilAsync(int solicitanteId, int usuarioId, AlterarPerfilUsuarioDto dto);
}

/// <summary>
/// Contas de acesso e seus perfis — o que a tela de Permissões do painel
/// mostra e altera. Antes essa tela era fixa no código e o "Salvar" não
/// gravava nada; promover alguém a RH/Admin só dava editando o banco.
/// </summary>
public class UsuarioService : IUsuarioService
{
    public const string MensagemPerfilInvalido = "Perfil inválido.";
    public const string MensagemSenhaProvisoria = "Defina uma senha provisória para a conta promovida.";

    private readonly AppDbContext _db;
    private readonly PasswordHasher<Usuario> _passwordHasher = new();

    public UsuarioService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<UsuarioListaDto>> ListarAsync()
    {
        var usuarios = await _db.Usuarios
            .Include(u => u.Funcionario)
            .OrderBy(u => u.Nome)
            .ThenBy(u => u.Id)
            .ToListAsync();

        return usuarios.Select(u => AuthService.ParaListaDto(u, u.Funcionario)).ToList();
    }

    public async Task<UsuarioListaDto> AlterarPerfilAsync(int solicitanteId, int usuarioId, AlterarPerfilUsuarioDto dto)
    {
        var novoPerfil = Enums.Validar(dto.Perfil, "Selecione o perfil.", MensagemPerfilInvalido);
        // perfilAtual 9 não é "outro Admin mudou antes" (409): é um valor
        // que nem existe.
        if (dto.PerfilAtual is PerfilUsuario perfilAtual && !Enum.IsDefined(perfilAtual))
        {
            throw new BusinessRuleException(MensagemPerfilInvalido);
        }
        if (solicitanteId == usuarioId)
        {
            throw new BusinessRuleException("Você não pode alterar o seu próprio perfil.");
        }

        await using var transacao = await _db.IniciarTransacaoAsync();
        // Uma troca de perfil por vez (trava global) e, dentro dela, o perfil
        // de quem pede é relido do banco: o token pode ser de antes de essa
        // pessoa ser rebaixada por outro Admin. Antes, dois Admins se
        // rebaixando ao mesmo tempo terminavam os dois como Funcionário.
        // O "Criar acesso ao app", a edição do colaborador (e-mail de login)
        // e o desligamento pegam esta mesma trava e travam a linha da conta
        // antes de ler o perfil: nenhum deles grava na conta no meio de uma
        // promoção.
        await using var travaPerfis = await _db.TravarGlobalAsync(AppDbContext.TravaPerfis);
        await _db.TravarAdminsAsync();
        await _db.TravarUsuarioAsync(usuarioId);

        var solicitanteEhAdminAtivo = await _db.Usuarios.AnyAsync(u =>
            u.Id == solicitanteId
            && u.Perfil == PerfilUsuario.Admin
            && (u.FuncionarioId == null || u.Funcionario!.Status == StatusFuncionario.Ativo));
        if (!solicitanteEhAdminAtivo)
        {
            throw new ForbiddenException("Somente um administrador pode alterar perfis.");
        }

        var usuario = await _db.Usuarios
            .Include(u => u.Funcionario)
            .FirstOrDefaultAsync(u => u.Id == usuarioId)
            ?? throw new NotFoundException("Usuário não encontrado.");

        // A tela manda o perfil que estava mostrando: se outro Admin mudou
        // nesse meio-tempo, 409 em vez de sobrescrever a mudança dele.
        if (dto.PerfilAtual is not null && dto.PerfilAtual != usuario.Perfil)
        {
            throw new ConflictException(Concorrencia.MensagemConflito);
        }

        if (usuario.Perfil != novoPerfil)
        {
            // Subir o perfil troca a senha pela provisória que o Admin
            // definiu: a senha de uma conta de colaborador é escolhida pelo
            // RH (no cadastro ou no "Criar acesso ao app"), e sem isso o RH
            // entrava como Admin na conta que acabou de ser promovida.
            var promocao = Nivel(novoPerfil) > Nivel(usuario.Perfil);
            if (promocao)
            {
                if (string.IsNullOrEmpty(dto.SenhaProvisoria))
                {
                    throw new BusinessRuleException(MensagemSenhaProvisoria);
                }
                Senhas.Validar(dto.SenhaProvisoria, "A senha provisória");
                usuario.SenhaHash = _passwordHasher.HashPassword(usuario, dto.SenhaProvisoria);
            }

            var alvoEhAdminAtivo = usuario.Perfil == PerfilUsuario.Admin
                && usuario.Funcionario?.Status != StatusFuncionario.Desligado;
            if (alvoEhAdminAtivo && await ContarAdminsAtivosAsync(_db, excetoUsuarioId: usuarioId) == 0)
            {
                throw new BusinessRuleException("O sistema precisa ter pelo menos um administrador.");
            }

            usuario.Perfil = novoPerfil;
            // O perfil vai no token: os tokens antigos (com o perfil velho) param de valer.
            usuario.TokenVersao++;
            await _db.SaveChangesAsync();
        }

        await AppDbContext.ConfirmarAsync(transacao);
        return AuthService.ParaListaDto(usuario, usuario.Funcionario);
    }

    private static int Nivel(PerfilUsuario perfil) => perfil switch
    {
        PerfilUsuario.Admin => 2,
        PerfilUsuario.Rh => 1,
        _ => 0,
    };

    /// <summary>
    /// Admins que conseguem entrar: conta Admin sem colaborador ligado, ou
    /// ligada a um colaborador ativo. Um Admin cujo colaborador foi desligado
    /// não entra mais, então não conta pra regra "pelo menos um Admin".
    /// </summary>
    public static Task<int> ContarAdminsAtivosAsync(AppDbContext db, int excetoUsuarioId) =>
        db.Usuarios.CountAsync(u =>
            u.Perfil == PerfilUsuario.Admin
            && u.Id != excetoUsuarioId
            && (u.FuncionarioId == null || u.Funcionario!.Status == StatusFuncionario.Ativo));
}
