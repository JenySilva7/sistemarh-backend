using System.Text.Json;
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
        catch (ApiException ex)
        {
            // Erro "esperado" de regra de negócio (ex.: não encontrado,
            // sem permissão) — a mensagem é segura pra mostrar ao usuário.
            _logger.LogWarning(ex, "Erro de regra de negócio: {Mensagem}", ex.Message);
            await EscreverRespostaDeErro(context, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            // Qualquer outro erro é inesperado (bug, falha de conexão, etc).
            // Logamos os detalhes completos pro desenvolvedor, mas o usuário
            // só recebe uma mensagem genérica.
            _logger.LogError(ex, "Erro inesperado ao processar {Metodo} {Caminho}", context.Request.Method, context.Request.Path);
            await EscreverRespostaDeErro(context, StatusCodes.Status500InternalServerError,
                "Ocorreu um erro inesperado. Tente novamente em instantes.");
        }
    }

    private static async Task EscreverRespostaDeErro(HttpContext context, int statusCode, string mensagem)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var corpo = JsonSerializer.Serialize(new { mensagem });
        await context.Response.WriteAsync(corpo);
    }
}
