using System.Text;
using ProjetoApi.Movimentacao;
using Xunit;

namespace ProjetoApi.UnitTests;

/// <summary>Regras da movimentação sem banco: máscara de CPF, limite do conteúdo, situações e rotas (as mesmas do template Python).</summary>
public sealed class MovimentacaoRegrasTests
{
    [Theory]
    [InlineData("529.982.247-25", "***.982.247-**")]
    [InlineData("52998224725", "***.982.247-**")]
    [InlineData("529982.247-25", "***.982.247-**")]
    [InlineData("{\"documento\": \"529.982.247-25\", \"nome\": \"Ana\"}", "{\"documento\": \"***.982.247-**\", \"nome\": \"Ana\"}")]
    [InlineData("a 52998224725 e 11144477735 juntos", "a ***.982.247-** e ***.444.777-** juntos")]
    [InlineData("19.182.685/0001-62", "19.182.685/0001-62")] // CNPJ formatado não é CPF
    [InlineData("19182685000162", "19182685000162")] // CNPJ só dígitos (14) não é CPF
    [InlineData("123456789012", "123456789012")] // 12 dígitos: não casa
    [InlineData("sem numero nenhum", "sem numero nenhum")]
    public void Mascara_todo_cpf_em_qualquer_formato(string entrada, string esperado) =>
        Assert.Equal(esperado, RegrasDeMovimentacao.MascararCpfs(entrada));

    [Fact]
    public void Conteudo_vazio_vira_nulo_e_qualquer_formato_vira_texto()
    {
        Assert.Null(RegrasDeMovimentacao.ConteudoParaGravar([]));
        Assert.Equal("<a>1</a>", RegrasDeMovimentacao.ConteudoParaGravar("<a>1</a>"u8));
        Assert.Equal("ol�", RegrasDeMovimentacao.ConteudoParaGravar([.. "ol"u8, 0xFF])); // byte inválido não derruba a gravação
        Assert.Equal("José Conceição", RegrasDeMovimentacao.ConteudoParaGravar(Encoding.UTF8.GetBytes("José Conceição")));
    }

    [Fact]
    public void Conteudo_acima_do_limite_e_cortado_com_aviso()
    {
        var texto = RegrasDeMovimentacao.ConteudoParaGravar(Encoding.UTF8.GetBytes(new string('a', RegrasDeMovimentacao.LimiteDoConteudo + 500)));

        Assert.NotNull(texto);
        Assert.Equal(RegrasDeMovimentacao.LimiteDoConteudo, texto.Length);
        Assert.EndsWith("[cortado em 32768 caracteres]", texto, StringComparison.Ordinal);

        var noLimite = new string('a', RegrasDeMovimentacao.LimiteDoConteudo);
        Assert.Equal(noLimite, RegrasDeMovimentacao.ConteudoParaGravar(Encoding.UTF8.GetBytes(noLimite))); // no limite: inteiro
    }

    [Theory]
    [InlineData(200, "OK", "OK")]
    [InlineData(301, "OK", "OK")]
    [InlineData(400, "ER", "ER")]
    [InlineData(401, "ER", "ER")]
    [InlineData(422, "ER", "ER")]
    [InlineData(429, "ER", "ER")]
    [InlineData(500, "OK", "ER")]
    [InlineData(503, "OK", "ER")]
    [InlineData(504, "OK", "ER")]
    public void Situacoes_da_requisicao_e_da_resposta(int status, string requisicao, string resposta) =>
        Assert.Equal((requisicao, resposta), RegrasDeMovimentacao.Situacoes(status));

    [Fact]
    public void Numero_formatado_e_rotas_sem_movimentacao()
    {
        Assert.Equal("2026-000123", RegrasDeMovimentacao.NumeroFormatado(2026, 123));
        Assert.Equal("2027-1234567", RegrasDeMovimentacao.NumeroFormatado(2027, 1_234_567));
        Assert.False(RegrasDeMovimentacao.Movimentar("/health/live"));
        Assert.False(RegrasDeMovimentacao.Movimentar("/health/ready"));
        Assert.False(RegrasDeMovimentacao.Movimentar("/metrics"));
        Assert.True(RegrasDeMovimentacao.Movimentar("/api/v1/triagem"));
        Assert.True(RegrasDeMovimentacao.Movimentar("/nao-existe"));
        Assert.True(RegrasDeMovimentacao.Movimentar("/"));
    }
}
