using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SistemaRH.Api.Common;
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
    public DbSet<AjusteRemuneracao> AjustesRemuneracao => Set<AjusteRemuneracao>();
    public DbSet<MensagemRh> MensagensRh => Set<MensagemRh>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigurarTamanhosDeTexto(modelBuilder);

        // Versão otimista (ver IVersionado): o UPDATE/DELETE só acontece se a
        // versão no banco ainda for a que foi lida.
        modelBuilder.Entity<Funcionario>().Property(f => f.Versao).IsConcurrencyToken();
        modelBuilder.Entity<Ausencia>().Property(a => a.Versao).IsConcurrencyToken();
        modelBuilder.Entity<MensagemRh>().Property(m => m.Versao).IsConcurrencyToken();

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
            .Property(f => f.Salario)
            .HasPrecision(18, 2);
        modelBuilder.Entity<Funcionario>()
            .Property(f => f.ValeAlimentacao)
            .HasPrecision(18, 2);

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

        // ── Histórico de remuneração ─────────────────────────────────────
        modelBuilder.Entity<AjusteRemuneracao>()
            .Property(a => a.ValorAnterior)
            .HasPrecision(18, 2);
        modelBuilder.Entity<AjusteRemuneracao>()
            .Property(a => a.ValorNovo)
            .HasPrecision(18, 2);
        modelBuilder.Entity<AjusteRemuneracao>()
            .HasOne(a => a.Funcionario)
            .WithMany(f => f.AjustesRemuneracao)
            .HasForeignKey(a => a.FuncionarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict nos cargos/departamentos pra o histórico nunca perder a
        // referência do "de onde pra onde" de uma promoção.
        modelBuilder.Entity<AjusteRemuneracao>()
            .HasOne(a => a.CargoAnterior)
            .WithMany()
            .HasForeignKey(a => a.CargoAnteriorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AjusteRemuneracao>()
            .HasOne(a => a.CargoNovo)
            .WithMany()
            .HasForeignKey(a => a.CargoNovoId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AjusteRemuneracao>()
            .HasOne(a => a.DepartamentoAnterior)
            .WithMany()
            .HasForeignKey(a => a.DepartamentoAnteriorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AjusteRemuneracao>()
            .HasOne(a => a.DepartamentoNovo)
            .WithMany()
            .HasForeignKey(a => a.DepartamentoNovoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Se a conta de quem fez o ajuste for excluída, o registro fica (sem o responsável).
        modelBuilder.Entity<AjusteRemuneracao>()
            .HasOne(a => a.ResponsavelUsuario)
            .WithMany()
            .HasForeignKey(a => a.ResponsavelUsuarioId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Mensagens ao RH ("Falar com o RH" do app) ───────────────────────
        modelBuilder.Entity<MensagemRh>()
            .HasOne(m => m.Usuario)
            .WithMany(u => u.MensagensRh)
            .HasForeignKey(m => m.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MensagemRh>()
            .HasOne(m => m.ResponsavelUsuario)
            .WithMany()
            .HasForeignKey(m => m.ResponsavelUsuarioId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MensagemRh>()
            .HasIndex(m => new { m.Status, m.CriadoEm });

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

    /// <summary>
    /// Tamanho máximo de cada coluna de texto — o mesmo das regras de
    /// validação (Common/Textos, Emails, Telefones). Sem limite, o MySQL
    /// criava LONGTEXT e um nome de 90 mil caracteres estourava a memória de
    /// ordenação do "ORDER BY Nome", derrubando todas as listas do painel.
    /// </summary>
    private static void ConfigurarTamanhosDeTexto(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(e =>
        {
            e.Property(u => u.Nome).HasMaxLength(120);
            e.Property(u => u.Email).HasMaxLength(Emails.TamanhoMaximo);
            e.Property(u => u.SenhaHash).HasMaxLength(255);
            e.Property(u => u.Cargo).HasMaxLength(100);
            e.Property(u => u.Departamento).HasMaxLength(100);
        });
        modelBuilder.Entity<Funcionario>(e =>
        {
            e.Property(f => f.Nome).HasMaxLength(120);
            e.Property(f => f.Cpf).HasMaxLength(14);
            e.Property(f => f.Email).HasMaxLength(Emails.TamanhoMaximo);
            // Gravado como "(11) 98765-4321"; 20 dá folga pra cadastros antigos sem máscara.
            e.Property(f => f.Telefone).HasMaxLength(20);
        });
        modelBuilder.Entity<Departamento>(e =>
        {
            e.Property(d => d.Nome).HasMaxLength(100);
            e.Property(d => d.Descricao).HasMaxLength(500);
        });
        modelBuilder.Entity<Cargo>(e =>
        {
            e.Property(c => c.Nome).HasMaxLength(100);
            e.Property(c => c.Descricao).HasMaxLength(500);
        });
        modelBuilder.Entity<Ausencia>().Property(a => a.Descricao).HasMaxLength(2000);
        modelBuilder.Entity<AjusteRemuneracao>().Property(a => a.Motivo).HasMaxLength(500);
        modelBuilder.Entity<Avaliacao>().Property(a => a.Comentario).HasMaxLength(2000);
        modelBuilder.Entity<EventoAgenda>(e =>
        {
            e.Property(ev => ev.Titulo).HasMaxLength(100);
            e.Property(ev => ev.Descricao).HasMaxLength(2000);
        });
        modelBuilder.Entity<FolhaPagamento>().Property(fp => fp.Competencia).HasMaxLength(7);
        modelBuilder.Entity<LancamentoBancoHoras>().Property(l => l.Descricao).HasMaxLength(500);
        modelBuilder.Entity<Recrutamento>(e =>
        {
            e.Property(r => r.NomeCandidato).HasMaxLength(120);
            e.Property(r => r.Email).HasMaxLength(Emails.TamanhoMaximo);
            e.Property(r => r.Telefone).HasMaxLength(20);
            e.Property(r => r.Vaga).HasMaxLength(100);
            e.Property(r => r.Observacoes).HasMaxLength(2000);
        });
        modelBuilder.Entity<MensagemRh>(e =>
        {
            e.Property(m => m.Assunto).HasMaxLength(100);
            e.Property(m => m.Mensagem).HasMaxLength(2000);
            e.Property(m => m.Resposta).HasMaxLength(2000);
        });
    }

    // ── Versão otimista ─────────────────────────────────────────────────
    // Toda entidade IVersionado alterada ganha versão nova no mesmo
    // SaveChanges; o valor ORIGINAL (o que foi lido) continua sendo o usado
    // no WHERE do UPDATE, então quem gravar depois com dado velho falha.

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IncrementarVersoes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        IncrementarVersoes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void IncrementarVersoes()
    {
        foreach (var entrada in ChangeTracker.Entries<IVersionado>())
        {
            if (entrada.State == EntityState.Modified)
            {
                entrada.Entity.Versao++;
            }
        }
    }

    // ── Transação e trava de linha (só no MySQL) ───────────────────────
    // O banco InMemory dos testes não tem transação nem SELECT ... FOR UPDATE,
    // então lá esses passos simplesmente não fazem nada.
    //
    // Regra de uso: TRAVAR PRIMEIRO, ler depois. E a transação é READ
    // COMMITTED: no REPEATABLE READ (padrão do MySQL) a primeira leitura
    // comum congela uma "foto" do banco, e tudo o que a transação lê depois
    // de esperar uma trava continua vindo dessa foto velha — foi assim que
    // um desligamento não enxergava o subordinado gravado por quem segurava a
    // trava, e uma promoção não via o ciclo criado por uma edição simultânea.
    // Em READ COMMITTED, cada leitura vê o que já foi confirmado.

    /// <summary>Abre uma transação (READ COMMITTED) no banco relacional; no InMemory devolve null.</summary>
    public async Task<IDbContextTransaction?> IniciarTransacaoAsync()
    {
        return Database.IsRelational()
            ? await Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted)
            : null;
    }

    /// <summary>Confirma a transação aberta por <see cref="IniciarTransacaoAsync"/> (se houver).</summary>
    public static async Task ConfirmarAsync(IDbContextTransaction? transacao)
    {
        if (transacao is not null)
        {
            await transacao.CommitAsync();
        }
    }

    // ── Travas globais (GET_LOCK do MySQL) ──────────────────────────────
    // Travar linhas não basta quando a regra depende do conjunto todo. Ex.:
    // com A→B e C→D gravados, "B→C" e "D→A" ao mesmo tempo travam pares
    // disjuntos ({B,C} e {D,A}), cada um confere o ciclo sem ver a aresta do
    // outro e os dois passam: fecha-se o ciclo A→B→C→D→A. Por isso TODA
    // mudança de hierarquia (cadastro com gestor, edição com gestor,
    // promoção e desligamento) passa por uma trava única, e as trocas de
    // perfil por outra. Sempre pegue a trava global ANTES das travas de
    // linha, e dentro de uma transação já aberta.

    public const string TravaHierarquia = "sistemarh_hierarquia";
    public const string TravaPerfis = "sistemarh_perfis";
    private const int SegundosParaTravaGlobal = 10;

    /// <summary>
    /// Pega a trava global com esse nome (por database) até o fim do bloco
    /// <c>await using</c>. Se não sair em 10 s, 409 de operação simultânea.
    /// No InMemory dos testes não faz nada.
    /// </summary>
    public async Task<IAsyncDisposable> TravarGlobalAsync(string nome)
    {
        if (!Database.IsRelational())
        {
            return TravaGlobal.Nenhuma;
        }
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A trava global precisa de uma transação aberta (IniciarTransacaoAsync).");
        }

        // GET_LOCK pertence à CONEXÃO. O EF fecha a conexão (devolve ao pool)
        // assim que a transação é confirmada, e aí o RELEASE_LOCK do fim do
        // bloco não teria onde rodar — a trava ficava presa numa conexão
        // parada no pool e as próximas operações esperavam 10 s e davam 409.
        // Abrir explicitamente aqui mantém a conexão aberta até a trava ser
        // liberada (depois do commit, pra quem vem depois ler o já gravado).
        await Database.OpenConnectionAsync();
        try
        {
            // O nome leva o database: dois bancos no mesmo servidor não se travam.
            var resultado = await ExecutarEscalarAsync(
                $"SELECT GET_LOCK(CONCAT('{nome}:', DATABASE()), {SegundosParaTravaGlobal})");
            if (resultado is null or DBNull || Convert.ToInt64(resultado, System.Globalization.CultureInfo.InvariantCulture) != 1)
            {
                throw new SistemaRH.Api.Exceptions.ConflictException(ErrosBanco.MensagemOperacaoSimultanea);
            }
        }
        catch
        {
            await Database.CloseConnectionAsync();
            throw;
        }
        return new TravaGlobal(this, nome);
    }

    private async Task<object?> ExecutarEscalarAsync(string sql)
    {
        var conexao = Database.GetDbConnection();
        await using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        return await comando.ExecuteScalarAsync();
    }

    private sealed class TravaGlobal : IAsyncDisposable
    {
        public static readonly IAsyncDisposable Nenhuma = new SemTrava();

        private readonly AppDbContext _db;
        private readonly string _nome;

        public TravaGlobal(AppDbContext db, string nome)
        {
            _db = db;
            _nome = nome;
        }

        public async ValueTask DisposeAsync()
        {
            // GET_LOCK é da conexão, não da transação: precisa ser liberado
            // explicitamente, com a conexão ainda aberta (ver TravarGlobalAsync).
            // Uma falha aqui não pode esconder o erro original da operação.
            try
            {
                await _db.ExecutarEscalarAsync($"SELECT RELEASE_LOCK(CONCAT('{_nome}:', DATABASE()))");
            }
            catch (Exception)
            {
                // Se a conexão caiu, o MySQL já soltou a trava junto com a sessão.
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }
        }

        private sealed class SemTrava : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Trava as linhas desses funcionários até o fim da transação, sempre na
    /// ordem do Id (duas transações travando o mesmo par na mesma ordem não
    /// entram em deadlock: uma espera a outra e depois lê o dado já gravado).
    /// </summary>
    public async Task TravarFuncionariosAsync(params int?[] ids)
    {
        if (!Database.IsRelational())
        {
            return;
        }

        foreach (var id in ids.OfType<int>().Distinct().Order())
        {
            await Database.ExecuteSqlRawAsync("SELECT `Id` FROM `Funcionarios` WHERE `Id` = {0} FOR UPDATE", id);
        }
    }

    /// <summary>
    /// Trava a linha do gestor em modo compartilhado (FOR SHARE) até o fim da
    /// transação: vários cadastros podem colocar gente sob o mesmo gestor ao
    /// mesmo tempo, mas o desligamento dele (FOR UPDATE) espera todos
    /// terminarem — e quem chega depois do desligamento já o vê desligado.
    /// </summary>
    public async Task TravarGestorParaLeituraAsync(int? gerenteId)
    {
        if (gerenteId is int id && Database.IsRelational())
        {
            await Database.ExecuteSqlRawAsync("SELECT `Id` FROM `Funcionarios` WHERE `Id` = {0} FOR SHARE", id);
        }
    }

    /// <summary>Trava os subordinados diretos desse gestor (pra realocá-los sem disputa).</summary>
    public async Task TravarSubordinadosAsync(int gerenteId)
    {
        if (Database.IsRelational())
        {
            await Database.ExecuteSqlRawAsync("SELECT `Id` FROM `Funcionarios` WHERE `GerenteId` = {0} FOR UPDATE", gerenteId);
        }
    }

    /// <summary>
    /// Trava a conta do usuário até o fim da transação. Serializa o que o
    /// mesmo usuário faz ao mesmo tempo (ex.: 10 toques em "Registrar ponto"
    /// chegando juntos gravavam 10 "Entradas").
    /// </summary>
    public async Task TravarUsuarioAsync(int usuarioId)
    {
        if (Database.IsRelational())
        {
            await Database.ExecuteSqlRawAsync("SELECT `Id` FROM `Usuarios` WHERE `Id` = {0} FOR UPDATE", usuarioId);
        }
    }

    /// <summary>
    /// Trava a conta de acesso ligada a esse colaborador (se houver) até o fim
    /// da transação. Quem vai mexer na senha, no e-mail de login ou desligar
    /// precisa travar a CONTA antes de ler o perfil dela: a troca de perfil
    /// trava essa mesma linha, então ou termina antes (e aqui já se lê o
    /// perfil novo) ou espera. Travar só a linha do colaborador deixava o RH
    /// ler a conta como Funcionário e gravar a senha dela depois de ela já
    /// ter virado Admin.
    /// Ordem das travas em todas as rotas: globais (hierarquia → perfis) →
    /// linhas de Funcionarios → linha da conta.
    /// </summary>
    public async Task TravarContaDoFuncionarioAsync(int funcionarioId)
    {
        if (Database.IsRelational())
        {
            await Database.ExecuteSqlRawAsync("SELECT `Id` FROM `Usuarios` WHERE `FuncionarioId` = {0} FOR UPDATE", funcionarioId);
        }
    }

    /// <summary>Trava as contas Admin (usado ao rebaixar alguém, pra nunca sobrar zero Admin).</summary>
    public async Task TravarAdminsAsync()
    {
        if (Database.IsRelational())
        {
            await Database.ExecuteSqlRawAsync(
                "SELECT `Id` FROM `Usuarios` WHERE `Perfil` = {0} ORDER BY `Id` FOR UPDATE", (int)PerfilUsuario.Admin);
        }
    }

    /// <summary>Nomes das propriedades que guardam um instante (sempre preenchidas com DateTime.UtcNow).</summary>
    private static readonly HashSet<string> PropriedadesDeInstante = new() { "CriadoEm", "RegistradoEm", "RespondidaEm" };

    /// <summary>
    /// A coluna DATETIME do MySQL não guarda fuso, então o valor volta do banco
    /// como DateTimeKind.Unspecified e sai no JSON sem o "Z" — o app então
    /// interpreta como horário local e mostra 3h adiantado. Ao ler, marcamos
    /// como UTC (é o que foi gravado).
    /// </summary>
    public static readonly ValueConverter<DateTime, DateTime> ConversorUtc = new(
        valor => valor,
        valor => DateTime.SpecifyKind(valor, DateTimeKind.Utc));

    /// <summary>Mesmo conversor, pra instantes opcionais (ex.: MensagemRh.RespondidaEm).</summary>
    public static readonly ValueConverter<DateTime?, DateTime?> ConversorUtcOpcional = new(
        valor => valor,
        valor => valor.HasValue ? DateTime.SpecifyKind(valor.Value, DateTimeKind.Utc) : valor);

    // Só nos instantes: datas de calendário (Ausencia.Data, DataAdmissao,
    // DataRegistro etc.) precisam continuar sem "Z", senão JS/Dart
    // converteriam pro fuso local e mostrariam o dia anterior no Brasil.
    private static void AplicarConversaoUtcNosInstantes(ModelBuilder modelBuilder)
    {
        var propriedades = modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => PropriedadesDeInstante.Contains(p.Name));

        foreach (var propriedade in propriedades)
        {
            if (propriedade.ClrType == typeof(DateTime))
            {
                propriedade.SetValueConverter(ConversorUtc);
            }
            else if (propriedade.ClrType == typeof(DateTime?))
            {
                propriedade.SetValueConverter(ConversorUtcOpcional);
            }
        }
    }
}
