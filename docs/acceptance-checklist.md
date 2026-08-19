# Checklist de aceite

## Ambiente limpo

- [x] `.env.example` pode ser copiado sem conter segredo real.
- [x] `docker compose config --quiet` passa.
- [ ] `docker compose up --build` deixa os três serviços saudáveis.
- [x] A aplicação manual permanece independente de `OPENAI_API_KEY`.

## Fluxo obrigatório

- [x] Produto com código, descrição e saldo é persistido.
- [x] Nota recebe número automático e status Aberta.
- [x] Múltiplos produtos e quantidades podem ser incluídos.
- [x] Fechamento exibe processamento e altera o status.
- [x] Saldos são reduzidos corretamente.
- [x] Nota fechada não causa nova baixa.
- [x] Saldo insuficiente não causa baixa parcial.

## Falha e recuperação

- [x] Estoque indisponível produz feedback compreensível.
- [x] Resultado desconhecido permanece pendente.
- [x] Retomar usa a mesma chave idempotente.
- [x] Religando o serviço, a operação converge sem baixa dupla.

## Diferenciais

- [x] Duas notas disputando uma unidade nunca geram saldo negativo.
- [x] PDF contém snapshot e aviso de documento demonstrativo.
- [x] Copiloto por texto pesquisa via MCP e gera rascunho revisável.
- [x] Copiloto por imagem valida arquivo e usa o mesmo contrato.
- [x] IA/MCP indisponível não interfere no fluxo manual.

## Entrega

- [ ] CI verde.
- [ ] README testado em clone limpo.
- [x] Detalhamento técnico cobre todos os itens do enunciado.
- [ ] Vídeo segue o roteiro e mostra primeiro os requisitos obrigatórios.
- [ ] Repositório público possui o nome `Korp_Teste_SeuNome`.
