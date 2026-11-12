import secrets

from fastapi import Depends, HTTPException, Security, status
from fastapi.security import APIKeyHeader

from app.core.config import Settings, get_settings

_api_key_header = APIKeyHeader(name="X-API-Key", auto_error=False)


def chave_valida(chave_recebida: str | None, settings: Settings) -> bool:
    """Compara a chave em tempo constante, para evitar ataques de temporização."""
    esperada = settings.api_key.get_secret_value().encode("utf-8")
    return chave_recebida is not None and secrets.compare_digest(chave_recebida.encode("utf-8"), esperada)


def verificar_api_key(
    chave_recebida: str | None = Security(_api_key_header),
    settings: Settings = Depends(get_settings),
) -> None:
    if not chave_valida(chave_recebida, settings):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Não autorizado",
            headers={"WWW-Authenticate": "APIKey"},
        )
