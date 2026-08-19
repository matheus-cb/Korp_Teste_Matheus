# Checklist de revisão

## Regras de domínio

- [x] A nota nasce `Open` e torna-se `Closed` uma única vez.
- [x] O snapshot do item permanece estável.
- [x] Um item inválido impede a baixa de todos os itens.
- [x] O banco impede saldo negativo.
- [x] Repetir a mesma chave e carga não repete o efeito.
- [x] Reutilizar a mesma chave com carga diferente retorna conflito.
- [x] Fechamentos simultâneos da mesma nota compartilham a tentativa ativa.
- [x] Resultado desconhecido fica pendente e pode ser reconciliado.

## Arquitetura e segurança

- [x] Nenhum serviço acessa o banco do outro.
- [x] REST continua sendo o caminho de escrita entre serviços.
- [x] MCP não oferece ferramentas de escrita.
- [x] IDs e quantidades produzidos pela IA são revalidados.
- [x] Upload valida tipo real, tamanho e dimensões.
- [x] Nenhum segredo ou conteúdo sensível aparece em código ou logs.

## Qualidade

- [x] Build, lint e testes executáveis localmente estão verdes.
- [x] Existe teste de regressão para a mudança.
- [x] Alterações de alto risco possuem teste de integração com PostgreSQL preparado para CI.
- [x] A documentação explica qualquer decisão nova ou limitação.
