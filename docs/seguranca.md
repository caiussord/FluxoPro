# Segurança e limites da demonstração

O FluxoPro é um projeto autoral de portfólio. Use dados fictícios no formulário, nos prints, nos PDFs e nos testes de e-mail. A configuração local não equivale a uma implantação pronta para receber dados de clientes.

## Proteções existentes

- A API valida os campos no servidor, limita o tamanho do pedido e aplica limite de requisições ao formulário e ao login por instância.
- Uma chave de idempotência evita criar novamente o mesmo pedido quando o envio é repetido.
- Rotas internas exigem `X-Internal-Key`; o webhook do n8n usa Header Auth. Essas chaves ficam no ambiente local e não no JavaScript do navegador nem no JSON exportado.
- O painel exige uma senha administrativa no ambiente e usa cookie de sessão `HttpOnly` e `SameSite=Strict`. Alterações administrativas exigem um cabeçalho adicional.
- Mudanças de status são limitadas pelas transições permitidas e registradas no histórico. O reenvio administrativo é restrito a falhas anteriores à geração do PDF.
- Valores inseridos nos modelos de e-mail são escapados antes de compor o HTML. A exportação CSV protege contra fórmulas em campos de texto.

## Limites operacionais

Os limites de requisição são locais a cada instância. A senha administrativa é compartilhada; não há usuários individuais nem autorização por papel. Para várias instâncias, cookies administrativos exigem um chaveiro ASP.NET Data Protection compartilhado. O SQLite é adequado à demonstração local, mas uma operação multi-instância pede revisão do banco e dos backups.

O dispatcher e o endpoint `/iniciar` reduzem processamento duplicado. Eles não garantem entrega de e-mail exatamente uma vez: se o SMTP aceitar uma mensagem e a confirmação se perder, pode haver duplicidade. O workflow desativa a repetição automática dos nós de e-mail por esse motivo. Veja [a arquitetura](architecture.md) para os detalhes.

## Privacidade e distribuição

O SQLite guarda nome, e-mail e descrição até que o banco seja removido manualmente. Não há rotina automática de retenção ou exclusão. Antes de usar dados reais, defina política de privacidade, retenção, exclusão por solicitação, HTTPS, contas individuais, logs sem dados sensíveis, monitoramento e backups. Revise também a publicação, o proxy, CORS e o isolamento do n8n.

Não distribua `.env`, banco SQLite, credenciais, mensagens reais, `.git/`, `bin/` ou `obj/`. `.env.example` contém apenas valores de exemplo. O ZIP antigo citado no plano de melhorias não deve ser reutilizado para distribuição; gere um pacote novo a partir de um clone limpo ou dos arquivos versionados. O Git ignora arquivos locais, mas isso não remove conteúdo de commits antigos.
