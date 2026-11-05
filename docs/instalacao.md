# Instalação e testes locais

Este guia usa dados fictícios. O formulário e o painel funcionam sem n8n; o envio de e-mails exige o workflow e uma credencial SMTP configurados.

## Requisitos

- SDK .NET 10.
- Docker Desktop para executar n8n e Mailpit.

## Iniciar somente a aplicação

Na raiz do repositório, em PowerShell:

```powershell
dotnet restore FluxoPro.slnx
$env:FLUXOPRO_INTERNAL_KEY = 'exemplo-local-troque-esta-chave'
$env:FLUXOPRO_ADMIN_PASSWORD = 'exemplo-local-senha-com-16-caracteres'
$env:FLUXOPRO_N8N_WEBHOOK_URL = ''
dotnet run --project src/FluxoPro.Api --urls http://127.0.0.1:5187
```

Abra `http://127.0.0.1:5187` para o formulário e `http://127.0.0.1:5187/admin` para o painel. Nesse modo, pedidos aparecem no painel, mas não geram e-mail. A API cria o SQLite no diretório da aplicação; defina `FLUXOPRO_DB_PATH` para usar outro local. Faça backup antes de atualizar um banco cujos dados queira conservar.

Os serviços iniciais têm preços ilustrativos por unidade: Site institucional (R$ 800), Landing page (R$ 500) e Automação simples (R$ 350). O painel pode alterar os preços dos pedidos futuros; pedidos antigos preservam o valor registrado.

## Configurar n8n e Mailpit

1. Copie `.env.example` para `.env` e substitua os exemplos por chaves locais. O arquivo `.env` é ignorado pelo Git. O .NET não o lê automaticamente; `start-api.ps1` carrega seus valores no processo.
2. Se a API roda no Windows e o n8n no Docker, configure `FLUXOPRO_API_URL=http://host.docker.internal:5187` no `.env`.
3. Execute `docker compose up -d`. O n8n abre em `http://localhost:5678` e o Mailpit em `http://localhost:8026`.
4. Importe [o workflow versionado](../automation/fluxopro-workflow.json) no n8n. No nó **Receber solicitação**, configure Header Auth com `X-Webhook-Key` e o valor de `FLUXOPRO_N8N_WEBHOOK_KEY`. Os nós HTTP usam `X-Internal-Key` com o valor de `FLUXOPRO_INTERNAL_KEY`.
5. Nos nós **Enviar confirmação** e **Avisar equipe**, associe uma credencial SMTP de teste: host `mailpit`, porta `1025`, SSL/TLS e STARTTLS desligados. Se o n8n pedir usuário e senha, use valores fictícios; o Mailpit desta configuração aceita autenticação de teste. O export do workflow não contém credenciais.
6. Publique o workflow e coloque sua URL de produção em `FLUXOPRO_N8N_WEBHOOK_URL`. Inicie a API com `./start-api.ps1`. Envie um pedido fictício e acompanhe a execução no n8n, o status no painel e os e-mails no Mailpit.

O Mailpit captura as mensagens localmente. Elas não chegam a caixas externas. A confirmação contém PDF e versões HTML e texto; o aviso à equipe não tem anexo. Para usar outro servidor SMTP, configure uma credencial própria no n8n e use apenas destinatários autorizados para o teste.

Para parar os contêineres sem apagar seus volumes, execute `docker compose stop`. Para voltar, execute `docker compose up -d`.

### Configuração local da porta 5190

`start-api.ps1` aceita `-Port`, `-DatabasePath` e `-WebhookUrl`. Nesta máquina de desenvolvimento, há workflows adicionais guardados **somente no volume local do n8n** para testar a API na porta 5190:

```powershell
./start-api.ps1 -Port 5190 -DatabasePath "$env:TEMP\fluxopro-portfolio-demo.db" -WebhookUrl 'http://localhost:5678/webhook/fluxopro-mailpit-preview'
```

Esse comando envia para o Mailpit. Para testar o SMTP externo já configurado nesta máquina, pare a API e substitua o webhook por `http://localhost:5678/webhook/fluxopro-gmail-5190`. Nesse caso, o e-mail vai para o endereço informado no formulário e não aparece no Mailpit. Um clone novo não contém esses workflows locais: importe o JSON versionado e configure o seu próprio ambiente.

## Executar os testes

```powershell
dotnet restore FluxoPro.slnx
dotnet build FluxoPro.slnx --no-restore --configuration Release
dotnet test FluxoPro.slnx --no-build --configuration Release
```

O projeto inclui testes para validação, idempotência, autenticação, transições de status, histórico, reserva concorrente, falhas de webhook, preços históricos, exportação CSV, PDF e composição de e-mail. O [workflow de CI](../.github/workflows/ci.yml) também verifica JavaScript e o JSON do n8n. A execução no GitHub Actions só pode ser confirmada após publicar esta versão.
