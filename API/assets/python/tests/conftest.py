import os

# Definido ANTES de importar a aplicação: main.py lê a configuração na importação.
# Sobrescreve o .env local, para os testes não dependerem da máquina.
os.environ.update(
    {
        "API_KEY": "chave-de-teste-com-pelo-menos-32-caracteres",
        "ALLOWED_HOSTS": '["test"]',
        "ENABLE_DOCS": "false",
        "RATE_LIMIT": "10000/minute",
        "REQUEST_TIMEOUT_SECONDS": "1",
    }
)
