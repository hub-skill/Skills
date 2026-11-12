"""Regras da movimentação (sem banco): máscara de CPF, limite do conteúdo, situações e rotas.

Validadas no Motor de Crédito (Python e .NET, com PostgreSQL e Oracle reais) e iguais nas duas linguagens.
"""

import re

LIMITE_DO_CONTEUDO = 32 * 1024  # caracteres; acima disso o conteúdo é cortado, com aviso no fim
ROTAS_SEM_MOVIMENTACAO = ("/health/", "/metrics")  # sondas e coleta de métricas: chamadas a cada poucos segundos

# CPF com ou sem pontuação, sem dígito colado antes ou depois (um CNPJ de 14 dígitos não casa).
_CPF = re.compile(r"(?<!\d)\d{3}\.?(\d{3})\.?(\d{3})-?\d{2}(?!\d)")
_AVISO_CORTE = "...[cortado em {limite} caracteres]"


def mascarar_cpfs(texto: str) -> str:
    """Troca todo CPF do texto pela máscara da CGU: 529.982.247-25 -> ***.982.247-**."""
    return _CPF.sub(lambda m: f"***.{m.group(1)}.{m.group(2)}-**", texto)


def conteudo_para_gravar(corpo: bytes) -> str | None:
    """Corpo como texto para a movimentação: UTF-8 (bytes inválidos viram �), CPF mascarado e limitado."""
    if not corpo:
        return None
    texto = mascarar_cpfs(corpo.decode("utf-8", errors="replace"))
    if len(texto) > LIMITE_DO_CONTEUDO:
        aviso = _AVISO_CORTE.format(limite=LIMITE_DO_CONTEUDO)
        texto = texto[: LIMITE_DO_CONTEUDO - len(aviso)] + aviso
    return texto


def conteudo_da_requisicao(corpo: bytes, tamanho_declarado: str | None, acima_do_limite: bool) -> str | None:
    """Conteúdo da chegada. Corpo acima do limite da API não é lido: só o tamanho fica gravado."""
    if acima_do_limite:
        return f"(corpo de {tamanho_declarado} bytes, acima do limite: não gravado)"
    return conteudo_para_gravar(corpo)


def situacoes(status_http: int) -> tuple[str, str]:
    """(situação da requisição, da resposta): 4xx = requisição recusada; 2xx = resposta de sucesso."""
    requisicao = "ER" if 400 <= status_http < 500 else "OK"
    resposta = "OK" if status_http < 400 else "ER"
    return requisicao, resposta


def movimentar(rota: str) -> bool:
    return not rota.startswith(ROTAS_SEM_MOVIMENTACAO)


def numero_formatado(ano: int, requisicao: int) -> str:
    """Valor do header X-Requisicao: 2026-000123."""
    return f"{ano}-{requisicao:06d}"
