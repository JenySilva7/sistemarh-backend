using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Data;

/// <summary>
/// Ponte entre o C# e o banco de dados. Cada "DbSet" vira uma tabela.
/// O Entity Framework Core usa essa classe pra saber o que existe no banco
/// e pra montar o SQL automaticamente quando você consulta ou salva dados.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RegistroPonto> RegistrosPonto => Set<RegistroPonto>();
    public DbSet<Ausencia> Ausencias => Set<Ausencia>();

    // Lado administrativo/RH (site) — estrutura criada, ainda sem rotas/serviços.
    public DbSet<Departamento> Departamentos => Set<Departamento>();
    public DbSet<Cargo> Cargos => Set<Cargo>();
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<LancamentoBancoHoras> LancamentosBancoHoras => Set<LancamentoBancoHoras>();
    public DbSet<Recrutamento> Recrutamentos => Set<Recrutamento>();
    public DbSet<Avaliacao> Avaliacoes => Set<Avaliacao>();
    public DbSet<EventoAgenda> EventosAgenda => Set<EventoAgenda>();
    public DbSet<FolhaPagamento> FolhasPagamento => Set<FolhaPagamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Usuario>()
            .HasOne(u => u.Funcionario)
            .WithMany()
            .HasForeignKey(u => u.FuncionarioId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RegistroPonto>()
            .HasOne(r => r.Usuario)
            .WithMany(u => u.RegistrosPonto)
            .HasForeignKey(r => r.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Ausencia>()
            .HasOne(a => a.Usuario)
            .WithMany(u => u.Ausencias)
            .HasForeignKey(a => a.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Departamentos / Cargos ──────────────────────────────────────
        modelBuilder.Entity<Departamento>()
            .HasIndex(d => d.Nome)
            .IsUnique();

        modelBuilder.Entity<Cargo>()
            .HasIndex(c => c.Nome)
            .IsUnique();
        modelBuilder.Entity<Cargo>()
            .Property(c => c.SalarioBase)
            .HasPrecision(10, 2);

        // ── Funcionários ─────────────────────────────────────────────────
        modelBuilder.Entity<Funcionario>()
            .HasIndex(f => f.Cpf)
            .IsUnique();
        modelBuilder.Entity<Funcionario>()
            .HasIndex(f => f.Email)
            .IsUnique();

        modelBuilder.Entity<Funcionario>()
            .HasOne(f => f.Departamento)
            .WithMany(d => d.Funcionarios)
            .HasForeignKey(f => f.DepartamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Funcionario>()
            .HasOne(f => f.Cargo)
            .WithMany(c => c.Funcionarios)
            .HasForeignKey(f => f.CargoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Auto-relacionamento (gestor/subordinados) — Restrict pra não correr
        // risco de um DELETE em cascata apagar a cadeia inteira de gestores.
        modelBuilder.Entity<Funcionario>()
            .HasOne(f => f.Gerente)
            .WithMany(f => f.Subordinados)
            .HasForeignKey(f => f.GerenteId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Banco de horas (extrato) ────────────────────────────────────
        modelBuilder.Entity<LancamentoBancoHoras>()
            .HasOne(l => l.Funcionario)
            .WithMany(f => f.LancamentosBancoHoras)
            .HasForeignKey(l => l.FuncionarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Avaliações ───────────────────────────────────────────────────
        modelBuilder.Entity<Avaliacao>()
            .Property(a => a.Nota)
            .HasPrecision(4, 2);
        modelBuilder.Entity<Avaliacao>()
            .HasOne(a => a.Funcionario)
            .WithMany(f => f.Avaliacoes)
            .HasForeignKey(a => a.FuncionarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Agenda ───────────────────────────────────────────────────────
        modelBuilder.Entity<EventoAgenda>()
            .HasOne(e => e.Funcionario)
            .WithMany(f => f.EventosAgenda)
            .HasForeignKey(e => e.FuncionarioId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Folha de pagamento ───────────────────────────────────────────
        modelBuilder.Entity<FolhaPagamento>()
            .HasIndex(fp => new { fp.FuncionarioId, fp.Competencia })
            .IsUnique();
        modelBuilder.Entity<FolhaPagamento>()
            .Property(fp => fp.Salario)
            .HasPrecision(10, 2);
        modelBuilder.Entity<FolhaPagamento>()
            .Property(fp => fp.Bonus)
            .HasPrecision(10, 2);
        modelBuilder.Entity<FolhaPagamento>()
            .Property(fp => fp.Descontos)
            .HasPrecision(10, 2);
        modelBuilder.Entity<FolhaPagamento>()
            .Property(fp => fp.SalarioLiquido)
            .HasPrecision(10, 2);
        modelBuilder.Entity<FolhaPagamento>()
            .HasOne(fp => fp.Funcionario)
            .WithMany(f => f.FolhasPagamento)
            .HasForeignKey(fp => fp.FuncionarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Dados de referência (departamentos e cargos já nascem populados) ──
        // CriadoEm é fixo (em vez de usar o padrão DateTime.UtcNow da classe)
        // de propósito: se cada gerador de migration pegasse um instante
        // diferente, toda vez que alguém rodasse "dotnet ef migrations add"
        // apareceria uma migration "fantasma" só por causa disso.
        var dataReferenciaSeed = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<Departamento>().HasData(
            new Departamento { Id = 1, Nome = "Recursos Humanos", Descricao = "Gestão de pessoas e processos de RH", CriadoEm = dataReferenciaSeed },
            new Departamento { Id = 2, Nome = "Tecnologia da Informação", Descricao = "Desenvolvimento e suporte tecnológico", CriadoEm = dataReferenciaSeed },
            new Departamento { Id = 3, Nome = "Financeiro", Descricao = "Gestão financeira da empresa", CriadoEm = dataReferenciaSeed },
            new Departamento { Id = 4, Nome = "Comercial", Descricao = "Vendas e relacionamento com clientes", CriadoEm = dataReferenciaSeed }
        );
        modelBuilder.Entity<Cargo>().HasData(
            new Cargo { Id = 1, Nome = "Analista de RH", SalarioBase = 3500m, Descricao = "Atuação nos processos de Recursos Humanos" },
            new Cargo { Id = 2, Nome = "Desenvolvedor", SalarioBase = 5000m, Descricao = "Desenvolvimento e manutenção de sistemas" },
            new Cargo { Id = 3, Nome = "Analista Financeiro", SalarioBase = 4000m, Descricao = "Controle e análise financeira" },
            new Cargo { Id = 4, Nome = "Vendedor", SalarioBase = 2800m, Descricao = "Atendimento e vendas" }
        );

        AplicarConversaoUtcNosInstantes(modelBuilder);
    }

    /// <summary>Nomes das propriedades que guardam um instante (sempre preenchidas com DateTime.UtcNow).</summary>
    private static readonly HashSet<string> PropriedadesDeInstante = new() { "CriadoEm", "RegistradoEm" };

    /// <summary>
    /// A coluna DATETIME do MySQL não guarda fuso, então o valor volta do banco
    /// como DateTimeKind.Unspecified e sai no JSON sem o "Z" — o app então
    /// interpreta como horário local e mostra 3h adiantado. Ao ler, marcamos
    /// como UTC (é o que foi gravado).
    /// </summary>
    public static readonly ValueConverter<DateTime, DateTime> ConversorUtc = new(
        valor => valor,
        valor => DateTime.SpecifyKind(valor, DateTimeKind.Utc));

    // Só nos instantes: datas de calendário (Ausencia.Data, DataAdmissao,
    // DataRegistro etc.) precisam continuar sem "Z", senão JS/Dart
    // converteriam pro fuso local e mostrariam o dia anterior no Brasil.
    private static void AplicarConversaoUtcNosInstantes(ModelBuilder modelBuilder)
    {
        var propriedades = modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) && PropriedadesDeInstante.Contains(p.Name));

        foreach (var propriedade in propriedades)
        {
            propriedade.SetValueConverter(ConversorUtc);
        }
    }
}
