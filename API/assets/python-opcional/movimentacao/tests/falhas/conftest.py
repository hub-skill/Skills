import asyncio
from collections.abc import Callable, Mapping

import pytest


def pytest_asyncio_loop_factories(
    config: pytest.Config, item: pytest.Item
) -> Mapping[str, Callable[[], asyncio.AbstractEventLoop]]:
    # psycopg async não funciona com o ProactorEventLoop (padrão no Windows).
    # SelectorEventLoop funciona em todos os sistemas, então vale para Linux/macOS também.
    return {"selector": asyncio.SelectorEventLoop}
