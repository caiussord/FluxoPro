# API

Base local: `http://localhost:5187`. JSON em UTF-8.

| Método | Rota | Acesso | Resultado |
|---|---|---|---|
| GET | `/health` | público | saúde da API |
| GET | `/api/servicos` | público | preços demonstrativos |
| POST | `/api/solicitacoes` | público | cria solicitação; `Idempotency-Key` obrigatório |
| GET | `/api/solicitacoes` | interno | até 100 pedidos recentes |
| GET | `/api/solicitacoes/{id}` | interno | dados e status |
| POST | `/api/solicitacoes/{id}/iniciar` | interno | reserva processamento uma vez; `409` se já iniciado |
| POST | `/api/solicitacoes/{id}/gerar-pdf` | interno | PDF demonstrativo |
| PATCH | `/api/solicitacoes/{id}/status` | interno | `Em processamento`, `Concluída` ou `Falha` |

Exemplo:

```powershell
$body = @{ nome='Maria Exemplo'; email='maria@example.com'; telefone=''; servico='Landing page'; descricao='Página demonstrativa para negócio fictício'; quantidade=2; consentimento=$true } | ConvertTo-Json
Invoke-RestMethod http://localhost:5187/api/solicitacoes -Method Post -ContentType 'application/json' -Headers @{'Idempotency-Key'='pedido-demo-001'} -Body $body
```

O mesmo corpo e chave retornam o mesmo ID (`200`); chave reutilizada com outro corpo retorna `409`. Uma criação nova retorna `201`. Erros de validação retornam `400`. Endpoints internos exigem `X-Internal-Key` igual a `FLUXOPRO_INTERNAL_KEY`.
