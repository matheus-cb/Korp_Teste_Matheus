# AGENTS.md

## Objetivo

Este repositório implementa o NotaFlow, uma solução demonstrativa de emissão de notas fiscais. A prioridade é preservar consistência do estoque e capacidade de recuperação; a IA é assistiva e nunca executa sozinha.

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
- **INV-27 — As tools MCP continuam somente leitura.** `search_products`, `get_product` e `check_availability`. Criar ou fechar nota **não** virou tool: o servidor MCP é do Inventory e nota é domínio do Billing, que é o cliente MCP — uma tool assim faria o Billing chamar a si mesmo pelo protocolo.

O que se perdeu: antes a IA não podia causar dano porque não tinha ferramenta de escrita. Agora o argumento é "confiamos na confirmação assinada e na revalidação". É mais fraco, e por isso os controles acima são obrigatórios e testados em `ProposedActionServiceTests`.

## Comandos de validação

```powershell
dotnet restore NotaFlow.slnx
dotnet build NotaFlow.slnx --no-restore
dotnet test NotaFlow.slnx --no-build
dotnet format NotaFlow.slnx --verify-no-changes
Set-Location frontend
npm ci
npm run lint
npm test
npm run build:production
```

`dotnet test` exige Docker: dois projetos de integração sobem PostgreSQL real via Testcontainers.

Validar template Angular exige `ng build` — `tsc --noEmit` e `ng lint` **não** compilam templates e passam verdes com erro de binding.

## Definição de pronto

- O fluxo alterado tem teste de caminho feliz e de erro relevante.
- Nenhum saldo fica negativo nem sofre baixa parcial.
- Retentativas reutilizam a mesma chave quando o resultado anterior é desconhecido.
- Erros externos viram `ProblemDetails` sanitizado com `traceId`.
- O fluxo manual continua operando sem OpenAI.
- README e detalhamento técnico refletem o comportamento final.

## Risco e revisão

- **Baixo:** texto, CSS e documentação; gates automáticos bastam.
- **Médio:** CRUD, contratos e tools MCP; revisar contratos e testes.
- **Alto:** migrations, saldo, idempotência, concorrência, fechamento e **escrita da IA**; revisão humana integral.
