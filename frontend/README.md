# NotaFlow — Frontend

Aplicação Angular standalone para produtos, notas fiscais, fechamento resiliente e Copiloto MCP.

## Desenvolvimento

```bash
npm ci
npm start
```

O servidor abre em `http://localhost:4200` e usa `proxy.conf.json` para encaminhar:

- `/inventory-api` para `http://localhost:5001`;
- `/billing-api` para `http://localhost:5002`.

## Verificação

```bash
npm run lint
npm test
npm run build:production
```

## Contratos consumidos

- Estoque: `/api/products`;
- Faturamento: `/api/invoices`, `/api/invoices/{id}/close`, `/api/invoices/{id}/pdf`;
- Copiloto: `/api/invoices/ai-draft` com `multipart/form-data` (`text` e `image`).

As URLs relativas são definidas em `src/environments/`. Em Docker, o Nginx encaminha os mesmos prefixos para os contêineres `inventory:8080` e `billing:8080`.
