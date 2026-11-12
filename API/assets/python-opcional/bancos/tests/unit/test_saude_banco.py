import json

import pytest

from app.bancos.erros import BancoExpirou, BancoIndisponivel
from app.bancos.saude import resposta_pronto


@pytest.mark.parametrize("banco", ["postgres", "oracle"])
async def test_banco_saudavel_responde_ok_com_o_nome_do_banco(banco: str) -> None:
    async def verificar() -> None:
        return None

    resposta = await resposta_pronto(verificar, banco)

    assert resposta.status_code == 200
    assert json.loads(bytes(resposta.body)) == {"status": "ok", "banco": banco}


@pytest.mark.parametrize("erro", [BancoIndisponivel, BancoExpirou])
async def test_banco_fora_ou_lento_responde_503_banco_indisponivel(erro: type[Exception]) -> None:
    async def verificar() -> None:
        raise erro

    resposta = await resposta_pronto(verificar, "postgres")

    assert resposta.status_code == 503
    assert json.loads(bytes(resposta.body)) == {"status": "banco_indisponivel"}


async def test_outro_erro_nao_e_engolido() -> None:
    async def verificar() -> None:
        raise RuntimeError("bug")

    with pytest.raises(RuntimeError):
        await resposta_pronto(verificar, "postgres")
