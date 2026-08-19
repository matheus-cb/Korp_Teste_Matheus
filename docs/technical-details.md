# Detalhamento técnico

## Visão da solução

O frontend Angular consome dois microsserviços ASP.NET Core. Inventory controla produtos, saldos, movimentos e operações idempotentes. Billing controla notas, itens, tentativas de fechamento, PDF e o Copiloto. Cada serviço possui PostgreSQL próprio.

## Angular

- Componentes standalone e Angular Material organizam telas e componentes visuais.
- Reactive Forms centralizam validações de produto e itens da nota. O painel do Copiloto usa `FormsModule` para texto e um input de arquivo validado também no backend.
- `ngOnInit` inicia a leitura das páginas; `takeUntilDestroyed` encerra assinaturas com o componente.
- RxJS compõe chamadas HTTP e estados: `switchMap` troca operações, `catchError` traduz falhas e `finalize` encerra indicadores de processamento.
- Angular 22 usa modo zoneless por padrão. Como o MVP mantém estado imperativo em assinaturas RxJS, a aplicação habilita explicitamente `provideZoneChangeDetection` e estratégia `Default`; uma evolução natural é migrar esses estados para signals e voltar ao modo zoneless.

## Bibliotecas e finalidade

| Biblioteca | Finalidade |
|---|---|
| Angular, Router, Forms e RxJS | Componentes, navegação, formulários, HTTP e composição assíncrona. |
| Angular Material e CDK | Campos, botões, diálogos, feedback de carregamento e acessibilidade. |
| ASP.NET Core | REST, rate limit, health checks, validação e `ProblemDetails`. |
| Entity Framework Core e Npgsql | Persistência PostgreSQL, migrations, transações e concorrência. |
| MCP C# SDK | Servidor e cliente MCP reais sobre Streamable HTTP. |
| SkiaSharp | Decodificação por conteúdo e reencodificação segura das imagens sem metadados. |
| QuestPDF | PDF demonstrativo produzido do snapshot da nota fechada. |
| xUnit e EF InMemory | Testes unitários determinísticos das regras e orquestrações. |
| Testcontainers for .NET | Testes de integração dos dois serviços contra PostgreSQL real. |
| Coverlet | Coleta de cobertura no quality gate. |

LINQ é utilizado em filtros, paginação, projeção de DTOs, agregação de itens e composição das consultas. Dependências NuGet têm versões fixas nos projetos; npm usa `package-lock.json`, e Docker fixa as imagens principais.


## Falhas, concorrência e idempotência

Exceções esperadas viram erros de domínio e `ProblemDetails`; exceções inesperadas recebem `traceId` sem expor stack trace. Billing usa timeout e mantém a tentativa pendente quando não sabe se Inventory processou a baixa.

Inventory bloqueia/atualiza os produtos em transação única, verifica todos antes de alterar qualquer saldo e mantém uma restrição `balance >= 0`. A chave da tentativa e o hash da carga são persistidos junto da baixa. Assim, retentativas devolvem o resultado anterior e duas notas não consomem simultaneamente a última unidade.

## Inteligência artificial e MCP

O Copiloto aceita texto e, opcionalmente, JPEG/PNG/WebP. A OpenAI Responses API interpreta o pedido; function calls são traduzidos para chamadas das ferramentas MCP descobertas em Inventory. O resultado final segue schema estruturado e passa por validação determinística.

As ferramentas MCP são somente leitura — `search_products`, `get_product` e `check_availability`. Criar ou fechar nota não é tool: o servidor MCP é do Inventory e nota é domínio do Billing, que é o cliente MCP; uma tool dessas faria o Billing chamar a si mesmo pelo protocolo.

A escrita assistida existe por **ação proposta** (`ProposedActionService`): a IA devolve a ação, o servidor a assina em HMAC com prazo de validade, e a execução só ocorre após confirmação humana, revalidando existência de produto e saldo no momento de confirmar. A proposta é sugestão, não autorização. A chave fica no backend, imagens não são persistidas, logs são sanitizados e o sistema manual opera sem a integração.

Cada chamada à Responses API usa `store: false` e o Billing reenvia explicitamente o histórico necessário ao loop de ferramentas. Isso evita a persistência de estado da resposta para continuidade da aplicação, mas não deve ser interpretado como retenção zero por si só: por padrão, o provedor pode manter logs de monitoramento de abuso por até 30 dias, e entradas de imagem passam por verificações de segurança. Por isso, a interface orienta o uso de pedidos operacionais sem dados pessoais ou sigilosos. Consulte os [controles de dados oficiais da OpenAI](https://developers.openai.com/api/docs/guides/your-data).

O endpoint do Copiloto aceita somente upload multipart local; URLs remotas não são buscadas. JPEG, PNG e WebP são decodificados por conteúdo real, limitados a 5 MB e 12 megapixels, reencodados sem metadados e descartados após a requisição.

## Qualidade

CI executa restore, format check, analisadores, build, testes, lint e build Angular. Testes críticos cobrem baixa integral, saldo insuficiente, concorrência, idempotência, resposta perdida, fechamento único, falha externa e validação do rascunho. Mutation testing é reservado para saldo, status e idempotência.
