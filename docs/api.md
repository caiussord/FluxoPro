# API FluxoPro

Base local: `http://localhost:5187`. JSON em UTF-8.

## Público

| Método | Rota | Resultado |
|---|---|---|
| GET | `/health` | Saúde da API |
| GET | `/api/servicos` | Serviços ativos e preços |
| POST | `/api/solicitacoes` | Cria pedido; exige `Idempotency-Key` |

```powershell
$body = @{ nome='Maria Exemplo'; email='maria@example.com'; telefone=''; servico='Landing page'; descricao='Página demonstrativa para negócio fictício'; quantidade=2; consentimento=$true } | ConvertTo-Json
Invoke-RestMethod http://localhost:5187/api/solicitacoes -Method Post -ContentType 'application/json' -Headers @{'Idempotency-Key'='pedido-demo-001'} -Body $body
```

Pedido novo retorna `201`; mesmo corpo e chave retornam o mesmo ID com `200`; chave reutilizada com outro corpo retorna `409`. Dados inválidos retornam `400`. O cadastro público é limitado por IP a dez requisições por minuto por instância.

## Integração interna com n8n

Todas as rotas abaixo exigem `X-Internal-Key` com o valor de `FLUXOPRO_INTERNAL_KEY`. A chave deve ficar apenas na API e nas credenciais do n8n.

| Método | Rota | Resultado |
|---|---|---|
| GET | `/api/solicitacoes` | Até 100 pedidos recentes |
| GET | `/api/solicitacoes/{id}` | Dados e status |
| POST | `/api/solicitacoes/{id}/iniciar` | Reserva processamento; `409` se já iniciado |
| POST | `/api/solicitacoes/{id}/gerar-pdf` | PDF somente durante processamento |
| GET | `/api/solicitacoes/{id}/mensagens` | Assunto e corpo dos templates `cliente` e `equipe` |
| PATCH | `/api/solicitacoes/{id}/status` | Aceita `Concluída` ou `Falha` se a transição for permitida |

Para concluir, o PDF precisa ter sido gerado. Estados terminais não aceitam nova transição pelo workflow. Uma tentativa inválida retorna `409`.

## Administração

Abra `/admin`. Configure `FLUXOPRO_ADMIN_PASSWORD` com pelo menos 16 caracteres. O login cria cookie de sessão `HttpOnly` e `SameSite=Strict`, válido por oito horas. Requisições administrativas que alteram dados exigem `X-FluxoPro-Admin: 1`. O login é limitado por IP a cinco tentativas por cinco minutos por instância.

| Método | Rota | Resultado |
|---|---|---|
| POST | `/api/admin/login` | Inicia sessão com `{ "password": "..." }` |
| GET | `/api/admin/session` | Verifica sessão |
| POST | `/api/admin/logout` | Encerra sessão |
| GET | `/api/admin/overview` | Contagem por status |
| GET | `/api/admin/quotes?search=&status=&page=` | Busca paginada, 20 por página |
| GET | `/api/admin/quotes/{id}` | Pedido e histórico |
| GET | `/api/admin/quotes/{id}/pdf` | Cópia demonstrativa do PDF |
| POST | `/api/admin/quotes/{id}/retry` | Reagenda falha anterior à geração do PDF |
| GET | `/api/admin/services` | Serviços e preços |
| PUT | `/api/admin/services/{name}` | Atualiza `{ "preco": 500, "ativo": true }` |
| GET | `/api/admin/templates` | Lista templates |
| PUT | `/api/admin/templates/{key}` | Atualiza `{ "assunto": "...", "corpo": "..." }` |
| GET | `/api/admin/export.csv?search=&status=` | Exporta até 5.000 pedidos filtrados |

Os templates aceitam `{{id}}`, `{{nome}}` e `{{servico}}`. A edição só afeta os e-mails após importar a versão atualizada de `automation/fluxopro-workflow.json` no n8n.
