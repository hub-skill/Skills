import json
import logging
import sys
from typing import Any

_CAMPOS_PADRAO = set(vars(logging.makeLogRecord({})).keys()) | {"message", "asctime"}


class FormatadorJson(logging.Formatter):
    """Logs em JSON. Campos passados em `extra` entram no objeto."""

    def format(self, record: logging.LogRecord) -> str:
        dados: dict[str, Any] = {
            "ts": self.formatTime(record),
            "level": record.levelname,
            "logger": record.name,
            "msg": record.getMessage(),
        }
        for chave, valor in vars(record).items():
            if chave not in _CAMPOS_PADRAO:
                dados[chave] = valor
        if record.exc_info:
            dados["exc"] = self.formatException(record.exc_info)
        return json.dumps(dados, ensure_ascii=False, default=str)


def configurar_logs(nivel: int = logging.INFO) -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(FormatadorJson())
    raiz = logging.getLogger()
    raiz.handlers = [handler]
    raiz.setLevel(nivel)
    # httpx loga URLs completas; em nível INFO isso exporia dados enviados na query string.
    logging.getLogger("httpx").setLevel(logging.WARNING)
