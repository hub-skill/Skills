from functools import lru_cache

from pydantic import SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Configuração carregada de variáveis de ambiente (ou do arquivo .env)."""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    app_env: str = "dev"  # dev | staging | prod
    api_key: SecretStr  # sem valor padrão: a aplicação não sobe sem ela
    allowed_origins: list[str] = []
    allowed_hosts: list[str] = ["localhost", "127.0.0.1"]
    request_timeout_seconds: float = 5.0
    enable_docs: bool = False


@lru_cache
def get_settings() -> Settings:
    return Settings()  # type: ignore[call-arg]  # api_key vem do ambiente
