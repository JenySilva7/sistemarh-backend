using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>Regras centrais de validação (Common/): textos, e-mail, telefone, CPF, senha, dinheiro e datas.</summary>
public class ValidacaoTests
{
    // ── Textos ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("  Ana   Maria  ", "Ana Maria")]
    [InlineData("Ana\u00A0\u00A0Maria", "Ana Maria")] // NBSP vira espaço comum
    [InlineData("José da Silva", "José da Silva")]
    public void TextoDeUmaLinha_JuntaEspacosEApara(string entrada, string esperado)
    {
        Assert.Equal(esperado, Textos.ValidarNomePessoa(entrada));
    }

    [Theory]
    [InlineData("Ana\nMaria")]          // quebra de linha
    [InlineData("Ana\r\nMaria")]
    [InlineData("Ana\tMaria")]          // tab
    [InlineData("Ana\u200BMaria")]      // zero-width space
    [InlineData("Ana\u202EairaM")]      // RTL override
    [InlineData("Ana\u0000Maria")]      // caractere nulo
    [InlineData("Ana\u2028Maria")]      // separador de linha
    [InlineData("\u3164\u3164\u3164")]  // "branco" do hangul
    public void TextoDeUmaLinha_RecusaQualquerControleOuInvisivel_InclusiveQuebraDeLinha(string entrada)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Textos.ValidarNomePessoa(entrada));

        Assert.Equal("O nome contém caracteres invisíveis ou de controle, que não são permitidos.", erro.Message);
    }

    [Fact]
    public void AssuntoEDepartamentoECargo_SaoDeUmaLinha()
    {
        Assert.Throws<BusinessRuleException>(() => Textos.Validar("Assunto\ncom quebra", Textos.AssuntoMensagem));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar("Depto\ncom quebra", Textos.NomeDepartamento));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar("Cargo\ncom quebra", Textos.NomeCargo));
    }

    [Theory]
    [InlineData("Linha 1\r\nLinha 2", "Linha 1\nLinha 2")]
    [InlineData("Linha 1\rLinha 2", "Linha 1\nLinha 2")]
    [InlineData("Linha 1\n\n\n\n\nLinha 2", "Linha 1\n\nLinha 2")]         // no máximo 2 quebras seguidas
    [InlineData("\n\n  Oi,  tudo bem?  \n\n", "Oi, tudo bem?")]             // trim só nas pontas do texto
    [InlineData("Item 1 \n Item 2", "Item 1\nItem 2")]                      // cada linha é aparada
    [InlineData("a\n \n \n \n \nb", "a\n\nb")]                                // linha só de espaço vira vazia antes de juntar as quebras
    [InlineData("a\n\u00A0\n\u00A0\n\u00A0\nb", "a\n\nb")]                    // idem com NBSP
    [InlineData("x  \n  \n  \n  y", "x\n\ny")]
    public void TextoLongo_AceitaQuebraDeLinhaNormalizada(string entrada, string esperado)
    {
        Assert.Equal(esperado, Textos.Validar(entrada, Textos.CorpoMensagem));
        Assert.Equal(esperado, Textos.Validar(entrada, Textos.RespostaMensagem));
        Assert.Equal(esperado, Textos.Validar(entrada, Textos.DescricaoAusencia));
        Assert.Equal(esperado, Textos.Validar(entrada, Textos.Motivo));
    }

    [Theory]
    [InlineData("Linha 1\tLinha 2")]
    [InlineData("Linha 1\u200BLinha 2")]
    [InlineData("Linha 1\u202ELinha 2")]
    [InlineData("Linha 1\u0085Linha 2")] // NEL é controle (Cc), não é \n
    public void TextoLongo_ContinuaRecusandoOsOutrosControles(string entrada)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Textos.Validar(entrada, Textos.CorpoMensagem));

        Assert.Equal("A mensagem contém caracteres invisíveis ou de controle, que não são permitidos.", erro.Message);
    }

    [Theory]
    [InlineData("", "O nome é obrigatório.")]
    [InlineData("   ", "O nome é obrigatório.")]
    [InlineData(" ab ", "O nome precisa ter pelo menos 3 caracteres.")]
    [InlineData("123 456", "O nome precisa ter pelo menos 2 letras.")]
    [InlineData("... ---", "O nome precisa ter pelo menos 2 letras.")]
    public void NomeDePessoa_TamanhoELetras(string entrada, string mensagem)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Textos.ValidarNomePessoa(entrada));

        Assert.Equal(mensagem, erro.Message);
    }

    [Fact]
    public void NomeDePessoa_Ate120Caracteres()
    {
        Assert.Equal(120, Textos.ValidarNomePessoa(new string('a', 120)).Length);
        var erro = Assert.Throws<BusinessRuleException>(() => Textos.ValidarNomePessoa(new string('a', 121)));
        Assert.Equal("O nome pode ter no máximo 120 caracteres.", erro.Message);
        // Um nome gigante (o que derrubava o ORDER BY) é recusado sem nem ser percorrido.
        Assert.Throws<BusinessRuleException>(() => Textos.ValidarNomePessoa(new string('a', 90_000)));
    }

    [Fact]
    public void DepartamentoECargo_De2A100()
    {
        Assert.Equal("TI", Textos.Validar(" TI ", Textos.NomeDepartamento));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar("T", Textos.NomeCargo));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar(new string('c', 101), Textos.NomeCargo));
    }

    // ── E-mail ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("  Ana.Souza@Empresa.COM ", "ana.souza@empresa.com")]
    [InlineData("joao+rh@empresa.com.br", "joao+rh@empresa.com.br")]
    public void Email_ValidoEhGravadoEmMinusculas(string entrada, string esperado)
    {
        Assert.Equal(esperado, Emails.Normalizar(entrada));
    }

    [Theory]
    [InlineData("joão@empresa.com")]          // não-ASCII
    [InlineData("zedalima@gmaíl.com")]
    [InlineData("ana souza@empresa.com")]     // espaço no meio
    [InlineData("ana@empresa")]               // sem .tld
    [InlineData("ana")]
    [InlineData("<script>@x.com")]
    [InlineData("ana@@empresa.com")]
    [InlineData("ze\u0000lima@gmail.com")]    // caractere de controle
    public void Email_InvalidoEhRecusado(string entrada)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Emails.Normalizar(entrada));
        Assert.Equal("Informe um e-mail válido, no formato usuario@dominio.com.", erro.Message);
        Assert.False(Emails.TryNormalizar(entrada, out _));
    }

    [Fact]
    public void Email_AcimaDe254Caracteres_EhRecusado()
    {
        var email = new string('a', 64) + "@" + new string('b', 186) + ".com"; // 255
        var erro = Assert.Throws<BusinessRuleException>(() => Emails.Normalizar(email));
        Assert.Equal("O e-mail pode ter no máximo 254 caracteres.", erro.Message);
    }

    // ── Telefone ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("11987654321", "(11) 98765-4321")]
    [InlineData("(11) 98765-4321", "(11) 98765-4321")]
    [InlineData("1134567890", "(11) 3456-7890")]
    [InlineData("16 3456.7890", "(16) 3456-7890")]
    public void Telefone_ValidoEhGravadoFormatado(string entrada, string esperado)
    {
        Assert.Equal(esperado, Telefones.NormalizarOpcional(entrada));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("+55 11 98765-4321")]   // o painel tira o +55 antes de enviar
    [InlineData("5511987654321")]
    [InlineData("0011987654321")]
    [InlineData("10987654321")]          // DDD 10 não existe
    [InlineData("0987654321")]           // DDD 09
    [InlineData("11887654321")]          // 11 dígitos sem o 9
    [InlineData("1198765432a")]
    [InlineData("<script>")]
    [InlineData("１１９８７６５４３２１")]   // dígitos fullwidth
    public void Telefone_InvalidoEhRecusado(string entrada)
    {
        Assert.Throws<BusinessRuleException>(() => Telefones.NormalizarOpcional(entrada));
    }

    [Fact]
    public void Telefone_VazioViraNulo()
    {
        Assert.Null(Telefones.NormalizarOpcional("   "));
    }

    // ── CPF ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("52998224725", "529.982.247-25")]
    [InlineData(" 529.982.247-25 ", "529.982.247-25")]
    public void Cpf_ValidoEhGravadoComMascara(string entrada, string esperado)
    {
        Assert.Equal(esperado, Cpf.Normalizar(entrada));
    }

    [Theory]
    [InlineData("５２９９８２２４７２５")]   // dígitos fullwidth
    [InlineData("٥٢٩٩٨٢٢٤٧٢٥")]           // dígitos arábicos
    [InlineData("529.982.247-24")]        // dígito verificador errado
    [InlineData("111.111.111-11")]
    [InlineData("529x982x247x25")]
    public void Cpf_InvalidoEhRecusado(string entrada)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Cpf.Normalizar(entrada));
        Assert.Equal(Cpf.MensagemInvalido, erro.Message);
    }

    // ── Senha do app ────────────────────────────────────────────────────

    [Theory]
    [InlineData("      ", "A senha de acesso ao app não pode ser formada só por espaços.")]
    [InlineData(" senha123", "A senha de acesso ao app não pode começar nem terminar com espaço.")]
    [InlineData("senha123 ", "A senha de acesso ao app não pode começar nem terminar com espaço.")]
    [InlineData("𝓐𝓑𝓒", "A senha de acesso ao app precisa ter pelo menos 6 caracteres.")] // 3 caracteres (6 unidades UTF-16)
    [InlineData("12345", "A senha de acesso ao app precisa ter pelo menos 6 caracteres.")]
    public void Senha_Invalida(string senha, string mensagem)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Senhas.Validar(senha));
        Assert.Equal(mensagem, erro.Message);
    }

    [Fact]
    public void Senha_ContaCaracteresDeVerdade()
    {
        Senhas.Validar("𝓐𝓑𝓒𝓓𝓔𝓕");                 // 6 caracteres (12 unidades UTF-16)
        Senhas.Validar("minha senha");                 // espaço no meio pode
        Senhas.Validar(new string('a', 64));
        var erro = Assert.Throws<BusinessRuleException>(() => Senhas.Validar(new string('a', 65)));
        Assert.Equal("A senha de acesso ao app pode ter no máximo 64 caracteres.", erro.Message);
    }

    // ── Dinheiro ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0.99")]
    [InlineData("1000000.01")]
    public void Salario_ForaDaFaixa(string valor)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Dinheiro.ValidarSalario(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(Dinheiro.MensagemFaixaSalario, erro.Message);
    }

    [Fact]
    public void Dinheiro_MaisDeDuasCasas_NaoArredondaEmSilencio()
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Dinheiro.ValidarSalario(2345.678m));
        Assert.Equal("Informe o salário com no máximo 2 casas decimais.", erro.Message);
        Assert.Equal(2345.67m, Dinheiro.ValidarSalario(2345.670m));
        Assert.Equal(1_000_000m, Dinheiro.ValidarSalario(1_000_000m));
        Assert.Equal(0m, Dinheiro.ValidarVale(0m));
        Assert.Throws<BusinessRuleException>(() => Dinheiro.ValidarVale(10_000.01m));
    }

    // ── Datas ───────────────────────────────────────────────────────────

    [Fact]
    public void Nascimento_De1900AteHojeMenos14Anos()
    {
        Assert.Throws<BusinessRuleException>(() => Datas.ValidarNascimento(new DateTime(1899, 12, 31)));
        Assert.Throws<BusinessRuleException>(() => Datas.ValidarNascimento(TestUtils.Hoje.AddYears(-14).AddDays(1)));
        Assert.Equal(TestUtils.Hoje.AddYears(-14), Datas.ValidarNascimento(TestUtils.Hoje.AddYears(-14)));
        Assert.Null(Datas.ValidarNascimento(null));
    }

    [Fact]
    public void Admissao_ObrigatoriaENaFaixa()
    {
        var nascimento = new DateTime(2000, 5, 20);
        Assert.Equal("A data de admissão é obrigatória.", Assert.Throws<BusinessRuleException>(() => Datas.ValidarAdmissao(null, nascimento)).Message);
        Assert.Throws<BusinessRuleException>(() => Datas.ValidarAdmissao(new DateTime(2014, 5, 19), nascimento)); // antes dos 14 anos
        Assert.Throws<BusinessRuleException>(() => Datas.ValidarAdmissao(TestUtils.Hoje.AddDays(91), nascimento));
        Assert.Equal(TestUtils.Hoje.AddDays(90), Datas.ValidarAdmissao(TestUtils.Hoje.AddDays(90), nascimento));
        Assert.Throws<BusinessRuleException>(() => Datas.ValidarAdmissao(new DateTime(1, 1, 1), null));
    }

    [Fact]
    public void Vigencia_DaAdmissaoAteHoje()
    {
        var admissao = new DateTime(2024, 1, 1);
        Assert.Equal("Ajustes com data futura ainda não são suportados.",
            Assert.Throws<BusinessRuleException>(() => Datas.ValidarVigencia(TestUtils.Hoje.AddDays(1), admissao)).Message);
        Assert.Throws<BusinessRuleException>(() => Datas.ValidarVigencia(new DateTime(2023, 12, 31), admissao));
        Assert.Equal(TestUtils.Hoje, Datas.ValidarVigencia(TestUtils.Hoje, admissao));
    }

    [Theory]
    [InlineData("\"2026-11-17\"")]
    [InlineData("\"2026-11-17T00:00:00\"")]
    [InlineData("\"2026-11-17T00:00:00+14:00\"")] // antes virava 16/11 no fuso do servidor
    [InlineData("\"2026-11-17T23:30:00-10:00\"")] // antes virava 18/11
    [InlineData("\"2026-11-17T01:30:00Z\"")]
    public void DataDeCalendario_EhLidaComoFoiEscrita(string json)
    {
        var dto = JsonSerializer.Deserialize<AjusteSalarioDto>($"{{\"dataVigencia\":{json}}}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(new DateTime(2026, 11, 17), dto.DataVigencia);
        Assert.Equal(DateTimeKind.Unspecified, dto.DataVigencia!.Value.Kind);
    }

    [Theory]
    [InlineData("\"31/02/2026\"")]
    [InlineData("\"2026-02-31\"")]
    [InlineData("\"2026-11-17Tlixo\"")]
    [InlineData("20261117")]
    public void DataDeCalendario_EmFormatoInvalido_EhErroDeFormato(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AjusteSalarioDto>($"{{\"dataVigencia\":{json}}}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    // ── Erros de banco e de binding ─────────────────────────────────────

    [Theory]
    [InlineData(1213, 409)] // deadlock
    [InlineData(1205, 409)] // espera de trava estourou
    [InlineData(1062, 409)] // entrada duplicada
    [InlineData(1406, 400)] // texto maior que a coluna
    public void ErrosDoMySql_ViramRespostaAmigavel(int numero, int status)
    {
        Assert.Equal(status, ErrosBanco.ClassificarNumero(numero)!.StatusCode);
        Assert.Null(ErrosBanco.ClassificarNumero(1045));
    }

    [Fact]
    public void ConflitoDeVersao_Vira409()
    {
        var classificacao = ErrosBanco.Classificar(new DbUpdateConcurrencyException("x"));

        Assert.Equal(409, classificacao!.StatusCode);
        Assert.Equal(Concorrencia.MensagemConflito, classificacao.Mensagem);
    }

    [Fact]
    public void MensagensDoFramework_ViramPortuguesSemRepetirOValorEnviado()
    {
        var estado = new ModelStateDictionary();
        estado.AddModelError("departamentoId", "The value '1;DROP TABLE Funcionarios' is not valid.");
        Assert.Equal("Departamento está em formato inválido.", MensagensValidacao.Montar(estado));

        var corpoVazio = new ModelStateDictionary();
        corpoVazio.AddModelError(string.Empty, "A non-empty request body is required.");
        corpoVazio.AddModelError("dto", "The dto field is required.");
        Assert.Equal(MensagensValidacao.MensagemCorpoVazio, MensagensValidacao.Montar(corpoVazio));

        var campoInventado = new ModelStateDictionary();
        campoInventado.AddModelError("$['<script>alert(1)</script>']", "The JSON value could not be converted.");
        Assert.DoesNotContain("script", MensagensValidacao.Montar(campoInventado));

        var jsonQuebrado = new ModelStateDictionary();
        jsonQuebrado.AddModelError("$", "'x' is an invalid start of a value.");
        Assert.Equal(MensagensValidacao.MensagemJsonInvalido, MensagensValidacao.Montar(jsonQuebrado));
    }
}
