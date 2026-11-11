"""Falhas de banco já traduzidas: a API responde 503 (indisponível) ou 504 (expirou), nunca 500.

Registre os handlers em main.py (ver LEIAME.md) e nunca registre em log o objeto de erro do driver
(o do psycopg carrega a senha em texto puro em exc.pgconn).
"""

from app.core.exceptions import ErroDeDominio


class BancoIndisponivel(ErroDeDominio):
    status_code = 503
    codigo = "banco_indisponivel"
    mensagem = "Banco de dados indisponível. Tente novamente."


class BancoExpirou(ErroDeDominio):
    status_code = 504
    codigo = "banco_timeout"
    mensagem = "O banco de dados não respondeu dentro do tempo limite."
