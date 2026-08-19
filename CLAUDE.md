@AGENTS.md

## Claude Code

O arquivo importado acima é a **fonte canônica** deste repositório e vale para
qualquer agente. Regra nova, invariante nova ou comando novo vai lá, nunca aqui
— este arquivo existe só para o que é específico do Claude Code. Codex lê
`AGENTS.md` nativamente; o Claude Code lê `CLAUDE.md`, e o import acima faz os
dois convergirem no mesmo texto.

### Ambiente das sessões remotas

Sessões do Claude Code na web sobem **sem .NET SDK, com um Node anterior ao
mínimo do Angular CLI e sem daemon Docker**. O hook `SessionStart`
(`.claude/hooks/session-start.sh`) resolve os dois primeiros: instala o SDK da
versão fixada em `global.json`, instala Node 24 quando o do contêiner é antigo,
restaura as dependências e exporta `CHROME_BIN`.

Docker não tem conserto por hook: a **Camada 3** de validação não roda em sessão
remota. Não tente subir Testcontainers nem `docker compose` aqui — declare no
relatório final que ela ficou por executar.

### Alteração de alto risco

O AGENTS.md classifica migrations, saldo, idempotência, concorrência,
fechamento e escrita da IA como alto risco. Nesses caminhos, apresente o plano
antes de editar: são exatamente os pontos onde um erro só aparece sob
concorrência ou falha de rede, quando o teste feliz já passou.
