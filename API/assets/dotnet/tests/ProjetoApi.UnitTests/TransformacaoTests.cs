using FsCheck.Xunit;
using ProjetoApi.Contracts;
using ProjetoApi.Services;
using ProjetoApi.Validation;
using Xunit;

namespace ProjetoApi.UnitTests;

/// <summary>Testes unitários do serviço (regra pura) e do validador. Troque pela regra real da API.</summary>
public sealed class TransformacaoTests
{
    [Theory]
    [InlineData("ola", "OLA")]
    [InlineData("ção", "ÇÃO")]
    public void Transforma_para_maiusculas(string entrada, string esperado) =>
        Assert.Equal(esperado, TransformacaoService.Transformar(entrada));

    [Fact]
    public async Task Servico_respeita_cancelamento()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new TransformacaoService().TransformarAsync("ola", cts.Token));
    }

    [Fact]
    public async Task Servico_para_de_esperar_quando_o_tempo_acaba_durante_o_trabalho()
    {
        // Regra lenta (até 3 s) e tempo limite de 200 ms: a espera tem de terminar no tempo limite, não em 3 s.
        using var liberar = new ManualResetEventSlim();
        var servico = new TransformacaoService(texto =>
        {
            liberar.Wait(TimeSpan.FromSeconds(3));
            return texto;
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var relogio = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => servico.TransformarAsync("ola", cts.Token));

        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(2), $"esperou {relogio.Elapsed}");
        liberar.Set();
    }

    [Theory]
    [InlineData("ola", true)]
    [InlineData("linha1\nlinha2\tcom tab", true)]   // tab, LF e CR são permitidos
    [InlineData("", false)]
    [InlineData("ola\u0000", false)]                // byte nulo
    [InlineData("ola\u0007", false)]                // caractere de controle
    public void Validador(string texto, bool valido) =>
        Assert.Equal(valido, new EntradaTextoValidator().Validate(new EntradaTexto(texto)).IsValid);

    [Fact]
    public void Validador_recusa_acima_do_tamanho_maximo() =>
        Assert.False(new EntradaTextoValidator().Validate(new EntradaTexto(new string('a', 10_001))).IsValid);

    // Propriedade: a transformação nunca muda o tamanho de textos ASCII.
    [Property(MaxTest = 300)]
    public bool Transformacao_preserva_tamanho_de_ascii(string? texto)
    {
        var ascii = new string((texto ?? string.Empty).Where(c => c < 128).ToArray());
        return TransformacaoService.Transformar(ascii).Length == ascii.Length;
    }
}
