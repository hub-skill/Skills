# Módulo opcional: entrada JSON (.NET)

Use quando a API **recebe um JSON estruturado** (vários campos, objetos aninhados, listas), em vez de um único texto.
É a opção **padrão** quando o usuário não escolhe entre String e JSON (ver SKILL.md, Passo 3).

O exemplo é um pedido com cliente e itens (`POST /api/v1/pedidos`), com os mesmos campos, limites e testes do
módulo Python. **ADAPTE** os campos, os limites e a regra de negócio; mantenha as proteções.

## Arquivos

| Arquivo | O que faz |
|---|---|
| `src/Contracts/Pedido.cs` | `EntradaPedido`, `ClienteDoPedido`, `ItemDoPedido`, `SaidaPedido` (records) |
| `src/Validation/EntradaPedidoValidator.cs` | Limites de tamanho, faixa e formato (FluentValidation) |
| `src/Endpoints/V1/PedidoEndpoints.cs` | Rota `POST /api/v1/pedidos` e a regra de negócio do exemplo |
| `src/Middleware/ProfundidadeDoJsonMiddleware.cs` | JSON aninhado fundo demais = 400, como no Python |
| `tests/EntradaJsonTests.cs` | 20 testes: válido, opcionais, 15 formas erradas (422), lista no topo, JSON fundo demais (400), sem eco |

## Passo a passo

1. Copie os arquivos de `src/` para `src/ProjetoApi/` (mesmas subpastas) e `tests/EntradaJsonTests.cs` para
   `tests/ProjetoApi.IntegrationTests/`.
2. Em `Program.cs`, complete o `ConfigureHttpJsonOptions` que já existe no template e registre o middleware e a rota:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
...
builder.Services.ConfigureHttpJsonOptions(opcoes =>
{
    opcoes.SerializerOptions.RespectNullableAnnotations = true;          // já no template
    opcoes.SerializerOptions.RespectRequiredConstructorParameters = true; // já no template
    opcoes.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; // campo desconhecido
    opcoes.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;  // "2" não vira 2 (o padrão Web aceita)
    opcoes.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower; // id_externo, como no Python
});
...
app.UseAuthorization();
app.UseMiddleware<ProfundidadeDoJsonMiddleware>(); // depois da autorização: só quem tem chave faz a API ler o corpo
...
app.MapPedidoV1();
```

3. Se a API não tiver também a rota de texto, apague `Textos.cs`, `EntradaTextoValidator.cs`,
   `TransformacaoEndpoints.cs`, o serviço de transformação e os testes deles; ajuste `Url` e `CorpoValido` em
   `ComportamentoDaApiTests.cs` para a rota JSON.
4. Rode o Passo 5 (`dotnet format`, build com 0 avisos, testes).

## Proteções (mantenha ao adaptar)

- **Campo desconhecido = 422** (`UnmappedMemberHandling.Disallow`), em qualquer nível.
- **Sem conversão de tipos** (`NumberHandling.Strict`): o padrão Web do ASP.NET aceita `"2"` como número.
- **Obrigatório ausente ou `null` em campo não anulável = 422** (`RespectRequiredConstructorParameters` e
  `RespectNullableAnnotations`); campo opcional = parâmetro com valor padrão (`string? Email = null`).
- **Limites em todo campo** no validador; **dinheiro em centavos** (`long`), nunca `double`.
- **A resposta não devolve dados pessoais** recebidos; o erro nunca ecoa a entrada.
- **Profundidade**: sem o middleware, o .NET para no primeiro tipo errado (422) e não mede a profundidade; com ele,
  acima de 1000 níveis é 400, como no Python. O teste `Json_aninhado_fundo_demais_responde_400_e_nao_500` do
  template falha sem o middleware.

## Contrato de erro da entrada JSON (igual ao Python)

| Situação | Resposta |
|---|---|
| JSON malformado ou aninhado fundo demais (mais de 1000 níveis) | 400 `requisicao_invalida` |
| JSON válido com forma errada: campo desconhecido, tipo errado, obrigatório ausente, `null` em obrigatório, fora do limite, lista no lugar de objeto | 422 `entrada_invalida` |

O `ErroDeDominioHandler` do template separa os dois casos: `JsonReaderException` (sintaxe) = 400, o resto = 422.
Diferença conhecida: `NaN`/`Infinity` são erro de leitura no .NET (400) e viram 422 no Python.

## Coleção Postman

Na coleção da API (`postman/`), troque a rota de negócio pelo corpo válido do pedido e inclua pedidos de 422 para
campo desconhecido e tipo errado.
