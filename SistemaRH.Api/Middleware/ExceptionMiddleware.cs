using System.Globalization;
using System.Text.Json;
using SistemaRH.Api.Common;
using SistemaRH.Api.Exceptions;

namespace SistemaRH.Api.Middleware;

/// <summary>
/// Um "middleware" é um código que roda ao redor de TODA requisição que
/// chega na API — entra antes do Controller, deixa a requisição passar
/// (chamando "next"), e intercepta qualquer exceção que escape.
///
/// Sem isso, um erro não tratado derrubaria a requisição com uma página de
/// erro padrão do ASP.NET, que em produção pode vazar detalhes internos
/// (stack trace, nome de classes, até trechos de SQL). Aqui a gente garante
/// que toda resposta de erro tenha o mesmo formato JSON e nunca exponha
/// detalhes internos — só a mensagem amigável e o status HTTP corretos.
/// </summary>
public class ExceptionMiddleware
{
    public const string MensagemRequisicaoGrande = "Requisição grande demais.";

    /// <summary>
    /// Status (não oficial, o mesmo do nginx) de quem desistiu da requisição
    /// antes da resposta. Só vai pro log de acesso: o cliente já foi embora.
    /// </summary>
    public const int StatusCanceladaPeloCliente = 499;

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (context.RequestAborted.IsCancellationRequested)
        {
            // O cliente desistiu (o painel cancela os GETs quando a pessoa
            // sai da tela, a conexão caiu...). O OperationCanceledException,
            // ou o erro de ler/escrever numa conexão fechada, não é bug:
            // antes cada cancelamento virava "Erro inesperado" no log e uma
            // tentativa de responder 500 a ninguém. Fica um registro
            // informativo, sem stack trace, e nenhuma resposta é escrita.
            _logger.LogInformation("Requisição cancelada pelo cliente em {Metodo} {Caminho} ({Tipo})",
                context.Request.Method, context.Request.Path, ex.GetType().Name);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCanceladaPeloCliente;
            }
        }
        catch (ApiException ex) when (!context.Response.HasStarted)
        {
            // Erro "esperado" de regra de negócio (ex.: não encontrado,
            // sem permissão) — a mensagem é segura pra mostrar ao usuário.
            _logger.LogWarning("Erro de regra de negócio ({Status}) em {Metodo} {Caminho}: {Mensagem}",
                ex.StatusCode, context.Request.Method, context.Request.Path, ex.Message);
            if (ex is TooManyRequestsException limite)
            {
                var segundos = Math.Max(1, (int)Math.Ceiling(limite.TentarDeNovoEm.TotalSeconds));
                context.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
            }
            await EscreverRespostaDeErro(context, ex.StatusCode, ex.Message);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            // O Kestrel recusou a requisição em si (ex.: corpo acima do limite
            // de 30 MB). Já vem com o status certo (413, 400...); antes caía
            // no catch genérico e virava 500.
            _logger.LogWarning("Requisição recusada ({Status}) em {Metodo} {Caminho}: {Mensagem}",
                ex.StatusCode, context.Request.Method, context.Request.Path, ex.Message);
            var mensagem = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? MensagemRequisicaoGrande
                : "Requisição inválida.";
            await EscreverRespostaDeErro(context, ex.StatusCode, mensagem);
        }
        catch (Exception ex) when (!context.Response.HasStarted && ErrosBanco.Classificar(ex) is { } erroDeBanco)
        {
            // Duas requisições disputando o mesmo registro (deadlock, chave
            // duplicada, versão desatualizada). Não é bug: quem perdeu a
            // corrida recebe 409 e pode atualizar a tela e tentar de novo.
            _logger.LogWarning(ex, "Conflito de banco em {Metodo} {Caminho}", context.Request.Method, context.Request.Path);
            await EscreverRespostaDeErro(context, erroDeBanco.StatusCode, erroDeBanco.Mensagem);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            // Qualquer outro erro é inesperado (bug, falha de conexão, etc).
            // Logamos os detalhes completos pro desenvolvedor, mas o usuário
            // só recebe uma mensagem genérica.
            _logger.LogError(ex, "Erro inesperado ao processar {Metodo} {Caminho}", context.Request.Method, context.Request.Path);
            await EscreverRespostaDeErro(context, StatusCodes.Status500InternalServerError,
                "Ocorreu um erro inesperado. Tente novamente em instantes.");
        }
    }

    public static async Task EscreverRespostaDeErro(HttpContext context, int statusCode, string mensagem)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = statusCode;

        var corpo = JsonSerializer.Serialize(new { mensagem });
        await context.Response.WriteAsync(corpo);
    }
}
