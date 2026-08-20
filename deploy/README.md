# Implantação na VPS

Estado atual: <https://143-95-221-82.nip.io>

## Como o deploy acontece

Push em `main` → `quality-gate` → `deploy`. O segundo só roda se o primeiro
passar (`workflow_run` + job `guard`), então um teste vermelho não vira
implantação.

O build acontece no runner do GitHub, nunca na VPS: 1 vCPU / 2 GB não aguenta
`dotnet publish` mais `ng build`. As imagens vão para o GHCR com duas tags,
`latest` e o SHA do commit; o `.env` do servidor aponta para a do SHA.

A VPS não guarda credencial de registry. O job passa o `GITHUB_TOKEN` efêmero
para o `docker login` no momento do deploy e faz `logout` ao terminar.

### Rollback

```bash
ssh hg-vps1
cd /opt/notaflow
sed -i 's|:[0-9a-f]\{40\}|:<sha-anterior>|g' .env
docker compose -f docker-compose.prod.yml up -d
```

## Segredos

Vivem só em `/opt/notaflow/.env` (permissão 600), gerados no próprio servidor
por `bootstrap-vps.sh`. Não existem no Git nem no GitHub.

| Variável | Papel |
|---|---|
| `POSTGRES_*_PASSWORD` | bancos, um por serviço |
| `INTERNAL_SERVICE_TOKEN` | Billing ↔ Inventory e `/mcp` |
| `SEED_PASSWORD` | senha de `operador` e `supervisor` nesta instância |

Para ler a senha de acesso: `ssh hg-vps1 "grep SEED_PASSWORD /opt/notaflow/.env"`.

> O `SEED_PASSWORD` só tem efeito no primeiro start, quando a tabela de usuários
> está vazia — `SeedUsersAsync` retorna cedo se já houver usuário. Trocar a
> variável depois não troca a senha de quem já existe.

## Exposição

Só a 443 (e a 80, que redireciona) sai para a internet. Caddy termina TLS com
certificado Let's Encrypt; `143-95-221-82.nip.io` resolve para o IP, o que dá
HTTPS real sem domínio próprio. As APIs ficam em `127.0.0.1:5001` e `:5002`,
alcançáveis só por túnel SSH, e os bancos não publicam porta nenhuma.

`AllowedHosts` das duas APIs precisa conter o hostname público (`PUBLIC_HOST`).
A allowlist é defesa contra ataque de Host header: amplie com o host real,
não troque por `*`.

## Agente de operação

A cada 6 horas: `notaflow-collect.service` escreve o retrato do sistema em
`/var/lib/nfagent/estado.txt`, e `notaflow-agent.service` roda o Claude Code
sobre esse arquivo.

A separação é o ponto: o coletor é privilegiado, o agente não. Ele roda como
`nfagent`, fora do grupo docker — pertencer a esse grupo equivale a ser root,
porque qualquer membro monta `/` dentro de um contêiner.

Remediação é uma allowlist fechada em `nf-remediate.sh`, atrás de um `sudo`
que só autoriza esse script: reiniciar serviço sem estado, limpar imagens,
backup sob demanda. Banco de dados não está na lista.

```bash
ssh hg-vps1 "cat /var/lib/nfagent/ultimo-relatorio.txt"   # último diagnóstico
ssh hg-vps1 "cat /var/log/nfagent-acoes.log"              # o que ele já fez
ssh hg-vps1 "systemctl start notaflow-agent.service"      # rodar agora
```

Credencial em `/etc/notaflow-agent.env` (600), fora do repositório e fora da
imagem. Sem ela o agente reporta `STATUS: INDISPONIVEL` e não roda.

## Backup

`notaflow-backup.timer` faz dump dos dois bancos às 04:30, com retenção de 14
dias em `/opt/notaflow/backups`. Restaurar:

```bash
gunzip -c backups/billing-AAAAMMDD-HHMM.sql.gz \
  | docker compose -f docker-compose.prod.yml exec -T billing-db psql -U billing -d billing
```

## O que continua em aberto

- **Migration automática na subida** (`Database:MigrateOnStartup`) segue ligada.
  Conveniente aqui, arriscado com dados que importem — seção 6.3 do
  `docs/plano-agente-vps.md`.
- **Rate limit por IP** continua particionando pelo IP da conexão, que atrás do
  Caddy é sempre o mesmo. Cinco requisições de qualquer usuário travam todos —
  seção 6.2 do plano.
- **A ponte do Copiloto não existe.** Nada aqui implementa as Fases 1 a 4 do
  plano; isto é infraestrutura e operação, não integração de IA no produto.
