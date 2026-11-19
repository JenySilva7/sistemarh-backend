using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

/// <summary>
/// Conferência feita a cada requisição autenticada, depois de o token passar
/// na assinatura e na validade (JwtBearerEvents.OnTokenValidated, ver
/// Program.cs). Assinatura e validade não bastam: aqui conferimos no banco se
/// a conta ainda existe, se a versão do token ("tv") é a atual e se o
/// colaborador ligado não foi desligado. É isso que faz o logout, a troca de
/// senha/perfil/e-mail e o desligamento valerem NA HORA — antes o token
/// antigo seguia funcionando por até 120 minutos (dava pra bater ponto
/// depois de desligado).
/// </summary>
public static class ValidacaoDeSessao
{
    /// <summary>Chave em HttpContext.Items com a mensagem do 401 (escrita pelo UseStatusCodePages).</summary>
    public const string ChaveMotivoNaoAutorizado = "SistemaRH.MotivoNaoAutorizado";

    public static async Task ConferirAsync(TokenValidatedContext context)
    {
        var http = context.HttpContext;
        var principal = context.Principal;
        var sub = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tv = principal?.FindFirst(JwtTokenService.ClaimTokenVersao)?.Value;
        if (!int.TryParse(sub, out var usuarioId) || !int.TryParse(tv, out var versaoDoToken))
        {
            context.Fail("Token sem a identificação da conta ou sem a versão.");
            return;
        }

        if (http.RequestAborted.IsCancellationRequested)
        {
            EncerrarSemErro(context);
            return;
        }

        bool existe, desligado;
        int versaoAtual;
        try
        {
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            // A busca NÃO recebe o RequestAborted: é uma leitura pela chave,
            // de milissegundos, e cancelá-la no meio não economiza nada. Com
            // o token do cliente, cada tela fechada no painel (que cancela os
            // GETs) virava "fail: Database.Connection[20004]" do EF e
            // "fail: JwtBearerHandler[3] … OperationCanceledException" no log.
            var conta = await db.Usuarios.AsNoTracking()
                .Where(u => u.Id == usuarioId)
                .Select(u => new
                {
                    u.TokenVersao,
                    Desligado = u.Funcionario != null && u.Funcionario.Status == StatusFuncionario.Desligado,
                })
                .FirstOrDefaultAsync();
            existe = conta is not null;
            desligado = conta?.Desligado ?? false;
            versaoAtual = conta?.TokenVersao ?? 0;
        }
        catch (Exception ex) when (http.RequestAborted.IsCancellationRequested || ex is OperationCanceledException)
        {
            // O cliente foi embora no meio (ou a leitura foi cancelada): não
            // é erro do servidor. Se o evento lançasse a exceção, o
            // JwtBearerHandler a registraria como "fail".
            EncerrarSemErro(context);
            return;
        }

        if (!existe)
        {
            context.Fail("Conta não existe mais.");
            return;
        }
        // Desligado é conferido antes da versão (que também sobe no
        // desligamento) pra pessoa ver o motivo certo no app.
        if (desligado)
        {
            http.Items[ChaveMotivoNaoAutorizado] = AuthService.MensagemAcessoDesativado;
            context.Fail("Colaborador desligado.");
            return;
        }
        if (versaoAtual != versaoDoToken)
        {
            context.Fail("Sessão encerrada.");
        }
    }

    /// <summary>
    /// Requisição cancelada pelo cliente durante a validação: registra como
    /// informação e encerra a autenticação sem resultado (NoResult). Não há
    /// exceção nem Fail com exceção, então nada vira "fail" no log; a
    /// resposta, se ainda sair, é um 401 pra ninguém.
    /// </summary>
    private static void EncerrarSemErro(TokenValidatedContext context)
    {
        var http = context.HttpContext;
        http.RequestServices.GetService<ILoggerFactory>()?
            .CreateLogger(typeof(ValidacaoDeSessao))
            .LogInformation("Requisição cancelada pelo cliente durante a validação do token em {Metodo} {Caminho}",
                http.Request.Method, http.Request.Path);
        context.NoResult();
    }
}
