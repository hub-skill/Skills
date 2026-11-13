using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Uma requisição com chave de API válida: o que chegou, o que a API respondeu e, se a resposta foi um erro (4xx ou 5xx), os
/// metadados do erro. Gravada de uma vez só, em segundo plano, depois da resposta: a chegada não depende do banco.
/// </summary>
/// <param name="RequisicaoId">ULID gerado na chegada (o mesmo que vai ao cliente no header X-Requisicao).</param>
/// <param name="SituacaoRequisicao">OK = aceita; ER = recusada (4xx).</param>
/// <param name="SituacaoResposta">OK = sucesso (2xx); ER = erro (4xx ou 5xx).</param>
/// <param name="Erro">Metadados do erro (sem corpo, sem dados pessoais); nulo quando a resposta não foi erro.</param>
public sealed record RegistroDeMovimentacao(
    string RequisicaoId,
    string Metodo,
    string Rota,
    string? RequisicaoConteudo,
    DateTimeOffset RequisicaoEm,
    int StatusHttp,
    string? RespostaConteudo,
    DateTimeOffset RespostaEm,
    string SituacaoRequisicao,
    string SituacaoResposta,
    RegistroDeErro? Erro);

/// <summary>
/// Movimentação: o que chegou à API e o que ela respondeu, na tabela movimentacao (e o erro, na log_erros). A aplicação não tem
/// acesso de leitura às tabelas: só insere. As duas tabelas são gravadas na mesma transação (o erro aponta para a movimentação).
/// </summary>
public interface IRepositorioDeMovimentacao
{
    /// <summary>Grava o lote: cada movimentação e, quando houver, o seu erro, numa única transação.</summary>
    Task GravarAsync(IReadOnlyList<RegistroDeMovimentacao> registros, CancellationToken cancellationToken);
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
}
