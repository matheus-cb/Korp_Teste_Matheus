#!/usr/bin/env bash
# Gate da convenção dos arquivos de contexto de agente.
#
# O AGENTS.md exige que cada regra tenha um teste apontável. A regra "AGENTS.md
# é canônico, CLAUDE.md só importa" não tinha nenhum — e foi violada na primeira
# vez em que foi escrita. Este script é o teste dela.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

falhas=0
falhar() { echo "FALHA: $1" >&2; falhas=$((falhas + 1)); }

# 1. A ponte existe e é a primeira coisa do arquivo. Sem isto o Claude Code não
#    enxerga nenhuma regra do projeto — foi o defeito original.
primeira="$(grep -m1 -v '^[[:space:]]*$' CLAUDE.md || true)"
[ "$primeira" = "@AGENTS.md" ] || falhar "CLAUDE.md deve começar com o import '@AGENTS.md'; começa com '$primeira'."

# 2. O Codex para de ler ao atingir project_doc_max_bytes (32 KiB por padrão).
#    Passar disso trunca em silêncio, e o corte cai no meio das invariantes.
bytes="$(wc -c <AGENTS.md)"
[ "$bytes" -lt 32768 ] || falhar "AGENTS.md tem $bytes bytes; o Codex corta em 32768."

# 3. Regra de projeto no CLAUDE.md é regra que o Codex nunca vê.
if grep -qE 'INV-[0-9]|^\| *\*\*INV' CLAUDE.md; then
  falhar "CLAUDE.md cita invariante (INV-nn). Invariante é regra de projeto: vai no AGENTS.md."
fi

# 4. O CLAUDE.md é uma ponte, não um segundo manual. Se cresceu, quase sempre é
#    porque conteúdo compartilhado foi parar no arquivo específico do Claude.
linhas_claude="$(wc -l <CLAUDE.md)"
[ "$linhas_claude" -le 40 ] || falhar "CLAUDE.md tem $linhas_claude linhas (máx. 40). Mova o que não for exclusivo do Claude Code para o AGENTS.md."

# 5. Adesão inversa é pior: se o combinado passa de 200 linhas, o Claude perde
#    aderência às instruções — o problema deixa de ser de organização.
total=$((linhas_claude + $(wc -l <AGENTS.md)))
[ "$total" -le 200 ] || falhar "AGENTS.md + CLAUDE.md somam $total linhas; acima de 200 a aderência cai."

# 6. Import quebrado carrega nada e não avisa. Vale para os dois arquivos.
for arquivo in AGENTS.md CLAUDE.md; do
  while read -r alvo; do
    [ -n "$alvo" ] || continue
    [ -e "$alvo" ] || falhar "$arquivo importa '@$alvo', que não existe."
  done < <(grep -oE '(^|[^`[:alnum:]])@[A-Za-z0-9._/-]+' "$arquivo" | sed 's/.*@//')
done

if [ "$falhas" -gt 0 ]; then
  echo "" >&2
  echo "$falhas verificação(ões) falharam. Ver AGENTS.md → 'Objetivo'." >&2
  exit 1
fi

echo "Convenção dos arquivos de contexto: ok ($bytes bytes no AGENTS.md, $total linhas no total)."
