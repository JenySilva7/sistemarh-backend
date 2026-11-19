using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SistemaRH.Api.Data;
using SistemaRH.Api.Middleware;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Não anuncia a tecnologia do servidor ("Server: Kestrel") em toda resposta.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ── Banco de dados ──────────────────────────────────────────────────────
// Usamos MySQL (rodando localmente via XAMPP). O "AutoDetect" abre uma
// conexão rapidinha ao iniciar só pra perguntar ao servidor qual versão do
// MySQL ele é, e configura o EF Core de acordo — evita ter que escrever a
// versão na mão e ela ficar desatualizada se o MySQL for atualizado depois.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Configuração 'ConnectionStrings:Default' não encontrada.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ── Autenticação JWT ─────────────────────────────────────────────────────
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "Configuração 'Jwt:Secret' não encontrada. Defina-a em appsettings.Development.json " +
        "(ambiente local) ou como variável de ambiente Jwt__Secret (produção).");

// Mensagem do 401, guardada na requisição pra o UseStatusCodePages (lá
// embaixo) escrever no corpo { "mensagem": ... }.
const string chaveMotivoNaoAutorizado = ValidacaoDeSessao.ChaveMotivoNaoAutorizado;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // Não descreve no cabeçalho WWW-Authenticate por que o token foi recusado.
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        options.Events = new JwtBearerEvents
        {
            // Conta existe, colaborador não desligado e versão do token ("tv")
            // atual — ver Services/ValidacaoDeSessao. Requisição cancelada
            // pelo cliente no meio dessa conferência não vira erro no log.
            OnTokenValidated = ValidacaoDeSessao.ConferirAsync,
            OnChallenge = context =>
            {
                if (!context.HttpContext.Items.ContainsKey(chaveMotivoNaoAutorizado))
                {
                    var semToken = string.IsNullOrEmpty(context.Request.Headers.Authorization);
                    context.HttpContext.Items[chaveMotivoNaoAutorizado] = semToken
                        ? "Faça login para continuar."
                        : "Sua sessão foi encerrada. Entre novamente.";
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(options =>
{
    // Restringe as rotas administrativas/RH (funcionários, recrutamento,
    // avaliações, agenda, folha de pagamento) a quem tem o perfil certo. Um
    // usuário comum (PerfilUsuario.Funcionario) recebe 403 ao tentar acessá-las.
    options.AddPolicy("RhOuAdmin", policy =>
        policy.RequireClaim("perfil", nameof(PerfilUsuario.Rh), nameof(PerfilUsuario.Admin)));
    // Só Admin: criar contas de acesso e mudar o perfil de alguém.
    options.AddPolicy("Admin", policy =>
        policy.RequireClaim("perfil", nameof(PerfilUsuario.Admin)));
});

// ── CORS ─────────────────────────────────────────────────────────────────
// Libera só os endereços configurados em appsettings (o site web em dev, por
// exemplo). O app Flutter rodando como app nativo (Android/iOS) NÃO passa
// pelo CORS — essa regra é aplicada pelo navegador, então só afeta clientes
// web (o site e a versão Flutter Web).
const string corsPolicyName = "FrontendsPermitidos";
var origensPermitidas = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        // Além da lista fixa (localhost em dev), libera qualquer subdomínio
        // *.trycloudflare.com: os túneis rápidos do Cloudflare geram uma URL
        // nova a cada restart, então travar em uma URL específica quebra o
        // CORS assim que o túnel reinicia.
        policy.SetIsOriginAllowed(origin =>
                origensPermitidas.Contains(origin)
                || (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    && uri.Scheme == "https"
                    && uri.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase)))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ── Injeção de dependência dos serviços da aplicação ───────────────────
// Relógio do sistema (os testes trocam por um relógio falso, ex.: no ponto).
builder.Services.AddSingleton(TimeProvider.System);
// Singleton: as falhas de login precisam ser contadas entre requisições.
builder.Services.AddSingleton<ILimiteTentativasLogin, LimiteTentativasLogin>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPontoService, PontoService>();
builder.Services.AddScoped<IAusenciaService, AusenciaService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IMeService, MeService>();
builder.Services.AddScoped<IMensagemRhService, MensagemRhService>();

// Lado administrativo/RH
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<IDepartamentoService, DepartamentoService>();
builder.Services.AddScoped<ICargoService, CargoService>();
builder.Services.AddScoped<IFuncionarioService, FuncionarioService>();
builder.Services.AddScoped<ILancamentoBancoHorasService, LancamentoBancoHorasService>();
builder.Services.AddScoped<IRecrutamentoService, RecrutamentoService>();
builder.Services.AddScoped<IAvaliacaoService, AvaliacaoService>();
builder.Services.AddScoped<IEventoAgendaService, EventoAgendaService>();
builder.Services.AddScoped<IFolhaPagamentoService, FolhaPagamentoService>();
builder.Services.AddScoped<IRhService, RhService>();

builder.Services.AddControllers(options =>
{
    // Sem isso, toda propriedade string não anulável virava [Required]
    // implícito, com mensagem em inglês ("The Descricao field is required.").
    // Os textos são validados nos serviços (Common/Textos), em português.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
.AddJsonOptions(options =>
{
    // Número só como número de verdade no JSON. O padrão da web aceita
    // número dentro de string e lê com ponto decimal: "5.000" virava R$ 5,00.
    // O painel e o app já mandam números (não strings).
    options.JsonSerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});

// ── IP real atrás do proxy ───────────────────────────────────────────────
// Na Railway a API fica atrás de um proxy; sem isso, todo login pareceria
// vir do IP do proxy e o limite de tentativas por IP valeria pra todo mundo
// junto. A lista de proxies conhecidos é limpa porque o IP do proxy da
// Railway não é fixo. Com ForwardLimit = 1 (padrão), só o último IP do
// X-Forwarded-For é usado — o que o proxy acrescentou, não o que o cliente
// escreveu. Se um dia houver mais de um proxy na frente, ajuste
// "Proxy:ForwardLimit" na configuração.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = builder.Configuration.GetValue<int?>("Proxy:ForwardLimit") ?? 1;
});
builder.Services.AddEndpointsApiExplorer();

// Por padrão, quando um DTO falha nas validações de [Required]/[EmailAddress]
// etc., o ASP.NET Core devolve um 400 num formato próprio (ProblemDetails,
// com um dicionário "errors"). Só que todo o resto da API (ver
// ExceptionMiddleware) sempre devolve erros como { "mensagem": "..." } — e é
// esse formato que o app Flutter e o site React sabem ler. Sem isso aqui,
// um erro de "campo obrigatório" apareceria pro usuário como uma mensagem
// genérica de "erro inesperado" em vez do aviso de verdade.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var mensagem = SistemaRH.Api.Common.MensagensValidacao.Montar(context.ModelState);
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { mensagem });
    };
    // 415, 404 etc. gerados pelo MVC saem sem corpo (em vez do ProblemDetails
    // em inglês) e o UseStatusCodePages abaixo escreve o { "mensagem" }.
    options.SuppressMapClientErrors = true;
});

// ── Swagger (documentação + console pra testar a API no navegador) ──────
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Sistema RH - API",
        Version = "v1",
        Description = "API do app/site do Sistema RH: autenticação, ponto, ausências e o lado administrativo/RH (funcionários, banco de horas, recrutamento, avaliações, agenda, folha de pagamento).",
    });

    // Ensina o Swagger a mostrar o botão "Authorize", onde dá pra colar
    // "Bearer SEU_TOKEN" e testar as rotas protegidas direto pelo navegador.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole aqui: Bearer {seu token}",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

var app = builder.Build();

// ── Aplica as migrations pendentes automaticamente ao iniciar ───────────
// Em projetos grandes, geralmente isso é feito manualmente (ou via
// pipeline de deploy); aqui, pra simplificar a vida de quem está
// aprendendo, o próprio app garante que o banco está atualizado ao subir.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Só bancos "relacionais" (como o SQLite) suportam migrations — o banco
    // In-Memory usado nos testes automatizados não suporta, então pulamos
    // esse passo nesse caso.
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
}

// Primeiro de tudo: troca o IP da conexão (o do proxy) pelo do cliente.
app.UseForwardedHeaders();

// Cabeçalhos de segurança em toda resposta: o navegador não "adivinha" o
// tipo do conteúdo, a API não pode ser aberta dentro de um iframe e o
// endereço não vaza pra outros sites no Referer.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseMiddleware<ExceptionMiddleware>();

// Erros que saem sem corpo (401 do token, 403 de perfil, 404 de rota, 405,
// 415 de Content-Type errado...) ganham o mesmo { "mensagem": "..." } em
// português que o resto da API — o painel e o app só sabem ler esse formato.
app.UseStatusCodePages(async contexto =>
{
    var http = contexto.HttpContext;
    var mensagem = http.Items[chaveMotivoNaoAutorizado] as string ?? http.Response.StatusCode switch
    {
        StatusCodes.Status401Unauthorized => "Sua sessão foi encerrada. Entre novamente.",
        StatusCodes.Status403Forbidden => "Você não tem permissão para acessar este recurso.",
        StatusCodes.Status404NotFound => "Recurso não encontrado.",
        StatusCodes.Status405MethodNotAllowed => "Método não permitido para este endereço.",
        StatusCodes.Status413PayloadTooLarge => ExceptionMiddleware.MensagemRequisicaoGrande,
        StatusCodes.Status415UnsupportedMediaType =>
            "Formato de conteúdo não suportado. Envie os dados em JSON (Content-Type: application/json).",
        _ => "Não foi possível processar a requisição.",
    };
    http.Response.ContentType = "application/json; charset=utf-8";
    await http.Response.WriteAsync(JsonSerializer.Serialize(new { mensagem }));
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(corsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Permite que o projeto de testes (WebApplicationFactory) enxergue a classe
// Program pra criar um servidor de teste em memória.
public partial class Program;
