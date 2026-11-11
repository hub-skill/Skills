using System.Text;

namespace ProjetoApi.Documentos;

public enum TipoDocumento
{
    Cpf,
    Cnpj,
}

public readonly record struct DocumentoNormalizado(string Valor, TipoDocumento Tipo);

/// <summary>Validação de CPF e CNPJ, inclusive o CNPJ alfanumérico da Receita Federal.</summary>
public static class ValidadorDeDocumento
{
    /// <summary>Remove pontuação, valida os dígitos verificadores e devolve o documento só com letras e dígitos.</summary>
    public static bool TentarNormalizar(string? entrada, out DocumentoNormalizado documento)
    {
        documento = default;
        if (string.IsNullOrEmpty(entrada))
        {
            return false;
        }

        var limpo = new StringBuilder(entrada.Length);
        foreach (var c in entrada)
        {
            if (c is '.' or '-' or '/' || char.IsWhiteSpace(c))
            {
                continue;
            }

            // Só letras ASCII viram maiúsculas; qualquer outro caractere reprova na validação abaixo.
            limpo.Append(c is >= 'a' and <= 'z' ? (char)(c - 32) : c);
        }

        var doc = limpo.ToString();
        if (doc.Length == 11 && CpfValido(doc))
        {
            documento = new DocumentoNormalizado(doc, TipoDocumento.Cpf);
            return true;
        }

        if (doc.Length == 14 && CnpjValido(doc))
        {
            documento = new DocumentoNormalizado(doc, TipoDocumento.Cnpj);
            return true;
        }

        return false;
    }

    public static bool CpfValido(string digitos)
    {
        ArgumentNullException.ThrowIfNull(digitos);
        if (digitos.Length != 11 || !digitos.All(EhDigito) || digitos.All(c => c == digitos[0]))
        {
            return false;
        }

        foreach (var posicao in (ReadOnlySpan<int>)[9, 10])
        {
            var soma = 0;
            for (var i = 0; i < posicao; i++)
            {
                soma += (digitos[i] - '0') * (posicao + 1 - i);
            }

            if (soma * 10 % 11 % 10 != digitos[posicao] - '0')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Módulo 11 com pesos 2 a 9; cada caractere vale o código ASCII menos 48 (regra do CNPJ alfanumérico).</summary>
    public static bool CnpjValido(string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        if (valor.Length != 14 || valor.All(c => c == valor[0]))
        {
            return false;
        }

        for (var i = 0; i < 14; i++)
        {
            var c = valor[i];
            var valido = i < 12 ? EhDigito(c) || c is >= 'A' and <= 'Z' : EhDigito(c);
            if (!valido)
            {
                return false;
            }
        }

        foreach (var tamanho in (ReadOnlySpan<int>)[12, 13])
        {
            var soma = 0;
            for (var i = 0; i < tamanho; i++)
            {
                var peso = ((tamanho - 1 - i) % 8) + 2;
                soma += (valor[i] - '0') * peso;
            }

            var resto = soma % 11;
            var digito = resto < 2 ? 0 : 11 - resto;
            if (digito != valor[tamanho] - '0')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>CPF: ***.456.789-** (mesmo formato da CGU). CNPJ é dado público: só formata.</summary>
    public static string Mascarar(DocumentoNormalizado documento)
    {
        var d = documento.Valor;
        return documento.Tipo == TipoDocumento.Cpf
            ? $"***.{d[3..6]}.{d[6..9]}-**"
            : $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}";
    }

    /// <summary>Os 6 dígitos que a CGU deixa visíveis no CPF mascarado (posições 4 a 9).</summary>
    public static string MioloCpf(string cpf)
    {
        ArgumentNullException.ThrowIfNull(cpf);
        return cpf[3..9];
    }

    // 'c is >= 0 and <= 9' em vez de char.IsDigit: IsDigit aceitaria dígitos Unicode como "٣".
    private static bool EhDigito(char c) => c is >= '0' and <= '9';
}
