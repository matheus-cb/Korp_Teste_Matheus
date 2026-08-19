@AGENTS.md

## Claude Code

Só a mecânica desta ferramenta; as regras do projeto estão no arquivo importado
acima.

- **Preparo do ambiente:** o hook `.claude/hooks/session-start.sh` roda em sessão
  remota e instala o SDK de `global.json`, Node 24 quando o do contêiner é
  antigo, e exporta `CHROME_BIN` para o Karma. Docker continua fora — ver
  "Sandboxes de agente" no AGENTS.md.
- **Alto risco:** nos caminhos que o AGENTS.md marca como alto risco, use plan
  mode antes de editar.
