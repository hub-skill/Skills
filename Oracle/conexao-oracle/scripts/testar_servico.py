"""Testa se serviços Oracle respondem, SEM usar credenciais reais.

Uso:
    uv run --no-project --with oracledb python testar_servico.py <dsn> [<dsn> ...]
    ex.: ... testar_servico.py localhost:1521/XEPDB1 localhost:1521/XE

Tenta conectar com um usuário fictício. Interpretação:
    ORA-01017              -> serviço encontrado (esperado: só a senha foi recusada)
    ORA-12514 / DPY-6001   -> serviço não registrado no listener (nome errado)
    ORA-12541 / DPY-6005   -> nada escutando no host/porta (listener parado, firewall)
    ORA-12170 / DPY-6000   -> timeout de rede
"""

import re
import sys

import oracledb

USUARIO_FICTICIO = "usuario_inexistente_teste_conexao"

DIAGNOSTICO = {
    "ORA-01017": "OK - serviço encontrado (login recusado, como esperado)",
    "ORA-12514": "FALHA - serviço não registrado no listener (confira o nome / CDB x PDB)",
    "DPY-6001": "FALHA - serviço não registrado no listener (confira o nome / CDB x PDB)",
    "ORA-12541": "FALHA - nada escutando no host/porta (listener parado ou firewall)",
    "DPY-6005": "FALHA - não conseguiu conectar no host/porta (listener, rede ou firewall)",
    "ORA-12170": "FALHA - timeout de rede",
    "DPY-6000": "FALHA - timeout ou recusa de rede",
    "DPY-3010": "FALHA - versão do banco não suportada no modo thin (use modo thick)",
}


def testar(dsn: str) -> bool:
    try:
        conn = oracledb.connect(user=USUARIO_FICTICIO, password="x", dsn=dsn)  # noqa: S106
    except oracledb.Error as exc:
        mensagem = str(exc)
        codigos = re.findall(r"(ORA-\d{5}|DPY-\d{4})", mensagem)
        # O driver pode encadear vários códigos (ex.: DPY-6005 + DPY-6001 + ORA-12514).
        # DIAGNOSTICO está em ordem de prioridade: o primeiro presente é o mais específico.
        texto = next((t for c, t in DIAGNOSTICO.items() if c in codigos), None)
        print(f"{dsn:32} {', '.join(codigos) or '?':30} {texto or mensagem.splitlines()[0]}")
        return "ORA-01017" in codigos
    conn.close()
    print(f"{dsn:32} {'-':30} conectou com usuário fictício (inesperado: verifique o banco)")
    return False


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    dsns = sys.argv[1:]
    if not dsns:
        print(__doc__)
        return 2
    print(f"python-oracledb {oracledb.__version__} | modo thin: {oracledb.is_thin_mode()}")
    resultados = [testar(d) for d in dsns]
    return 0 if all(resultados) else 1


if __name__ == "__main__":
    sys.exit(main())
