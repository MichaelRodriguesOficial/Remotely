# Changelog

## v1.0.0 — 2026-11-19

Primeira versão do fork, baseada no último código do [immense/Remotely](https://github.com/immense/Remotely) (dez/2024).

### Plataforma
- Servidor migrado para **.NET 10**, com ASP.NET Core, EF Core e Npgsql 10. Agents e desktop continuam em .NET 8.
- **Imagem Docker própria** (`michechel10/remotely`, também no GHCR):
  - build multi-stage;
  - executa sem root;
  - healthcheck;
  - instaladores dos agents incluídos.
- `docker-compose.yml` com volume nomeado e `.env.example`.
- **Releases com pacote pronto** (`Remotely_Server_Linux-x64.zip` + SHA-256), usados pelos scripts de instalação e atualização.

### Interface
- Interface traduzida para **português (pt-BR)**: páginas, telas de conta, mensagens, ajuda e viewer de controle remoto.
- Novo visual (temas escuro e claro):
  - barra lateral por seções;
  - cartões de dispositivos modernos;
  - layout responsivo;
  - viewport corrigido para celular.

### Segurança
- Filtros de API (`ApiAuthorizationFilter`/`ExpiringTokenFilter`) liberavam a requisição em caso de exceção. Agora negam o acesso.
- Páginas com `[Authorize]` agora são protegidas também na navegação interativa (`AuthorizeRouteView`).
- Bloqueio de conta após 5 tentativas, senha mínima de 8 caracteres e rate limit por IP nos endpoints de autenticação.
- Validação do ID da organização antes de gravá-lo nos instaladores `.ps1`/`.sh` (injeção de comandos via link).
- Correção de XSS armazenado:
  - saída de scripts e discos do dispositivo codificados em HTML;
  - viewer usa `textContent`.
- IDs de sessão assistida com RNG criptográfico e limite de tentativas inválidas por IP.
- A opção "Exigir login na página de controle remoto" também vale no hub do viewer.
- `AgentHub` só repassa mensagens de dispositivos registrados.
- Logs do servidor restritos a administradores do servidor.
- Verificação de organização em arquivos compartilhados e scripts salvos.
- Cabeçalhos de segurança (`nosniff`, `frame-ancestors`, `Referrer-Policy`).
- Swagger desativado em produção.
- Chaves de Data Protection persistidas em `AppData`.
- Dependências atualizadas: MailKit, MimeKit, MessagePack e System.Security.Cryptography.Pkcs. Removido o pacote de scaffolding que trazia Microsoft.Build vulnerável.
- Novas opções: `EnableSwagger`, `AllowedFrameAncestors`, `AuthRateLimitPerMinute`.

### Correções de bugs
- Upload via `/api/FileSharing` nunca salvava arquivos.
- `/api/ScriptResults/{id}` serializava a `Task` em vez do resultado.
- `GenerateResetUrl` retornava `null`.
- Usuários convidados não eram encontrados no login.
- Sessões autônomas não reconectavam após queda do screen caster.
- Download do desktop para macOS ignorava a organização.
- Handler `async void` do terminal podia derrubar o processo.
- CI usava .NET 7 com projetos .NET 8.
- Variável `DockerGatewayIp` com nome errado no compose.

### Scripts
- `remotely_install.sh`:
  - baixa o release do fork em vez de compilar no servidor;
  - instala só o ASP.NET Core Runtime 10;
  - renova o Let's Encrypt automaticamente;
  - Kestrel só em `127.0.0.1`;
  - permissões restritas em `AppData`;
  - pergunta se mantém os dados ao reinstalar.
- `remotely_upgrade.sh`:
  - atualiza pelo release (`--version`);
  - para o serviço antes do backup;
  - preserva `appsettings.json` e instaladores;
  - move o banco SQLite antigo da raiz para `AppData` (antes ele era apagado na atualização);
  - religa a versão anterior se a atualização falhar.
- `remotely_remove.sh`: não apaga nada se o backup final falhar.
