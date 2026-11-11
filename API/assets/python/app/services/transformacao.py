from collections.abc import Callable

Transformador = Callable[[str], str]


def transformar(texto: str) -> str:
    """Regra de negócio. Substitua pela transformação real.

    Mantenha a função pura: sem I/O, sem estado global e sem dependência de HTTP.
    """
    return texto.upper()


def get_transformador() -> Transformador:
    """Dependência da rota. Nos testes, sobrescrita para simular lentidão (504) ou erro inesperado (500)."""
    return transformar
