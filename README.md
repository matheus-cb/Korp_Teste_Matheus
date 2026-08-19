# NotaFlow

Sistema demonstrativo de produtos e emissão de notas. O NotaFlow dá prioridade à consistência distribuída: o Estoque é independente do Faturamento, fechamentos são idempotentes e resultados incertos são reconciliados. Como diferencial, um Copiloto interpreta pedidos em texto ou imagem usando IA e consulta o catálogo por MCP somente leitura.

> As notas e PDFs deste projeto são demonstrativos e não possuem validade fiscal.

## Primeiros cinco minutos

Pré-requisitos: Docker Desktop com WSL2 e Git.

```powershell
.\scripts\setup.ps1
docker compose up --build
```

Abra <http://localhost:4200>. A chave `OPENAI_API_KEY` é opcional: sem ela, todo o fluxo manual funciona e o Copiloto informa que está desabilitado.

Para encerrar:

```powershell
docker compose down
```

Use `docker compose down -v` somente se desejar apagar permanentemente os bancos locais.

## Serviços

| Serviço | URL local | Responsabilidade |
|---|---:|---|
| Angular | <http://localhost:4200> | Experiência do usuário |
| Inventory API | <http://localhost:5001> | Produtos, saldo, baixa e MCP |
| Billing API | <http://localhost:5002> | Notas, fechamento, PDF e Copiloto |
| Inventory PostgreSQL | interno | Dados exclusivos do Estoque |
| Billing PostgreSQL | interno | Dados exclusivos do Faturamento |

O proxy público encaminha ao Inventory somente o catálogo de produtos. As rotas de baixa e `/mcp` ficam fora do edge e exigem o token interno aleatório criado pelo setup.

O documento OpenAPI do Billing fica disponível em `/openapi/v1.json` quando o ambiente é Development. Os serviços expõem `/health/live` e `/health/ready`; o Inventory também mantém `/health` como atalho.

## Fluxo de fechamento

1. Billing persiste uma tentativa `Pending` antes de chamar Inventory.
2. Inventory processa todos os itens em uma transação e registra a chave idempotente.
3. Billing fecha a nota ao receber ou reconciliar o resultado.
4. Se a resposta se perder, a mesma tentativa é consultada/repetida sem uma segunda baixa.
5. A ação principal **Imprimir e fechar** baixa o estoque e, depois da confirmação, baixa o PDF.
6. O PDF permanece disponível separadamente como segunda via repetível.

Para demonstrar a falha obrigatória:

```powershell
docker compose stop inventory
# Tente fechar uma nota na interface.
docker compose start inventory
# Retome/aguarde a reconciliação da mesma tentativa.
```

## Desenvolvimento sem Docker para as APIs

O SDK .NET 10 e o Node.js 24 são recomendados. Inicie bancos PostgreSQL compatíveis e ajuste as connection strings.

```powershell
dotnet restore NotaFlow.slnx
dotnet build NotaFlow.slnx
dotnet test NotaFlow.slnx

Set-Location frontend
npm ci
npm start
```

O proxy do Angular direciona `/inventory-api` a `localhost:5001` e `/billing-api` a `localhost:5002`.

## Copiloto e MCP

Billing funciona como host/orquestrador e cliente MCP. Ele descobre e chama, pelo protocolo, apenas as ferramentas `search_products`, `get_product` e `check_availability`. A Responses API recebe function tools equivalentes; o backend executa as chamadas MCP e valida deterministicamente o rascunho final.

A IA nunca possui ferramenta para criar produto, persistir nota, fechar nota ou alterar saldo. O usuário precisa aplicar e confirmar o rascunho. Texto, imagens e descrições do catálogo são tratados como dados não confiáveis.

Não envie dados pessoais ou sigilosos ao Copiloto. As requisições usam `store: false`, mas as políticas de monitoramento e retenção da API ainda se aplicam; veja o detalhamento técnico.

## Documentação

- [Detalhamento técnico](docs/technical-details.md)
- [Decisões arquiteturais](docs/architecture/decisions.md)
- [Roteiro de apresentação](docs/video-script.md)
- [Instruções para agentes](AGENTS.md)
- [Checklist de revisão](REVIEW.md)
