using FsCheck.Xunit;
using ProjetoApi.Documentos;
using Xunit;

namespace ProjetoApi.UnitTests;

public sealed class ValidadorDeDocumentoTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData(" 529 982 247 25 ")]
    public void Cpf_aceita_formatos(string entrada)
    {
        Assert.True(ValidadorDeDocumento.TentarNormalizar(entrada, out var doc));
        Assert.Equal(new DocumentoNormalizado("52998224725", TipoDocumento.Cpf), doc);
    }

    [Theory]
    [InlineData("00.000.000/0001-91")]
    [InlineData("00000000000191")]
    [InlineData("19.182.685/0001-62")]
    public void Cnpj_numerico_valido(string entrada)
    {
        Assert.True(ValidadorDeDocumento.TentarNormalizar(entrada, out var doc));
        Assert.Equal(TipoDocumento.Cnpj, doc.Tipo);
        Assert.Equal(14, doc.Valor.Length);
    }

    [Fact]
    public void Cnpj_alfanumerico_exemplo_da_receita()
    {
        // Exemplo publicado pela Receita Federal para o CNPJ alfanumérico.
        Assert.True(ValidadorDeDocumento.TentarNormalizar("12.ABC.345/01DE-35", out var doc));
        Assert.Equal("12ABC34501DE35", doc.Valor);
    }

    [Fact]
    public void Cnpj_alfanumerico_minusculo_vira_maiusculo()
    {
        Assert.True(ValidadorDeDocumento.TentarNormalizar("12abc34501de35", out var doc));
        Assert.Equal("12ABC34501DE35", doc.Valor);
    }

    [Theory]
    [InlineData("52998224724")]      // CPF com dígito errado
    [InlineData("11111111111")]      // CPF repetido
    [InlineData("00000000000192")]   // CNPJ com dígito errado
    [InlineData("00000000000000")]   // CNPJ repetido
    [InlineData("1234567890")]       // curto
    [InlineData("529982247251")]     // 12 dígitos
    [InlineData("٥٢٩٩٨٢٢٤٧٢٥")]      // dígitos arábicos
    [InlineData("12ABC34501DE3A")]   // letra no dígito verificador
    [InlineData("' OR 1=1 --")]      // injeção
    [InlineData("")]
    [InlineData(null)]
    public void Documentos_invalidos(string? entrada) =>
        Assert.False(ValidadorDeDocumento.TentarNormalizar(entrada, out _));

    [Fact]
    public void Mascaras_e_miolo()
    {
        Assert.Equal("***.982.247-**", ValidadorDeDocumento.Mascarar(new("52998224725", TipoDocumento.Cpf)));
        Assert.Equal("00.000.000/0001-91", ValidadorDeDocumento.Mascarar(new("00000000000191", TipoDocumento.Cnpj)));
        Assert.Equal("982247", ValidadorDeDocumento.MioloCpf("52998224725"));
    }

    // Propriedade: para qualquer texto, ou rejeita, ou devolve um documento com dígitos verificadores corretos.
    [Property(MaxTest = 500)]
    public bool Validacao_nunca_quebra_e_so_aceita_documento_valido(string? texto)
    {
        if (!ValidadorDeDocumento.TentarNormalizar(texto, out var doc))
        {
            return true;
        }

        return doc.Tipo == TipoDocumento.Cpf ? ValidadorDeDocumento.CpfValido(doc.Valor) : ValidadorDeDocumento.CnpjValido(doc.Valor);
    }

    // Propriedade: todo CPF montado com dígitos verificadores calculados é aceito.
    [Property(MaxTest = 300)]
    public bool Cpf_gerado_com_digitos_corretos_e_aceito(uint semente)
    {
        var baseCpf = (semente % 1_000_000_000u).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        if (baseCpf.Distinct().Count() == 1)
        {
            return true; // repetidos (000000000...) são inválidos por regra
        }

        var digitos = baseCpf.Select(c => c - '0').ToList();
        foreach (var posicao in (int[])[9, 10])
        {
            var soma = 0;
            for (var i = 0; i < posicao; i++)
            {
                soma += digitos[i] * (posicao + 1 - i);
            }

            digitos.Add(soma * 10 % 11 % 10);
        }

        return ValidadorDeDocumento.CpfValido(string.Concat(digitos));
    }
}
