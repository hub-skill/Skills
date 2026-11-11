"""Testa se um servidor PostgreSQL responde, SEM usar credenciais reais.

Uso:
    uv run --no-project --with "psycopg[binary]" python testar_servidor.py "<conninfo>" ["<conninfo>" ...]
    ex.: ... testar_servidor.py "host=127.0.0.1 port=5432 dbname=postgres"
         ... testar_servidor.py "postgresql://db.exemplo.com:5432/app?sslmode=require"

O script força um usuário fictício e uma senha falsa (user/password passados na
string são ignorados). Interpretação:
    servidor pediu senha        -> OK: no ar, porta certa, pg_hba.conf aceitou o pedido
    FATAL sem pedir senha       -> servidor respondeu, mas recusou antes da senha
                                   (pg_hba.conf sem regra para host/usuário/banco, ou SSL exigido)
    ConnectionTimeout/recusado  -> nada escutando (porta, serviço parado, listen_addresses, firewall)

Limitação: o nome do banco NÃO é validado (o PostgreSQL autentica antes de checar o banco).

Por que não usar sqlstate nem o texto: na fase de conexão o psycopg devolve sqlstate=None,
e a mensagem vem no idioma do servidor (no Windows, muitas vezes com acentuação corrompida).
"""

import sys

import psycopg

USUARIO_FICTICIO = "usuario_inexistente_teste_conexao"


def testar(conninfo: str) -> bool:
    try:
        conn = psycopg.connect(
            conninfo,
            user=USUARIO_FICTICIO,
            password="x",  # noqa: S106 - senha propositalmente falsa
            connect_timeout=5,
            application_name="teste-servidor",
        )
    except psycopg.errors.ConnectionTimeout:
        print(f"{conninfo:50} FALHA - sem resposta no tempo limite (porta, serviço parado, firewall)")
        return False
    except psycopg.Error as exc:
        # Nunca imprima exc.pgconn: o repr dele contém a senha em texto puro.
        pgconn = exc.pgconn
        usou_senha = bool(pgconn is not None and pgconn.used_password)
        respondeu = "FATAL" in str(exc)  # a severidade FATAL não é traduzida pelo servidor
        if usou_senha:
            print(f"{conninfo:50} OK - servidor pediu senha (no ar; pg_hba.conf aceitou o pedido)")
            return True
        if respondeu:
            print(f"{conninfo:50} FALHA - servidor recusou antes da senha "
                  "(pg_hba.conf sem regra para host/usuário/banco, ou SSL exigido)")
            return False
        print(f"{conninfo:50} FALHA - não conectou: {type(exc).__name__} "
              "(porta, serviço parado, listen_addresses, firewall ou DNS)")
        return False
    conn.close()
    print(f"{conninfo:50} ATENÇÃO - conectou com usuário fictício: o pg_hba.conf usa 'trust'?")
    return False


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    alvos = sys.argv[1:]
    if not alvos:
        print(__doc__)
        return 2
    print(f"psycopg {psycopg.__version__} ({psycopg.pq.__impl__}) | libpq {psycopg.pq.version()}")
    resultados = [testar(a) for a in alvos]
    return 0 if all(resultados) else 1


if __name__ == "__main__":
    sys.exit(main())
