using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjetoApi.Movimentacao;

/// <summary>Resposta da API para completar a linha da movimentação (conteúdo já mascarado e limitado).</summary>
/// <param name="SituacaoRequisicao">OK = aceita; ER = recusada (4xx).</param>
/// <param name="SituacaoResposta">OK = sucesso (2xx); ER = erro (4xx ou 5xx).</param>
public sealed record RegistroDeResposta(
    int Ano,
    long Requisicao,
    int StatusHttp,
    string? Conteudo,
    string SituacaoRequisicao,
    string SituacaoResposta);

/// <summary>
/// Movimentação: o que chegou à API e o que ela respondeu, na tabela movimentacao. A aplicação não tem acesso às
/// tabelas: só executa a função/procedure que numera e grava a chegada e a que completa a linha com a resposta.
/// </summary>
public interface IRepositorioDeMovimentacao
{
    /// <summary>Numera a requisição no ano e grava a chegada. Devolve (ano, requisicao).</summary>
    Task<(int Ano, long Requisicao)> RegistrarRequisicaoAsync(
        string metodo, string rota, string? conteudo, CancellationToken cancellationToken);

    /// <summary>Completa as linhas da movimentação com as respostas (cada linha uma vez só).</summary>
    Task RegistrarRespostasAsync(IReadOnlyList<RegistroDeResposta> respostas, CancellationToken cancellationToken);
}

/// <summary>Regras da movimentação, as mesmas do template Python (app/movimentacao/regras.py).</summary>
public static partial class RegrasDeMovimentacao
{
    /// <summary>Máximo de caracteres de conteúdo gravado; acima disso é cortado, com aviso no fim.</summary>
    public const int LimiteDoConteudo = 32 * 1024;

    // CPF com ou sem pontuação, sem dígito colado antes ou depois (um CNPJ de 14 dígitos não casa).
    [GeneratedRegex(@"(?<![0-9])[0-9]{3}\.?([0-9]{3})\.?([0-9]{3})-?[0-9]{2}(?![0-9])")]
    private static partial Regex Cpf();

    /// <summary>Troca todo CPF do texto pela máscara da CGU: 529.982.247-25 vira ***.982.247-**.</summary>
    public static string MascararCpfs(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return Cpf().Replace(texto, "***.$1.$2-**");
    }

    /// <summary>
    /// Corpo como texto para a movimentação: UTF-8 (bytes inválidos viram U+FFFD), CPF mascarado e limitado a
    /// <see cref="LimiteDoConteudo"/> caracteres. Corpo vazio = nulo.
    /// </summary>
    public static string? ConteudoParaGravar(ReadOnlySpan<byte> corpo)
    {
        if (corpo.IsEmpty)
        {
            return null;
        }

        var texto = MascararCpfs(Encoding.UTF8.GetString(corpo));
        if (texto.Length <= LimiteDoConteudo)
        {
            return texto;
        }

        var aviso = string.Create(CultureInfo.InvariantCulture, $"...[cortado em {LimiteDoConteudo} caracteres]");
        return string.Concat(texto.AsSpan(0, LimiteDoConteudo - aviso.Length), aviso);
    }

    /// <summary>(situação da requisição, situação da resposta): 4xx = requisição recusada; 2xx = resposta de sucesso.</summary>
    public static (string Requisicao, string Resposta) Situacoes(int statusHttp) =>
        (statusHttp is >= 400 and < 500 ? "ER" : "OK", statusHttp < 400 ? "OK" : "ER");

    /// <summary>Probes e scraping (chamadas a cada poucos segundos) não entram na movimentação.</summary>
    public static bool Movimentar(string rota)
    {
        ArgumentNullException.ThrowIfNull(rota);
        return !rota.StartsWith("/health/", StringComparison.OrdinalIgnoreCase)
            && !rota.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Valor do header X-Requisicao: 2026-000123.</summary>
    public static string NumeroFormatado(int ano, long requisicao) =>
        string.Create(CultureInfo.InvariantCulture, $"{ano}-{requisicao:D6}");
}
