#!/usr/bin/env bash
# Par POSIX de scripts/setup.ps1: cria o .env e gera o token interno aleatório.
# Idempotente — um token já configurado nunca é substituído.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo/.env"

[ -f "$env_file" ] || cp "$repo/.env.example" "$env_file"

configured="$(sed -n 's/^INTERNAL_SERVICE_TOKEN=\(.*\)$/\1/p' "$env_file" | head -1 | tr -d '[:space:]')"
if [ -n "$configured" ]; then
  echo "Arquivo .env já contém um token interno; nenhum segredo foi alterado."
  exit 0
fi

token="$(head -c 32 /dev/urandom | base64 | tr '+/' '-_' | tr -d '=\n')"
if grep -q '^INTERNAL_SERVICE_TOKEN=' "$env_file"; then
  # Template explícito: `mktemp` sem argumento não é portátil no macOS/BSD.
  tmp="$(mktemp "${TMPDIR:-/tmp}/notaflow-env.XXXXXX")"
  sed "s|^INTERNAL_SERVICE_TOKEN=.*$|INTERNAL_SERVICE_TOKEN=$token|" "$env_file" > "$tmp"
  mv "$tmp" "$env_file"
else
  printf '\nINTERNAL_SERVICE_TOKEN=%s\n' "$token" >> "$env_file"
fi

echo "Arquivo .env criado e token interno aleatório gerado."
