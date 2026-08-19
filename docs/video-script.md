# Roteiro do vídeo

## 1. Contexto e arquitetura

- Apresentar Angular, Inventory, Billing e os dois bancos.
- Explicar que não há acesso cruzado entre bancos.
- Mostrar Swagger/health e o Docker Compose.

## 2. Fluxo funcional

- Cadastrar três produtos.
- Criar uma nota com múltiplos itens.
- Mostrar número automático e status Aberta.
- Fechar a nota, acompanhar processamento e conferir os novos saldos.
- Baixar o PDF demonstrativo e tentar fechar novamente.

## 3. Falha e recuperação

- Parar Inventory.
- Solicitar fechamento e mostrar feedback apropriado/pending.
- Iniciar Inventory e mostrar a reconciliação sem desconto duplicado.

## 4. Concorrência e idempotência

- Explicar o cenário da última unidade.
- Executar ou mostrar o teste em que somente uma nota vence.
- Repetir a mesma chave e provar que o saldo muda uma única vez.

## 5. IA e MCP

- Digitar um pedido com sinônimo e produto inexistente.
- Mostrar as ferramentas MCP chamadas e as pendências.
- Aplicar o rascunho, revisar e criar manualmente.
- Enviar uma imagem de pedido, se a extensão multimodal estiver habilitada.
- Explicar que MCP é read-only e REST continua responsável pela escrita.

## 6. Detalhamento técnico

- Citar `ngOnInit`, Reactive Forms, RxJS e Angular Material.
- Mostrar ASP.NET Core, EF Core, migrations, LINQ e `ProblemDetails`.
- Explicar saga, transação local, idempotência, logs e testes.
- Encerrar com CI verde, README e limitações conhecidas.

