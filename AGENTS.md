# AGENTS.md

## Objetivo

Este repositório implementa o NotaFlow, uma solução demonstrativa de emissão de notas fiscais. A prioridade é preservar consistência do estoque e capacidade de recuperação; a IA é assistiva e nunca executa sozinha.

Regra nova, invariante ou comando entra **neste arquivo**, que todo agente lê. O `CLAUDE.md` apenas o importa e guarda o que é mecânica exclusiva do Claude Code; `scripts/check-agent-docs.sh` verifica isso, como qualquer outra regra daqui.

Não mova regra para `.claude/rules/` nem para `AGENTS.md` de subpasta: o primeiro o Codex não lê, o segundo o Claude Code não lê. Instrução dividida por ferramenta é instrução que metade dos agentes não recebe.

## Invariantes

Regras numeradas e testáveis. Use o número no nome do teste e na mensagem de commit — assim cada regra tem um teste apontável, em vez de "temos testes".

| # | Regra | Onde vive |
|---|---|---|
| **INV-01** | Porta única: só `StockDebitService` escreve saldo e movimento | `services/inventory/.../Application/StockDebitService.cs` |
| **INV-02** | `Inventory` é o único dono de produtos e saldos | — |
| **INV-03** | `Billing` é o único dono de notas e tentativas de fechamento | — |
| **INV-04** | Item que não movimenta estoque é **reportado**, nunca ignorado em silêncio | `StockDebitService`, campo `IgnoredItemsJson` |
| **INV-05** | Um serviço nunca acessa banco, contexto EF ou migrations do outro | — |
| **INV-06** | Baixa usa REST, transação local e `Idempotency-Key` | `StockDebitService`, `InventoryClient` |
| **INV-07** | Saldo nunca fica negativo; produto sem controle não valida saldo | `Product.CanFulfill` + `CK_Products_Balance` |
| **INV-08** | Quantidade de movimento é sempre positiva | `CK_StockMovements_Quantity` |
| **INV-09** | Saldo é projeção verificável do extrato, com comando de reconciliação | `Application/StockReconciliation.cs` |
| **INV-10** | Baixa é tudo ou nada: valida todos os itens antes de debitar qualquer um | `StockDebitService` |
| **INV-11** | Mesma chave + mesmo payload devolve o resultado gravado | `UX_StockDebitOperations_AttemptId` |
| **INV-12** | Mesma chave + payload diferente devolve `IDEMPOTENCY_KEY_REUSED` | `StockDebitService.EnsureSamePayload` |
| **INV-13** | Resultado desconhecido nunca é tratado como sucesso; reconcilia com a mesma chave | `ClosureCoordinator` |
| **INV-14** | Concorrência é detectada: lock consultivo, `FOR UPDATE` e token otimista | `StockDebitService`, `Invoice.Version` |
| **INV-15** | Movimento nunca é apagado; o extrato é append-only | `StockMovements` |
| **INV-16** | Nota é imutável após criação | `Invoice` |
| **INV-17** | Refechar nota fechada devolve o resultado terminal, sem nova baixa | `InvoiceService.BeginClosureAsync` |
| **INV-18** | PDF só existe para nota fechada; segunda via não redebita | `InvoiceEndpoints` |
| **INV-19** | Todo movimento aponta para a operação e a nota de origem | `StockMovement` |
| **INV-20** | Toda operação registra quem confirmou | `Invoice.CreatedBy` / `ClosedBy` |
| **INV-21** | Erro externo vira `ProblemDetails` sanitizado com `traceId` | `ExceptionHandlingMiddleware` |
| **INV-22** | Nunca registrar chave, token, imagem, prompt integral ou raciocínio do modelo | — |
| **INV-23** | O fluxo manual funciona sem OpenAI | `AiDraftService` → `AI_DISABLED` |

## IA — o que mudou e por quê

> **Mudança deliberada de um limite antes declarado inviolável.**
> Até 18/08/2026 o MCP era somente leitura e a IA não podia persistir nem fechar nota.
> Por decisão do responsável pelo produto, a IA passou a poder **escrever**.

O que **não** mudou, e é o que sustenta a segurança:

- **INV-24 — A IA nunca executa sozinha.** Ela devolve uma **ação proposta** tipada; a interface mostra exatamente o que será feito; e só após confirmação humana o backend executa.
- **INV-25 — A confirmação é controle de servidor.** A ação carrega assinatura HMAC e prazo de validade (`ProposedActionService`). Confirmação apenas na interface seria contornável por prompt injection chamando o endpoint direto.
- **INV-26 — A execução revalida tudo.** A proposta é sugestão, não autorização: existência de produto e saldo são checados de novo no momento de confirmar.
- **INV-27 — As tools MCP continuam somente leitura.** `search_products`, `get_product` e `check_availability`. Criar ou fechar nota **não** virou tool: o servidor MCP é do Inventory e nota é domínio do Billing, que é o cliente MCP — uma tool assim faria o Billing chamar a si mesmo pelo protocolo. As tools recebem `IReadOnlyProductCatalog`, que não tem método de escrita: a anotação `ReadOnly` declara a intenção, o tipo é o que a torna inalcançável.

O que se perdeu: antes a IA não podia causar dano porque não tinha ferramenta de escrita. Agora o argumento é "confiamos na confirmação assinada e na revalidação". É mais fraco, e por isso os controles acima são obrigatórios e testados em `ProposedActionServiceTests`.

## Ferramentas exigidas

| Ferramenta | Versão | Como conferir |
|---|---|---|
| .NET SDK | fixado em `global.json` (`10.0.400`, `latestPatch`) | `dotnet --version` |
| Node.js | **≥ 22.22.3**, ou 24 como no CI | `node --version` |
| Docker | daemon **em execução**, não só o cliente | `docker info` |
| Chromium | qualquer, apontado por `CHROME_BIN` | `"$CHROME_BIN" --version` |

Duas armadilhas que já custaram tempo:

- O Angular CLI **recusa** Node abaixo de 22.22.3 e, ao recusar, imprime a mensagem e **sai com código 0**. Um agente que confere só o código de saída conclui que o lint passou. Confira a versão antes, não o exit status depois.
- `docker --version` responde com o daemon desligado. Só `docker info` prova que a Camada 3 é executável.

### Sandboxes de agente

Os contêineres de sessão remota — Codex cloud e Claude Code na web — sobem **sem .NET SDK, com Node anterior ao mínimo do Angular CLI e sem daemon Docker**. Os dois primeiros se resolvem instalando no início da sessão. O terceiro não tem conserto: a **Camada 3 não é executável em sandbox**. Não tente subir Testcontainers nem `docker compose` lá; declare no relatório que ela ficou por executar.

## Comandos de validação

Três camadas, por dependência externa. Rode a maior camada que o ambiente permitir e **declare no relatório final qual delas não rodou** — silenciar isso é reportar verde falso.

**Camada 1 — sem dependência externa.** É o mínimo obrigatório de qualquer alteração; roda em qualquer sandbox de agente.

```bash
./scripts/check-agent-docs.sh
dotnet restore NotaFlow.slnx
dotnet build NotaFlow.slnx --no-restore
dotnet format NotaFlow.slnx --verify-no-changes --no-restore
dotnet test tests/billing/Billing.Api.Tests.csproj --no-build
dotnet test tests/inventory/Inventory.UnitTests/Inventory.UnitTests.csproj --no-build
(cd frontend && npm ci && npm run lint && npm run build:production)
```

**Camada 2 — exige Chromium.** Testes do frontend; sem `CHROME_BIN` o Karma falha por timeout, sem dizer que o navegador nunca subiu.

```bash
(cd frontend && npm test)
```

**Camada 3 — exige daemon Docker.** Integração dos dois serviços contra PostgreSQL real via Testcontainers, mais a validação da configuração do Compose. O **smoke HTTP** que sobe a stack, faz login, fecha nota e confere saldo e rotas internas roda **só no `quality-gate`** — localmente ele não é reproduzido, para não manter setenta linhas de `curl` divergindo do CI em silêncio.

```bash
dotnet test tests/billing/Billing.IntegrationTests/Billing.IntegrationTests.csproj
dotnet test tests/inventory/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj
docker compose config --quiet
```

`scripts/verify.sh` executa a Camada 1; `scripts/verify.sh --all` tenta as três. Falta de Chromium ou Docker **avisa em vez de falhar**, e o script lista no fim o que não rodou — mas sai com **código 3**, para que automação que só lê o exit status não confunda "pulou" com "passou". Falta de .NET ou Node compatível é **erro**: a Camada 1 é obrigatória, e pulá-la produziria o mesmo verde falso que ela existe para impedir. `scripts/verify.ps1` roda as três de uma vez e pressupõe a estação de trabalho completa, com Docker Desktop.

> `dotnet test NotaFlow.slnx` roda a solução inteira e **inclui a Camada 3** — use-o só onde há Docker.

Validar template Angular exige `ng build` — `tsc --noEmit` e `ng lint` **não** compilam templates e passam verdes com erro de binding. Por isso `build:production` está na Camada 1, e não é opcional.

## Definição de pronto

- O fluxo alterado tem teste de caminho feliz e de erro relevante.
- Nenhum saldo fica negativo nem sofre baixa parcial.
- Retentativas reutilizam a mesma chave quando o resultado anterior é desconhecido.
- Erros externos viram `ProblemDetails` sanitizado com `traceId`.
- O fluxo manual continua operando sem OpenAI.
- README e detalhamento técnico refletem o comportamento final.
- O relatório final declara qual camada de validação rodou e qual não rodou, com o motivo.

## Risco e revisão

- **Baixo:** texto, CSS e documentação; gates automáticos bastam.
- **Médio:** CRUD, contratos e tools MCP; revisar contratos e testes.
- **Alto:** migrations, saldo, idempotência, concorrência, fechamento e **escrita da IA**; revisão humana integral.
