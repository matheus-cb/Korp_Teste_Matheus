# Detalhamento técnico

## Visão da solução

O NotaFlow é uma aplicação Angular para cadastro de produtos, criação de notas e fechamento com baixa consistente de estoque. O frontend conversa com dois microsserviços ASP.NET Core:

- **Inventory** é o único responsável por produtos, saldos, movimentos e operações de baixa idempotentes.
- **Billing** é o único responsável por notas, itens, tentativas de fechamento, PDF e o assistente.

Cada serviço possui seu próprio PostgreSQL e suas próprias migrations. O Billing não acessa o banco do Inventory, nem o Inventory acessa o banco do Billing. A baixa de estoque é um comando REST interno, com transação local e `Idempotency-Key`; MCP é usado somente para consultas de contexto pelo assistente.

O Inventory também guarda uma trilha imutável por produto: um evento de **criação** e outro a cada **edição de metadados**, sempre com usuário e data/hora. Produtos já existentes recebem o evento de criação durante a migration; não são inventadas edições históricas que não existiam no banco. A tabela principal de Produtos apresenta criação e alteração em quatro campos separados: usuário e data/hora de criação; usuário e data/hora da última alteração. O modal fica restrito à edição. Saldo não é alterado nesse fluxo.

## Angular: ciclo de vida e organização

O frontend usa Angular 22 com componentes standalone, Router, Reactive Forms e injeção por `inject()`.

Foram usados dois `ngOnInit`:

- em `create-invoice.dialog.ts`, para receber o rascunho transferido pelo assistente e carregar o catálogo antes de a pessoa editar a nota;
- em `invoice-detail.dialog.ts`, para carregar a nota quando o diálogo de detalhes é aberto.

O projeto não adiciona hooks sem necessidade. A maior parte dos componentes inicializa suas dependências no construtor com `inject()` e encerra assinaturas com `takeUntilDestroyed(this.destroyRef)`. Essa abordagem substitui o padrão manual de `ngOnDestroy` com `Subject`, reduzindo risco de assinaturas esquecidas.

A aplicação mantém `provideZoneChangeDetection` com estratégia `Default`, pois alguns estados são atualizados por assinaturas RxJS imperativas. Uma evolução futura seria migrar esses estados para signals e reavaliar o modo zoneless.

## RxJS: onde e por que é usado

RxJS é usado para coordenar eventos da interface e requisições HTTP.

- `takeUntilDestroyed` remove assinaturas automaticamente quando o componente é destruído.
- `debounceTime(250)` e `distinctUntilChanged` evitam busca de catálogo a cada tecla digitada.
- `switchMap` cancela a busca anterior quando surge um termo novo, evitando que uma resposta antiga substitua uma pesquisa mais recente.
- `merge` combina recarga explícita da tela com a busca, sem descartar a atualização manual.
- `timer(0, 2000)`, `filter`, `take` e `timeout` fazem o polling de reconciliação do fechamento quando a resposta inicial é pendente. Isso é parte direta do tratamento de falhas entre Billing e Inventory.
- `forkJoin` carrega dados independentes do dashboard em paralelo.
- `catchError` traduz falhas para mensagens tratáveis pela interface e `finalize` encerra indicadores de carregamento mesmo quando a requisição falha.

## Bibliotecas e finalidade

| Biblioteca ou tecnologia | Finalidade |
|---|---|
| Angular, Router, Forms e RxJS | Componentes, navegação, formulários, HTTP e fluxos assíncronos. |
| Angular Material e CDK | Botões, campos, ícones, diálogos, spinner e recursos de acessibilidade. |
| AG Grid Community e AG Grid Angular | Tabelas de produtos, notas e movimentos, com paginação, formatação e linha de total. |
| ASP.NET Core 10 | APIs REST, autenticação, rate limit, health checks, validação e `ProblemDetails`. |
| Entity Framework Core 10 e Npgsql | Persistência PostgreSQL, migrations, transações, consultas e concorrência. |
| Model Context Protocol C# SDK | Servidor MCP no Inventory e cliente MCP no Billing, sobre Streamable HTTP. |
| SkiaSharp | Decodificação por conteúdo e reencodificação segura de imagens sem metadados. |
| QuestPDF | PDF demonstrativo produzido do snapshot da nota fechada. |
| AspNetCore.HealthChecks.NpgSql | Health check de prontidão que verifica a conexão real com PostgreSQL. |
| xUnit, EF InMemory e Testcontainers for .NET | Testes unitários determinísticos e testes de integração contra PostgreSQL real. |
| Coverlet | Coleta de cobertura no quality gate. |

O backend foi implementado em C#, portanto gerenciamento de dependências em Go não se aplica. As dependências .NET são declaradas nos arquivos de projeto, npm usa `package-lock.json` e as imagens principais do Docker são fixadas na configuração de deploy.

## Frameworks C# e APIs

O backend usa ASP.NET Core 10. O Billing adota Minimal APIs para seus endpoints de autenticação, notas, assistente, confirmação de ações propostas e health checks. O Inventory usa Controllers para os endpoints REST de produtos, baixas, movimentos e reconciliação. Os dois serviços usam injeção de dependência nativa do ASP.NET Core e EF Core 10 com o provider Npgsql para PostgreSQL.

## Tratamento de erros e exceções

O Billing possui uma hierarquia de exceções de domínio:

```text
AppException
|- DomainValidationException
|- ResourceNotFoundException
|- ConflictException
`- DependencyUnavailableException
```

`ExceptionHandlingMiddleware` converte essas exceções em `ProblemDetails`, no padrão RFC 7807, com código estável e `traceId`. A interface converte os códigos em mensagens em português.

Dois cuidados são importantes:

- `DbUpdateConcurrencyException` vira conflito HTTP 409 com retorno acionável.
- Exceções inesperadas não expõem stack trace, SQL, endereço interno ou segredo ao navegador; o `traceId` permite correlacionar a falha nos logs.

Quando Billing não consegue saber se o Inventory concluiu uma baixa, ele não assume sucesso. Mantém a tentativa como pendente e reconcilia usando a mesma chave idempotente.

## LINQ

LINQ é usado tanto em consultas convertidas para SQL quanto em regras executadas em memória.

- Em `ProductCatalog`, `AssistantTools`, endpoints de notas e leitores de movimentos, `IQueryable`, `AsNoTracking`, `Where`, `Select`, `Skip` e `Take` filtram, projetam e paginam no PostgreSQL antes da materialização.
- Em regras de domínio, `GroupBy` e `Sum` agregam itens repetidos de uma nota e quantidades usadas como evidência do assistente.
- Na reconciliação de estoque, agrupamentos verificam se o saldo armazenado corresponde ao extrato append-only de movimentos.

Assim, o sistema evita carregar coleções inteiras sem necessidade e mantém as regras de agregação explícitas.

## Consistência, concorrência, idempotência e falhas

O fechamento cria uma tentativa persistida antes de chamar o Inventory. A baixa usa transação local, lock consultivo, bloqueio de linha e valida todos os itens antes de alterar qualquer saldo. Há constraint de banco que impede saldo negativo.

Se duas notas disputam a última unidade, apenas uma consegue concluir. Se uma resposta se perde, Billing consulta ou repete a mesma operação usando a mesma `Idempotency-Key`. A operação gravada contém a chave e o hash da carga: mesma chave e mesma carga devolvem o resultado anterior; mesma chave com carga diferente gera conflito. Nenhuma nota produz baixa parcial.

O PDF só é gerado quando a nota está fechada. A ação principal da interface é **Imprimir e fechar**: ela trabalha sobre a nota aberta, mostra processamento, confirma a baixa e fecha a nota. Depois disso, o PDF é uma segunda via e não gera nova baixa.

## Assistente, IA e MCP

O assistente aceita texto e, quando o provedor suporta, imagem JPEG, PNG ou WebP. URLs remotas não são buscadas. A imagem é validada pelo conteúdo real, limitada a 5 MB e 12 megapixels, reencodada sem metadados e descartada após a requisição.

O Inventory expõe ferramentas MCP somente leitura: `search_products`, `get_product`, `check_availability`, `list_products` e `list_movements`. O Billing mantém consultas locais de notas. Essas consultas dão contexto ao modelo, mas não concedem escrita por MCP.

O assistente pode sugerir cadastro de produto ou criação de nota por meio de uma **ação proposta**. A proposta é assinada em HMAC, tem prazo de validade e aparece para revisão. Somente depois de confirmação humana o endpoint do servidor executa a ação e revalida produto, saldo e demais regras. O assistente não pode fechar nota: uma nota proposta sempre nasce aberta e o fechamento continua sendo uma ação humana no fluxo normal.

O Billing pode usar OpenAI Responses API ou uma ponte local para Claude Code, conforme a configuração do ambiente. Em ambos os casos, a proveniência de produtos é derivada dos resultados das ferramentas, não de uma alegação do modelo. Com OpenAI, as chamadas usam `store: false`; isso reduz o estado mantido pela API para a aplicação, mas não deve ser interpretado como retenção zero do provedor. Logs não guardam token, imagem, prompt integral ou raciocínio interno.

Quando a ponte local já está ocupada, ela responde `429` com uma indicação de nova tentativa. O Billing converte esse caso em `503` com o código `AI_BUSY`, e a interface orienta a pessoa a aguardar alguns segundos, em vez de exibir um `INTERNAL_ERROR`. Indisponibilidade de rede ou do provedor segue o mesmo contrato sanitizado com `AI_UNAVAILABLE`. A unidade do agente aguarda a rede Docker antes de iniciar, evitando a corrida de boot que poderia deixar a ponte sem escutar até um reinício.

## Qualidade e entrega contínua

O quality gate executa verificação das regras de agente, restore, analisadores, format check, build, testes .NET, lint e build Angular. Em ambiente com Docker, os testes de integração usam PostgreSQL real e o pipeline também valida a stack publicada por Compose.

Os testes críticos cobrem baixa integral, saldo insuficiente, concorrência, idempotência, resposta perdida, fechamento único, falha externa, ações propostas, trilha de criação/edição de produtos e validação de evidência do assistente. Mutation testing permanece reservado para regras de saldo, status e idempotência.

Para a demonstração, o fluxo manual permanece utilizável mesmo sem provedor de IA configurado.
