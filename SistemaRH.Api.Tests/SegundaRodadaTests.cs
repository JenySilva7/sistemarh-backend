using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Middleware;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Correções do reteste (2ª rodada), em testes que não precisam de servidor:
/// limite de login, textos, e-mail, senha, datas, mensagens ao RH,
/// promoção, vigência e mensagens de erro.
/// </summary>
public class SegundaRodadaTests
{
    // ── Limite de login (RT1-01 e RT1-02) ────────────────────────────────

    [Fact]
    public async Task Login_VinteTentativasEmParalelo_SoCincoConferemASenha()
    {
        var limite = new LimiteTentativasLogin();
        var conferidas = 0;

        var restantes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            var restante = await limite.ReservarAsync("ana@empresa.com", "10.0.0.1");
            if (restante is null)
            {
                Interlocked.Increment(ref conferidas);
                await Task.Delay(5);
                limite.RegistrarFalha("ana@empresa.com", "10.0.0.1");
            }
            return restante;
        })));

        Assert.Equal(LimiteTentativasLogin.MaximoFalhasPorConta, conferidas);
        Assert.All(restantes.Where(r => r is not null), r => Assert.True(r > TimeSpan.FromMinutes(14)));
    }

    [Fact]
    public async Task Login_AcertoNaoDesfazBloqueioQueJaEstaValendo()
    {
        var limite = new LimiteTentativasLogin();
        for (var i = 0; i < 5; i++)
        {
            Assert.Null(await limite.ReservarAsync("ana@empresa.com", "10.0.0.1"));
            limite.RegistrarFalha("ana@empresa.com", "10.0.0.1");
        }

        limite.RegistrarSucesso("ana@empresa.com", "10.0.0.1");

        Assert.NotNull(await limite.ReservarAsync("ana@empresa.com", "10.0.0.1"));
    }

    [Fact]
    public async Task Login_AcertoAntesDoLimite_ZeraAsFalhasDaquelaConta()
    {
        var limite = new LimiteTentativasLogin();
        for (var i = 0; i < 4; i++)
        {
            await limite.ReservarAsync("ana@empresa.com", "10.0.0.1");
            limite.RegistrarFalha("ana@empresa.com", "10.0.0.1");
        }
        await limite.ReservarAsync("ana@empresa.com", "10.0.0.1");
        limite.RegistrarSucesso("ana@empresa.com", "10.0.0.1");

        for (var i = 0; i < 4; i++)
        {
            Assert.Null(await limite.ReservarAsync("ana@empresa.com", "10.0.0.1"));
            limite.RegistrarFalha("ana@empresa.com", "10.0.0.1");
        }
        Assert.Null(await limite.ReservarAsync("ana@empresa.com", "10.0.0.1"));
    }

    [Fact]
    public async Task Login_BloqueioEhPorEmailEIp_OutroIpContinuaEntrando()
    {
        var limite = new LimiteTentativasLogin();
        for (var i = 0; i < 5; i++)
        {
            await limite.ReservarAsync("admin@empresa.com", "6.6.6.6");
            limite.RegistrarFalha("admin@empresa.com", "6.6.6.6");
        }

        Assert.NotNull(await limite.ReservarAsync("admin@empresa.com", "6.6.6.6"));
        Assert.Null(await limite.ReservarAsync("admin@empresa.com", "10.0.0.1"));
    }

    [Fact]
    public async Task Login_VinteFalhasDoMesmoIpEmEmailsDiferentes_BloqueiamOIp()
    {
        var limite = new LimiteTentativasLogin();
        for (var i = 0; i < LimiteTentativasLogin.MaximoFalhasPorIp; i++)
        {
            Assert.Null(await limite.ReservarAsync($"pessoa{i}@empresa.com", "6.6.6.6"));
            limite.RegistrarFalha($"pessoa{i}@empresa.com", "6.6.6.6");
        }

        Assert.NotNull(await limite.ReservarAsync("mais.uma@empresa.com", "6.6.6.6"));
        Assert.Null(await limite.ReservarAsync("mais.uma@empresa.com", "10.0.0.1"));
    }

    [Fact]
    public async Task Login_BloqueioAcabaDepoisDe15Minutos_ECancelarDevolveAVez()
    {
        var relogio = new RelogioFalso(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        var limite = new LimiteTentativasLogin(relogio, esperaMaxima: TimeSpan.FromMilliseconds(100));
        Assert.Null(await limite.ReservarAsync("ana@empresa.com", "ip"));
        // Uma tentativa desse e-mail em andamento: a outra espera a vez e,
        // se ela não chegar, recebe só a pausa curta (não há bloqueio).
        Assert.Equal(LimiteTentativasLogin.PausaFilaParada, await limite.ReservarAsync("ana@empresa.com", "ip"));
        limite.Cancelar("ana@empresa.com", "ip");
        for (var i = 0; i < 5; i++)
        {
            Assert.Null(await limite.ReservarAsync("ana@empresa.com", "ip"));
            limite.RegistrarFalha("ana@empresa.com", "ip");
        }
        Assert.Equal(TimeSpan.FromMinutes(15), await limite.ReservarAsync("ana@empresa.com", "ip"));

        relogio.Avancar(TimeSpan.FromMinutes(15));

        Assert.Null(await limite.ReservarAsync("ana@empresa.com", "ip"));
    }

    // ── Corpo grande demais (RT1-03) ─────────────────────────────────────

    [Fact]
    public async Task CorpoAcimaDoLimite_Da413ComMensagem_ENao500()
    {
        var middleware = new ExceptionMiddleware(
            _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            NullLogger<ExceptionMiddleware>.Instance);
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(contexto);

        Assert.Equal(413, contexto.Response.StatusCode);
        contexto.Response.Body.Position = 0;
        var json = JsonDocument.Parse(contexto.Response.Body).RootElement;
        Assert.Equal("Requisição grande demais.", json.GetProperty("mensagem").GetString());
    }

    // ── Deadlock (RT3-04) ────────────────────────────────────────────────

    [Theory]
    [InlineData(1213)]
    [InlineData(1205)]
    public void Deadlock_TemMensagemPropria_EVersaoContinuaComADeConflito(int numero)
    {
        Assert.Equal("Outra operação estava em andamento ao mesmo tempo. Tente de novo.", ErrosBanco.ClassificarNumero(numero)!.Mensagem);
        Assert.Equal(Concorrencia.MensagemConflito,
            ErrosBanco.Classificar(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("x"))!.Mensagem);
    }

    // ── E-mail (RT2-03) ──────────────────────────────────────────────────

    [Theory]
    [InlineData("o'neil@empresa.com")]
    [InlineData("ana#rh@empresa.com")]
    [InlineData("a!b$c%d&e*f+g/h=i?j^k_l`m{n|o}p~q-r@empresa.com.br")]
    [InlineData("a.b.c@sub.empresa.com")]
    public void Email_AceitaOsCaracteresComunsDoRfcNaParteLocal(string email)
    {
        Assert.Equal(email.ToLowerInvariant(), Emails.Normalizar(email));
    }

    [Fact]
    public void Email_RecusaPontoNasPontasPontoDuploELimitesDeTamanho()
    {
        foreach (var email in new[]
        {
            ".ana@empresa.com",
            "ana.@empresa.com",
            "ana..souza@empresa.com",
            new string('a', 65) + "@empresa.com",             // parte local acima de 64
            "ana@" + new string('b', 64) + ".com",            // rótulo de domínio acima de 63
            "ana@empresa." + new string('c', 64),             // TLD acima de 63
            "ana@-empresa.com",
            "ana(x)@empresa.com",
        })
        {
            Assert.Throws<BusinessRuleException>(() => Emails.Normalizar(email));
        }
        Assert.Equal("ana@" + new string('b', 63) + ".com", Emails.Normalizar("ana@" + new string('b', 63) + ".com"));
        Assert.Equal(new string('a', 64) + "@x.com", Emails.Normalizar(new string('a', 64) + "@x.com"));
    }

    // ── Nome e textos: brancos invisíveis e letras fora do BMP (RT2-03, RT2-06) ──

    [Theory]
    [InlineData("Rt\uFE0F Silva", false)]          // seletor de variação VS16 (parte de emoji: aceito no texto longo)
    [InlineData("Rt\uFE00 Silva", true)]           // VS1
    [InlineData("Rt\U000E0100 Silva", true)]       // VS17
    [InlineData("Rt\u180B Silva", true)]           // FVS1 do mongol
    [InlineData("Rt\u180F Silva", true)]           // FVS4 do mongol
    [InlineData("Rt\uFFA0 Silva", true)]           // halfwidth hangul filler
    [InlineData("Rt\u115F Silva", true)]
    [InlineData("Rt\u1160 Silva", true)]
    [InlineData("Rt\u3164 Silva", true)]
    [InlineData("Rt\u2800 Silva", true)]
    public void Nome_ComBrancoInvisivel_EhRecusado(string nome, bool recusadoNoTextoLongo)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Textos.ValidarNomePessoa(nome));

        Assert.Equal("O nome contém caracteres invisíveis ou de controle, que não são permitidos.", erro.Message);
        if (recusadoNoTextoLongo)
        {
            Assert.Throws<BusinessRuleException>(() => Textos.Validar("Mensagem " + nome, Textos.CorpoMensagem));
        }
    }

    [Fact]
    public void Nome_LetrasForaDoBmp_ContamComoLetras()
    {
        Assert.Equal("𝓐𝓷𝓪 𝓢𝓲𝓵𝓿𝓪", Textos.ValidarNomePessoa("𝓐𝓷𝓪 𝓢𝓲𝓵𝓿𝓪"));
    }

    // ── Senha (RT7-06) ───────────────────────────────────────────────────

    [Theory]
    [InlineData("\u200B\u200B\u200B\u200B\u200B\u200B")] // parece vazia
    [InlineData("abc\u0000def")]                         // NUL
    [InlineData("\uFEFFabc123")]                         // BOM
    [InlineData("\u0085abc123")]                         // NEL
    [InlineData("abc\tdef")]                             // tab
    [InlineData("abc\ndef")]
    [InlineData("abc\u2028def")]
    [InlineData("abc\u200Ddef")]                         // zero-width joiner
    [InlineData("abc\u3164def")]
    [InlineData("abc️def")]
    public void Senha_ComCaractereInvisivelOuDeControle_EhRecusada(string senha)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Senhas.Validar(senha));

        Assert.Equal(Senhas.MensagemEmojiOuInvisivel, erro.Message);
    }

    // ── Agenda: data e hora sempre de Brasília (RT8-03) ──────────────────

    [Theory]
    [InlineData("\"2026-12-01T00:30:00+14:00\"", 2026, 11, 30, 7, 30)]
    [InlineData("\"2026-12-01T02:00:00Z\"", 2026, 11, 30, 23, 0)]
    [InlineData("\"2026-12-01T02:00:00-03:00\"", 2026, 12, 1, 2, 0)]
    [InlineData("\"2026-12-01T09:15:00\"", 2026, 12, 1, 9, 15)]
    [InlineData("\"2026-12-01T09:15\"", 2026, 12, 1, 9, 15)]
    public void Agenda_DataEventoEhConvertidaPraBrasiliaSemFuso(string json, int ano, int mes, int dia, int hora, int minuto)
    {
        var dto = JsonSerializer.Deserialize<EventoAgendaCreateDto>($"{{\"dataEvento\":{json}}}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(new DateTime(ano, mes, dia, hora, minuto, 0), dto.DataEvento);
        Assert.Equal(DateTimeKind.Unspecified, dto.DataEvento!.Value.Kind);
    }

    [Fact]
    public async Task Agenda_ServicoGravaHorarioDeBrasilia_MesmoRecebendoUtc()
    {
        var agenda = new EventoAgendaService(TestUtils.CriarDbContextEmMemoria());
        var amanhaUtc = DateTime.SpecifyKind(TestUtils.Hoje.AddDays(1).AddHours(15), DateTimeKind.Utc);

        var evento = await agenda.CriarAsync(new EventoAgendaCreateDto { Titulo = "Reunião", DataEvento = amanhaUtc });

        Assert.Equal(TestUtils.Hoje.AddDays(1).AddHours(12), evento.DataEvento);
        Assert.Equal(DateTimeKind.Unspecified, evento.DataEvento.Kind);
    }

    // ── Mensagem ao RH repetida (RT8-02) ─────────────────────────────────

    private static async Task<(AppDbContext Db, int UsuarioId)> ColaboradorComConta()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var departamento = new Departamento { Nome = "TI" };
        var cargo = new Cargo { Nome = "Dev", SalarioBase = 5000m };
        var funcionario = new Funcionario { Nome = "Paula Prado", Cpf = "1", DataAdmissao = new DateTime(2024, 1, 1), Departamento = departamento, Cargo = cargo };
        db.Funcionarios.Add(funcionario);
        await db.SaveChangesAsync();
        var usuario = TestUtils.CriarUsuario(db, "paula@empresa.com", funcionarioId: funcionario.Id);
        return (db, usuario.Id);
    }

    [Fact]
    public async Task MensagemAoRh_RepetidaEmAte2Minutos_Da409()
    {
        var (db, usuarioId) = await ColaboradorComConta();
        var relogio = new RelogioFalso(DateTimeOffset.UtcNow);
        var service = new MensagemRhService(db, relogio);
        var mensagem = new MensagemRhCreateDto { Assunto = "Férias", Mensagem = "Posso tirar férias?" };
        await service.CriarAsync(usuarioId, mensagem);

        var repetida = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CriarAsync(usuarioId, new MensagemRhCreateDto { Assunto = " Férias ", Mensagem = "Posso  tirar férias?" }));
        Assert.Equal("Você acabou de enviar essa mesma mensagem. Aguarde a resposta do RH.", repetida.Message);

        // Outro texto, ou depois de 2 minutos, é mensagem nova.
        await service.CriarAsync(usuarioId, new MensagemRhCreateDto { Assunto = "Férias", Mensagem = "E em fevereiro?" });
        relogio.Avancar(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        await service.CriarAsync(usuarioId, mensagem);
        Assert.Equal(3, db.MensagensRh.Count());
    }

    [Fact]
    public async Task MensagemAoRh_JaRespondida_PodeSerEnviadaDeNovo()
    {
        var (db, usuarioId) = await ColaboradorComConta();
        var rh = TestUtils.CriarUsuario(db, "rh@empresa.com", perfil: PerfilUsuario.Rh);
        var service = new MensagemRhService(db);
        var mensagem = new MensagemRhCreateDto { Assunto = "Férias", Mensagem = "Posso tirar férias?" };
        var primeira = await service.CriarAsync(usuarioId, mensagem);
        await service.ResponderAsync(primeira.Id, new ResponderMensagemRhDto { Resposta = "Pode." }, rh.Id);

        await service.CriarAsync(usuarioId, mensagem);

        Assert.Equal(2, db.MensagensRh.Count());
    }

    // ── Promoção (RT5-02 e RT5-04) e vigência com admissão futura (RT5-05) ──

    private static FuncionarioCreateDto NovoCadastro(int departamentoId, int cargoId, string nome, int? gerenteId = null, DateTime? admissao = null) => new()
    {
        Nome = nome,
        Cpf = TestUtils.GerarCpfValido(),
        DataAdmissao = admissao ?? new DateTime(2024, 1, 1),
        DepartamentoId = departamentoId,
        CargoId = cargoId,
        GerenteId = gerenteId,
    };

    private static (int DepartamentoId, int CargoId) Lugar(AppDbContext db, string nome)
    {
        var departamento = new Departamento { Nome = nome };
        var cargo = new Cargo { Nome = "Cargo " + nome, SalarioBase = 5000m };
        db.AddRange(departamento, cargo);
        db.SaveChanges();
        return (departamento.Id, cargo.Id);
    }

    private static PromocaoDto Promocao((int DepartamentoId, int CargoId) destino, decimal salario, int? novoGerenteId = null) => new()
    {
        NovoCargoId = destino.CargoId,
        NovoDepartamentoId = destino.DepartamentoId,
        NovoSalario = salario,
        DataVigencia = TestUtils.Hoje,
        Motivo = "Promoção",
        NovoGerenteId = novoGerenteId,
    };

    [Fact]
    public async Task Promover_ComNovoGerenteIgualAoAtualMasDesligado_Lanca400()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var service = new FuncionarioService(db);
        var ti = Lugar(db, "TI");
        var vendas = Lugar(db, "Vendas");
        var gestor = await service.CriarAsync(NovoCadastro(ti.DepartamentoId, ti.CargoId, "Gestor Saindo"));
        var pessoa = await service.CriarAsync(NovoCadastro(ti.DepartamentoId, ti.CargoId, "Pessoa Promovida", gestor.Id));
        // Dado antigo: o gestor foi desligado sem realocar a pessoa.
        db.Funcionarios.Single(f => f.Id == gestor.Id).Status = StatusFuncionario.Desligado;
        await db.SaveChangesAsync();

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.PromoverAsync(pessoa.Id, Promocao(vendas, 6000m, novoGerenteId: gestor.Id), 1));

        Assert.Equal("O gestor escolhido está desligado. Escolha um gestor ativo.", erro.Message);
    }

    [Fact]
    public async Task Promover_SemMudarCargoNemDepartamento_MensagemDependeDoQueMudou()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var service = new FuncionarioService(db);
        var ti = Lugar(db, "TI");
        var outroGestor = await service.CriarAsync(NovoCadastro(ti.DepartamentoId, ti.CargoId, "Outro Gestor"));
        var pessoa = await service.CriarAsync(NovoCadastro(ti.DepartamentoId, ti.CargoId, "Pessoa Teste"));

        var nadaMudou = await Assert.ThrowsAsync<BusinessRuleException>(() => service.PromoverAsync(pessoa.Id, Promocao(ti, 5000m), 1));
        var soGestor = await Assert.ThrowsAsync<BusinessRuleException>(() => service.PromoverAsync(pessoa.Id, Promocao(ti, 5000m, outroGestor.Id), 1));
        var soSalario = await Assert.ThrowsAsync<BusinessRuleException>(() => service.PromoverAsync(pessoa.Id, Promocao(ti, 7000m), 1));

        Assert.Equal("A promoção precisa mudar o cargo ou o departamento.", nadaMudou.Message);
        Assert.Equal("A promoção precisa mudar o cargo ou o departamento.", soGestor.Message);
        Assert.Equal("Para alterar só o salário, use Ajuste Salarial.", soSalario.Message);
    }

    [Fact]
    public async Task Promover_DuasIguaisComAVersaoDaTela_ASegundaRecebe409()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var service = new FuncionarioService(db);
        var ti = Lugar(db, "TI");
        var vendas = Lugar(db, "Vendas");
        var pessoa = await service.CriarAsync(NovoCadastro(ti.DepartamentoId, ti.CargoId, "Pessoa Teste"));
        var promocao = Promocao(vendas, 6000m);
        promocao.Versao = pessoa.Versao;
        await service.PromoverAsync(pessoa.Id, promocao, 1);

        await Assert.ThrowsAsync<ConflictException>(() => service.PromoverAsync(pessoa.Id, promocao, 1));
    }

    [Fact]
    public async Task AdmissaoFutura_VigenciaEhAPropriaAdmissao()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var service = new FuncionarioService(db);
        var ti = Lugar(db, "TI");
        var admissao = TestUtils.Hoje.AddDays(30);
        var pessoa = await service.CriarAsync(NovoCadastro(ti.DepartamentoId, ti.CargoId, "Pessoa Futura", admissao: admissao));

        var hoje = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarValeAlimentacaoAsync(pessoa.Id,
            new AjusteValeAlimentacaoDto { NovoValor = 700m, DataVigencia = TestUtils.Hoje, Motivo = "Valor certo" }, 1));
        var resultado = await service.AjustarValeAlimentacaoAsync(pessoa.Id,
            new AjusteValeAlimentacaoDto { NovoValor = 700m, DataVigencia = admissao, Motivo = "Valor certo" }, 1);

        Assert.Equal($"Para quem ainda vai ser admitido, a vigência é a própria data de admissão ({Datas.Formatar(admissao)}).", hoje.Message);
        Assert.Equal(700m, resultado.ValeAlimentacao);
        Assert.Equal(admissao, db.AjustesRemuneracao.Single().DataVigencia);
    }
}

/// <summary>Correções do reteste (2ª rodada) que passam pelo HTTP de verdade (em memória).</summary>
public class SegundaRodadaIntegracaoTests : IClassFixture<IntegrationTestFactory>
{
    private readonly ApiDeTeste _api;

    public SegundaRodadaIntegracaoTests(IntegrationTestFactory factory)
    {
        _api = new ApiDeTeste(factory);
    }

    private async Task<HttpResponseMessage> LoginDoIp(string email, string senha, string ip)
    {
        var cliente = _api.Cliente();
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return await cliente.PostAsJsonAsync("/api/auth/login", new { email, senha });
    }

    [Fact]
    public async Task Login_VinteEmParalelo_SoCincoTestamASenha_EOutroIpContinuaEntrando()
    {
        var (_, email, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => LoginDoIp(email, $"errada{i:00}", "203.0.113.10")));
        var certaDoMesmoIp = await LoginDoIp(email, "senha123", "203.0.113.10");
        var certaDeOutroIp = await LoginDoIp(email, "senha123", "198.51.100.20");

        Assert.Equal(5, respostas.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(15, respostas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.TooManyRequests, certaDoMesmoIp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, certaDeOutroIp.StatusCode);
    }

    [Fact]
    public async Task Login_VinteFalhasDoMesmoIpEmVariosEmails_BloqueiamOIp()
    {
        for (var i = 0; i < 20; i++)
        {
            var falha = await LoginDoIp($"spray{i}-{Guid.NewGuid():N}@empresa.com", "123456", "203.0.113.99");
            Assert.Equal(HttpStatusCode.BadRequest, falha.StatusCode);
        }
        var (_, email, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginDoIp(email, "senha123", "203.0.113.99")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(email, "senha123", "203.0.113.100")).StatusCode);
    }

    [Fact]
    public async Task NumeroComoTexto_EhRecusado()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var (departamentoId, cargoId) = await _api.CriarDepartamentoECargo();

        var cadastro = await rh.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Pessoa Teste",
            cpf = TestUtils.GerarCpfValido(),
            dataAdmissao = "2025-03-10",
            departamentoId,
            cargoId,
            salario = "5.000",
        });
        var idComoTexto = await rh.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Pessoa Teste",
            cpf = TestUtils.GerarCpfValido(),
            dataAdmissao = "2025-03-10",
            departamentoId = departamentoId.ToString(),
            cargoId,
        });

        Assert.Equal(HttpStatusCode.BadRequest, cadastro.StatusCode);
        Assert.Equal("Salário está em formato inválido.", await ApiDeTeste.LerMensagem(cadastro));
        Assert.Equal(HttpStatusCode.BadRequest, idComoTexto.StatusCode);
    }

    [Fact]
    public async Task JsonTruncado_NaoApontaCampo()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);

        var resposta = await _api.Cliente(tokenRh).PostAsync("/api/funcionarios",
            new StringContent("{\"nome\": \"RT2", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("O corpo da requisição não é um JSON válido.", await ApiDeTeste.LerMensagem(resposta));
    }

    [Theory]
    [InlineData("{\"status\": 0}")]
    [InlineData("{\"status\": 99}")]
    [InlineData("{\"status\": -1}")]
    [InlineData("{\"status\": \"abc\"}")]
    [InlineData("{\"status\": null}")]
    [InlineData("{}")]
    public async Task StatusInvalidoNoCorpo_TemSempreAMesmaMensagem(string corpo)
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);

        var resposta = await _api.Cliente(tokenRh).PutAsync("/api/rh/ausencias/1/status",
            new StringContent(corpo, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Status inválido.", await ApiDeTeste.LerMensagem(resposta));
    }

    [Theory]
    [InlineData("/api/rh/ausencias?status=9")]
    [InlineData("/api/rh/ausencias?status=abc")]
    [InlineData("/api/rh/mensagens?status=7")]
    [InlineData("/api/funcionarios?status=abc")]
    public async Task StatusInvalidoNaQuery_TemSempreAMesmaMensagem(string url)
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);

        var resposta = await _api.Cliente(tokenRh).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Status inválido.", await ApiDeTeste.LerMensagem(resposta));
    }

    [Fact]
    public async Task Agenda_PostEGetDevolvemOMesmoHorarioDeBrasilia()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var dia = TestUtils.Hoje.AddDays(10);
        var instanteUtc = TestUtils.Iso(dia) + "T15:00:00Z";       // 12:00 em Brasília
        var comFuso = TestUtils.Iso(dia) + "T22:00:00+10:00";      // 09:00 em Brasília

        var criadoUtc = await ApiDeTeste.LerJson(await rh.PostAsJsonAsync("/api/agenda", new { titulo = "Reunião Z", dataEvento = instanteUtc }));
        var criadoFuso = await ApiDeTeste.LerJson(await rh.PostAsJsonAsync("/api/agenda", new { titulo = "Reunião +10", dataEvento = comFuso }));
        var lista = (await ApiDeTeste.LerJson(await rh.GetAsync($"/api/agenda?de={TestUtils.Iso(dia)}&ate={TestUtils.Iso(dia.AddDays(1))}"))).EnumerateArray().ToList();

        Assert.Equal(TestUtils.Iso(dia) + "T12:00:00", criadoUtc.GetProperty("dataEvento").GetString());
        Assert.Equal(TestUtils.Iso(dia) + "T09:00:00", criadoFuso.GetProperty("dataEvento").GetString());
        Assert.Contains(lista, e => e.GetProperty("dataEvento").GetString() == TestUtils.Iso(dia) + "T12:00:00");
        Assert.Contains(lista, e => e.GetProperty("dataEvento").GetString() == TestUtils.Iso(dia) + "T09:00:00");
    }

    [Fact]
    public async Task MensagemAoRh_EnvioRepetido_Da409()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var (_, _, tokenApp) = await _api.CriarColaboradorComAcesso(_api.Cliente(tokenRh));
        var app = _api.Cliente(tokenApp);
        var corpo = new { assunto = "Holerite", mensagem = "Não recebi o holerite." };

        var primeira = await app.PostAsJsonAsync("/api/mensagens-rh", corpo);
        var repetida = await app.PostAsJsonAsync("/api/mensagens-rh", corpo);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
    }

    [Fact]
    public async Task RhNaoAssumeContaAdminLigadaAColaborador()
    {
        // RT6-01: o Admin promove a conta de um colaborador a Admin; o RH não
        // pode redefinir a senha, trocar o e-mail de login nem desligar.
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var admin = _api.Cliente(tokenAdmin);
        var (funcionario, email, _) = await _api.CriarColaboradorComAcesso(rh, "Futura Admin");
        var funcionarioId = funcionario.GetProperty("id").GetInt32();
        var contaId = _api.LerBanco(db => db.Usuarios.Single(u => u.Email == email).Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/usuarios/{contaId}/perfil", new { perfil = 2, perfilAtual = 0, senhaProvisoria = "provisoria1" })).StatusCode);

        var senha = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/acesso-app", new { senha = "senhaDoRh1" });
        var emailNovo = await rh.PutAsJsonAsync($"/api/funcionarios/{funcionarioId}", new
        {
            nome = "Futura Admin",
            email = $"rh-controla-{Guid.NewGuid():N}@empresa.com",
            departamentoId = funcionario.GetProperty("departamentoId").GetInt32(),
            cargoId = funcionario.GetProperty("cargoId").GetInt32(),
        });
        var desligar = await rh.PostAsync($"/api/funcionarios/{funcionarioId}/desligar", null);

        Assert.Equal(HttpStatusCode.Forbidden, senha.StatusCode);
        Assert.Equal("Somente um administrador pode alterar o acesso de contas RH/Admin.", await ApiDeTeste.LerMensagem(senha));
        Assert.Equal(HttpStatusCode.Forbidden, emailNovo.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, desligar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.TentarLogin(email, "senhaDoRh1")).StatusCode);

        // O Admin pode.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/acesso-app", new { senha = "senhaDoAdmin1" })).StatusCode);
    }

    [Fact]
    public async Task AlterarPerfil_ComPerfilAtualDaTelaVelha_Da409()
    {
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (alvoId, _, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var admin = _api.Cliente(tokenAdmin);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/usuarios/{alvoId}/perfil", new { perfil = 2, perfilAtual = 0, senhaProvisoria = "provisoria1" })).StatusCode);
        var telaVelha = await admin.PutAsJsonAsync($"/api/usuarios/{alvoId}/perfil", new { perfil = 1, perfilAtual = 0 });

        Assert.Equal(HttpStatusCode.Conflict, telaVelha.StatusCode);
        Assert.Equal("Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo.", await ApiDeTeste.LerMensagem(telaVelha));
    }
}
