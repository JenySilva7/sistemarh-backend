using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SistemaRH.Api.Data;
using SistemaRH.Api.Middleware;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
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
    });
builder.Services.AddAuthorization(options =>
{
    // Restringe as rotas administrativas/RH (funcionários, recrutamento,
    // avaliações, agenda, folha de pagamento) a quem tem o perfil certo. Um
    // usuário comum (PerfilUsuario.Funcionario, o padrão de todo cadastro
    // público) recebe 403 ao tentar acessá-las.
    options.AddPolicy("RhOuAdmin", policy =>
        policy.RequireClaim("perfil", nameof(PerfilUsuario.Rh), nameof(PerfilUsuario.Admin)));
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
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPontoService, PontoService>();
builder.Services.AddScoped<IAusenciaService, AusenciaService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// Lado administrativo/RH
builder.Services.AddScoped<IDepartamentoService, DepartamentoService>();
builder.Services.AddScoped<ICargoService, CargoService>();
builder.Services.AddScoped<IFuncionarioService, FuncionarioService>();
builder.Services.AddScoped<ILancamentoBancoHorasService, LancamentoBancoHorasService>();
builder.Services.AddScoped<IRecrutamentoService, RecrutamentoService>();
builder.Services.AddScoped<IAvaliacaoService, AvaliacaoService>();
builder.Services.AddScoped<IEventoAgendaService, EventoAgendaService>();
builder.Services.AddScoped<IFolhaPagamentoService, FolhaPagamentoService>();

builder.Services.AddControllers();
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
        var mensagem = string.Join(' ', context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage));
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { mensagem });
    };
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseCors(corsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Permite que o projeto de testes (WebApplicationFactory) enxergue a classe
// Program pra criar um servidor de teste em memória.
public partial class Program;
