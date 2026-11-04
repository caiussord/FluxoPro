# FluxoPro — Automação de Orçamentos

Um formulário recebe pedidos fictícios, a API .NET valida e salva em SQLite, gera um PDF e entrega um evento ao n8n para envio de e-mails. Não é um produto comercial.

## Executar a API e o formulário

Requisitos: SDK .NET 10. Opcionalmente, Docker Desktop para o n8n.

```powershell
dotnet restore FluxoPro.slnx
$env:FLUXOPRO_INTERNAL_KEY = 'troque-por-um-segredo-longo'
dotnet run --project src/FluxoPro.Api --urls http://localhost:5187
```

Depois de configurar o `.env`, você também pode iniciar a API com `./start-api.ps1`. Esse script carrega o `.env` apenas para o processo e abre a API na porta 5187 para acesso do n8n no Docker.

Abra `http://localhost:5187`. O banco é criado em `src/FluxoPro.Api/bin/Debug/net10.0/data/fluxopro.db` por padrão. Defina `FLUXOPRO_DB_PATH` para usar outro local. Os preços de exemplo são R$ 800 para site institucional, R$ 500 para landing page e R$ 350 para automação simples, multiplicados pela quantidade. Podem ser alterados via configuração `Prices:Site`, `Prices:LandingPage` e `Prices:Automation`.

## Ligar o n8n

1. Copie `.env.example` para `.env` e troque os segredos e endereços. Use `FLUXOPRO_API_URL=http://host.docker.internal:5187` quando a API roda no host Windows e o n8n no Docker.
2. Carregue as variáveis `FLUXOPRO_INTERNAL_KEY`, `FLUXOPRO_N8N_WEBHOOK_URL` e `FLUXOPRO_N8N_WEBHOOK_KEY` no processo da API. O .NET não lê `.env` automaticamente. Em PowerShell, use `$env:NOME='valor'` antes de `dotnet run`.
3. Execute `docker compose up -d`, abra `http://localhost:5678` e importe [automation/fluxopro-workflow.json](automation/fluxopro-workflow.json). O Compose guarda os dados do n8n no volume `projeto2_n8n_data`. Para parar use `docker compose stop`; para voltar use `docker compose up -d`.
4. No nó **Receber solicitação**, crie uma credencial **Header Auth**: cabeçalho `X-Webhook-Key` com o valor de `FLUXOPRO_N8N_WEBHOOK_KEY`. O export não contém credenciais.
5. O Compose também inicia o **Mailpit**. Nos nós **Enviar confirmação** e **Avisar equipe**, use a mesma credencial SMTP de teste: host `mailpit`, porta `1025`, **SSL/TLS desligado**, **Disable STARTTLS ligado**, usuário e senha fictícios se a interface exigir. O Mailpit desta configuração aceita autenticação de teste. Consulte as mensagens em `http://localhost:8026`. Ajuste remetente e destinatário no `.env`. Ative o workflow e copie a URL de produção do webhook para `FLUXOPRO_N8N_WEBHOOK_URL` da API.

Como alternativa já testada, use uma credencial SMTP do Gmail nos dois nós de e-mail: host `smtp.gmail.com`, porta `465`, SSL/TLS ligado e senha de aplicativo salva **somente no gerenciador de credenciais do n8n**. Ajuste `FLUXOPRO_FROM_EMAIL` e `FLUXOPRO_TEAM_EMAIL` no `.env` local e reinicie o n8n com `docker compose up -d n8n` para aplicar as mudanças.

O dispatcher da API tenta entregar o evento até cinco vezes, com intervalo de dois minutos. O n8n chama `/iniciar` de forma atômica para impedir execução duplicada. Cada nó externo tenta até três vezes; se houver erro, o fluxo registra `Falha`. Uma execução que permaneça `Em processamento` por mais de 30 minutos também passa a `Falha`.

**Limite atual:** se um servidor SMTP aceitar a mensagem e a conexão cair antes da confirmação, uma nova tentativa do nó pode duplicar o e-mail. Use apenas destinatários de teste. O fluxo completo de n8n/SMTP requer configuração e validação no ambiente onde será apresentado.

## API

Veja [docs/api.md](docs/api.md). Os endpoints internos exigem o cabeçalho `X-Internal-Key`; não exponha essa chave no navegador. A interface pública só recebe o protocolo e o status inicial.

Um [PDF de exemplo com dados fictícios](docs/exemplo-orcamento.pdf) mostra o documento gerado pela API.

## Privacidade e limites

Use somente dados fictícios. O SQLite local guarda nome, e-mail e descrição até exclusão manual do arquivo do banco; a aplicação não implementa rotina automática de retenção. Não publique o banco, `.env`, mensagens de e-mail nem dados reais. O formulário mostra um aviso de consentimento para uso demonstrativo. Antes de uma operação real, seriam necessárias política de privacidade, retenção definida, exclusão por solicitação, HTTPS e revisão de segurança.

## Estado do projeto

API, formulário, SQLite, PDF e workflow exportado estão implementados. A compilação, geração local de PDF e execução ponta a ponta com n8n e SMTP foram verificadas em 04/11/2026 com dados fictícios. O operador confirmou o recebimento dos dois e-mails e do PDF anexado.
