namespace ProjetoApi.Contracts;

// Entrada em JSON estruturado (exemplo: pedido com cliente e itens). ADAPTE os campos à API gerada.
// Nomes em snake_case no JSON (id_externo, valor_unitario_centavos), iguais aos do Python.
// Dinheiro em centavos (inteiro): nunca double/float.

/// <summary>Cliente do pedido. E-mail opcional.</summary>
public sealed record ClienteDoPedido(string Nome, string? Email = null);

public sealed record ItemDoPedido(string Descricao, int Quantidade, long ValorUnitarioCentavos);

public sealed record EntradaPedido(
    string IdExterno,
    ClienteDoPedido Cliente,
    IReadOnlyList<ItemDoPedido> Itens,
    string? Observacao = null);

/// <summary>Só o calculado: nada de nome ou e-mail do cliente (dados pessoais não voltam na resposta).</summary>
public sealed record SaidaPedido(string IdExterno, int QuantidadeItens, long QuantidadeTotal, long ValorTotalCentavos);
