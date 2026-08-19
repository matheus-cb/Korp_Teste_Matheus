# Decisões arquiteturais

## Dois serviços e dois bancos

Inventory e Billing possuem processos, credenciais e bancos distintos. Compartilhar o mesmo servidor PostgreSQL seria aceitável, mas compartilhar tabelas não é. A comunicação ocorre somente por contratos de rede.

## REST para comandos críticos; MCP para contexto

REST é explícito, fácil de observar e adequado à baixa de estoque. MCP é usado para descoberta e consulta de ferramentas pelo Copiloto. Limitar MCP a leitura impede que uma interpretação probabilística cause efeitos de negócio.

## Saga idempotente em vez de transação distribuída

Não existe uma transação ACID envolvendo os bancos. Billing registra uma tentativa durável; Inventory registra a chave e a baixa na mesma transação local; a reconciliação repete a mesma operação até descobrir o resultado. Isso oferece convergência sem desconto duplicado.

## Nota e itens imutáveis

A nota armazena o snapshot de código e descrição. A imutabilidade estabiliza o hash usado na idempotência e preserva o documento mesmo que o catálogo evolua.

## Sequência com lacunas permitidas

PostgreSQL gera números únicos e crescentes sob concorrência. Rollbacks podem deixar lacunas, comportamento assumido e documentado para evitar contenção artificial.

## IA assistiva e confirmação humana

A IA transforma dados não estruturados em um rascunho. IDs precisam ter sido observados nas ferramentas da mesma execução e todas as regras são revalidadas. O formulário manual é sempre o fallback.

