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
    public void Rotas_sem_movimentacao()
    {
        Assert.False(RegrasDeMovimentacao.Movimentar("/health/live"));
        Assert.False(RegrasDeMovimentacao.Movimentar("/health/ready"));
        Assert.False(RegrasDeMovimentacao.Movimentar("/metrics"));
        Assert.True(RegrasDeMovimentacao.Movimentar("/api/v1/triagem"));
        Assert.True(RegrasDeMovimentacao.Movimentar("/nao-existe"));
        Assert.True(RegrasDeMovimentacao.Movimentar("/"));
    }
}

/// <summary>ULID: identificador da requisição, gerado na API (sem banco).</summary>
public sealed class UlidTests
{
    [Fact]
    public void Tem_26_caracteres_do_alfabeto_e_e_valido()
    {
        var id = Ulid.Novo();

        Assert.Equal(26, id.Length);
        Assert.True(Ulid.Valido(id));
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{26}$", id); // Crockford: sem I, L, O e U
    }

    [Fact]
    public void Carrega_a_hora_em_milissegundos()
    {
        var momento = new DateTimeOffset(2026, 11, 13, 15, 30, 45, 123, TimeSpan.Zero);

        var id = Ulid.Novo(momento);

        Assert.Equal(momento, Ulid.Momento(id));
        Assert.StartsWith("01", id, StringComparison.Ordinal); // 2026 em milissegundos desde 1970
    }

    [Fact]
    public void Ordem_alfabetica_e_ordem_cronologica()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(0, 200).Select(i => Ulid.Novo(inicio.AddMilliseconds(i * 7))).ToList();

        Assert.Equal(ids, ids.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Cem_mil_ids_no_mesmo_milissegundo_nao_repetem()
    {
        var momento = DateTimeOffset.UtcNow;

        var ids = Enumerable.Range(0, 100_000).Select(_ => Ulid.Novo(momento)).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(100_000, ids.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01JC4M7Z9Q8T2X5V6B3N0WYKH")] // 25 caracteres
    [InlineData("01JC4M7Z9Q8T2X5V6B3N0WYKHDX")] // 27 caracteres
    [InlineData("01JC4M7Z9Q8T2X5V6B3N0WYKHI")] // I não existe no alfabeto
    [InlineData("01jc4m7z9q8t2x5v6b3n0wykhd")] // minúsculas
    [InlineData("81JC4M7Z9Q8T2X5V6B3N0WYKHD")] // hora fora dos 48 bits
    public void Texto_que_nao_e_ulid_e_recusado(string? texto)
    {
        Assert.False(Ulid.Valido(texto));
    }

    [Fact]
    public void Hora_fora_do_intervalo_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ulid.Novo(DateTimeOffset.UnixEpoch.AddMilliseconds(-1)));
        Assert.Throws<ArgumentException>(() => Ulid.Momento("nao e ulid"));
    }
}
