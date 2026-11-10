import secrets

from fastapi import Depends, HTTPException, Security, status
from fastapi.security import APIKeyHeader

from app.core.config import Settings, get_settings

_api_key_header = APIKeyHeader(name="X-API-Key", auto_error=False)


def verificar_api_key(
    chave_recebida: str | None = Security(_api_key_header),
    settings: Settings = Depends(get_settings),
) -> None:
    """Compara a chave em tempo constante, para evitar ataques de temporização."""
    chave_esperada = settings.api_key.get_secret_value().encode("utf-8")
    if chave_recebida is None or not secrets.compare_digest(
        chave_recebida.encode("utf-8"), chave_esperada
    ):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Não autorizado",
            headers={"WWW-Authenticate": "APIKey"},
        )
