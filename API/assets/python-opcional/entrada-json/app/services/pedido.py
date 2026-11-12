"""Regra de negócio do exemplo: totaliza o pedido. ADAPTE à API gerada."""

from app.schemas.pedido import EntradaPedido, SaidaPedido


def totalizar(pedido: EntradaPedido) -> SaidaPedido:
    return SaidaPedido(
        id_externo=pedido.id_externo,
        quantidade_itens=len(pedido.itens),
        quantidade_total=sum(item.quantidade for item in pedido.itens),
        valor_total_centavos=sum(item.quantidade * item.valor_unitario_centavos for item in pedido.itens),
    )
