"""Contrato de erro da API: RFC 9457 (Problem Details).

É o mesmo contrato do template .NET (`ContratoDeErro.cs`): os dois conjuntos de testes
conferem as mesmas coisas.
"""

from httpx import Response

CAMPOS_DO_PROBLEMA = {"title", "status", "code", "request_id"}
TIPO_DO_PROBLEMA = "application/problem+json"


def afirmar_problema(resposta: Response, status: int, codigo: str) -> dict[str, object]:
    """Confere status, tipo de conteúdo, conjunto exato de campos, `status` e `code` do corpo."""
    assert resposta.status_code == status
    assert resposta.headers["content-type"].split(";")[0].strip() == TIPO_DO_PROBLEMA
    corpo: dict[str, object] = resposta.json()
    assert set(corpo) == CAMPOS_DO_PROBLEMA
    assert corpo["status"] == status
    assert corpo["code"] == codigo
    assert isinstance(corpo["title"], str) and corpo["title"]
    assert isinstance(corpo["request_id"], str) and corpo["request_id"]
    return corpo
