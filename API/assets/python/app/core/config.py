from functools import lru_cache

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Configuração carregada de variáveis de ambiente (ou do arquivo .env)."""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    app_env: str = "dev"  # dev | staging | prod
    # Sem valor padrão e com mínimo de 32 caracteres: a aplicação não sobe sem uma chave forte.
    api_key: SecretStr = Field(..., min_length=32)
    allowed_origins: list[str] = []
    allowed_hosts: list[str] = ["localhost", "127.0.0.1"]
    request_timeout_seconds: float = Field(5.0, gt=0, le=60)
    rate_limit: str = "60/minute"  # por IP; com várias instâncias, use Redis (app/core/limiter.py)
    enable_docs: bool = False


@lru_cache
def get_settings() -> Settings:
    return Settings()  # valores obrigatórios (api_key) vêm do ambiente
