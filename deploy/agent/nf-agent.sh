#!/usr/bin/env bash
# Executa o agente Claude sobre o retrato coletado. Roda como nfagent, sem root.
set -uo pipefail
cd /var/lib/nfagent

if [ -z "${CLAUDE_CODE_OAUTH_TOKEN:-}${ANTHROPIC_API_KEY:-}" ]; then
    echo "STATUS: INDISPONIVEL"
    echo "RESUMO: sem credencial em /etc/notaflow-agent.env; o agente nao rodou."
    echo "ACAO: nenhuma"
    exit 0
fi

RELATORIOS=/var/lib/nfagent/relatorios
mkdir -p "$RELATORIOS"
stamp=$(date +%Y%m%d-%H%M)

# --allowedTools restringe o harness: leitura do estado e a allowlist de
# remediacao. Sem Write, sem Edit, sem Bash livre (secao 5.1 do plano).
timeout 600 "$HOME/.local/bin/claude" -p "$(cat <<'PROMPT'
Voce e o agente de operacao do NotaFlow nesta VPS.

Leia /var/lib/nfagent/estado.txt (retrato coletado agora) e produza um
diagnostico curto em portugues.

Formato exato:
STATUS: OK | ATENCAO | CRITICO
RESUMO: uma linha
ACHADOS: lista curta, so o que foge do normal. Se nada foge, escreva "nada".
ACAO: a acao tomada, ou "nenhuma"

Regras:
- Nao invente numero que nao esteja no arquivo. Se um dado faltar, diga que faltou.
- Memoria alta com swap parado nao e problema: a VPS tem 2 GB e cache conta como usada.
- So remedie sozinho nestes casos, um por execucao:
  * conteiner unhealthy ou exited (exceto banco) -> sudo /usr/local/bin/nf-remediate.sh reiniciar-servico <nome>
  * disco acima de 85% -> sudo /usr/local/bin/nf-remediate.sh limpar-imagens
- Banco de dados nunca e reiniciado por voce. Se o problema for em banco,
  marque CRITICO e nao aja.
- Se remediar, releia o estado com `cat /var/lib/nfagent/estado.txt` nao vai
  refletir a mudanca: apenas registre a acao e deixe a proxima execucao verificar.
PROMPT
)" \
  --allowedTools "Bash(cat /var/lib/nfagent/estado.txt)" \
                 "Bash(sudo /usr/local/bin/nf-remediate.sh:*)" \
  > "$RELATORIOS/$stamp.txt" 2>&1

codigo=$?
ln -sf "$RELATORIOS/$stamp.txt" /var/lib/nfagent/ultimo-relatorio.txt

# Retencao: 30 dias de relatorios bastam.
find "$RELATORIOS" -name '*.txt' -mtime +30 -delete 2>/dev/null

# O status vai para o journal, entao `journalctl -u notaflow-agent` conta a historia.
grep -E '^(STATUS|RESUMO|ACAO):' "$RELATORIOS/$stamp.txt" 2>/dev/null || {
    echo "agente nao produziu relatorio no formato esperado (codigo $codigo)"
    head -20 "$RELATORIOS/$stamp.txt"
}
exit 0
