using System.Security.Cryptography;

namespace ProjetoApi.Movimentacao;

/// <summary>
/// Identificador da requisição: ULID (26 caracteres, alfabeto Crockford em maiúsculas), gerado na própria API, sem banco.
/// 48 bits de hora em milissegundos + 80 bits aleatórios (gerador criptográfico). Dois ULID nunca repetem na prática e o texto,
/// em ordem alfabética, é a ordem cronológica. Não é sequencial e não reinicia a cada ano.
/// </summary>
public static class Ulid
{
    public const int Tamanho = 26;

    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Novo() => Novo(DateTimeOffset.UtcNow);

    public static string Novo(DateTimeOffset momento)
    {
        var ms = momento.ToUnixTimeMilliseconds();
        if (ms is < 0 or >= (1L << 48))
        {
            throw new ArgumentOutOfRangeException(nameof(momento), "Fora do intervalo de 48 bits de milissegundos.");
        }

        Span<char> saida = stackalloc char[Tamanho];
        for (var i = 9; i >= 0; i--) // 10 caracteres de hora, do menos para o mais significativo
        {
            saida[i] = Alfabeto[(int)(ms & 31)];
            ms >>= 5;
        }

        Span<byte> aleatorio = stackalloc byte[10]; // 80 bits = 16 caracteres de 5 bits
        RandomNumberGenerator.Fill(aleatorio);
        var acumulado = 0;
        var bits = 0;
        var posicao = 10;
        foreach (var b in aleatorio)
        {
            acumulado = (acumulado << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                saida[posicao++] = Alfabeto[(acumulado >> bits) & 31];
            }

            acumulado &= (1 << bits) - 1;
        }

        return new string(saida);
    }

    /// <summary>26 caracteres do alfabeto, e hora dentro dos 48 bits (o primeiro caractere vai até 7).</summary>
    public static bool Valido(string? texto)
    {
        if (texto is not { Length: Tamanho } || texto[0] > '7')
        {
            return false;
        }

        foreach (var c in texto)
        {
            if (Alfabeto.IndexOf(c, StringComparison.Ordinal) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A hora (UTC) que o ULID carrega, com precisão de milissegundo.</summary>
    public static DateTimeOffset Momento(string ulid)
    {
        if (!Valido(ulid))
        {
            throw new ArgumentException("ULID inválido.", nameof(ulid));
        }

        long ms = 0;
        for (var i = 0; i < 10; i++)
        {
            ms = (ms << 5) | (long)Alfabeto.IndexOf(ulid[i], StringComparison.Ordinal);
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }
}
