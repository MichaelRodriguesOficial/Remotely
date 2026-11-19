# Changelog

## v1.1.1 — 2026-11-19

### Interface
- **Uma tela só para quem não está logado:** a página inicial pública foi removida e o endereço raiz abre direto o login.
- O login ganhou a área **"Precisa de suporte agora?"** com os clientes de suporte instantâneo (Windows 64/32 bits e Linux), além de links para entrar em uma sessão, todos os downloads e **Sobre**.
- Páginas públicas (Downloads, Sobre, Solicitar suporte) usam uma barra superior simples, sem o menu lateral.
- **"Criar uma conta"** só aparece enquanto o cadastro estiver liberado (pelo padrão, antes da primeira conta). Num servidor novo, o login mostra o aviso de primeiro acesso.

### Conexão em tempo real
- **Correção: botões sem resposta na imagem Docker.** No .NET 10 o script do Blazor (`_framework/blazor.web.js`) passou a ser servido pelo manifesto de static web assets; a imagem não o entregava (404), então as páginas abriam mas nada era interativo. Agora o servidor usa `MapStaticAssets()` e a página referencia o script pela coleção de assets.
- Quando a conexão em tempo real (`/_blazor`) não sobe, aparece um aviso explicando que os botões não vão responder e o que conferir no proxy. Antes a tela parecia normal, mas nada funcionava.
- Tela de reconexão em português.
- README com configuração de Nginx/aaPanel para WebSocket e um roteiro de diagnóstico.
- Teste de fumaça verifica `/_blazor/negotiate` e o `blazor.web.js`.

## v1.1.0 — 2026-11-19

### Interface
- **Visual novo** em todo o painel, com design system próprio (temas escuro e claro), ícones Font Awesome e cabeçalho padrão nas páginas.
- **Dispositivos:** indicadores (total, online, offline, selecionados), busca e filtros numa barra só, paginação no rodapé e estado vazio com atalho para implantar.
- **Cards de dispositivo** com ícone do sistema, status, barras de CPU/memória/disco e ações rápidas (conectar, chat, somente visualizar). Aberto, o card mostra inventário, ações e edição de apelido, grupo, tags e observações lado a lado.
- **Login e cadastro** em tela própria, com a marca da organização; **Minha conta** dentro do painel com menu lateral.
- **Configuração do servidor** organizada por seções (Geral, Segurança, Acesso remoto, E-mail, Rede e registros, Dispositivos banidos, Administradores), com chaves liga/desliga, descrição de cada opção, listas editáveis e barra de salvar fixa.
- Páginas de organização, chaves de API, downloads, implantar, scripts, logs, preferências, personalização, detalhes do dispositivo, alertas, terminal, chat e viewer redesenhadas.
- **Sobre e créditos** atualizados para o fork mantido por Michael Rodrigues, com crédito ao projeto original.

### API
- **Swagger** habilitado por padrão em `/swagger`, só para usuários logados, com esquema de autenticação `X-Api-Key` e apenas os endpoints `/api`.
- Controllers registrados com `AddControllers()`, o que faltava para a documentação listar a API.

### Plataforma
- **Agents e clientes de desktop migrados para .NET 10** (PowerShell SDK 7.6, Avalonia 11.3) e compilados no CI deste repositório, em vez de virem do último release oficial (2024).
- Vulnerabilidade do `Tmds.DBus.Protocol` (dependência do Avalonia no Linux) corrigida.
- Pacotes publicam só os recursos em inglês e português.
- O indicador de agente desatualizado compara com a versão dos agents distribuídos pelo servidor.

### Release
- **Pacote do servidor muito menor**: só Linux x64, sem os agents e sem os runtimes de outras plataformas.
- Agents num pacote separado (`Remotely_Agents.zip`). O `remotely_upgrade.sh` só baixa esse pacote quando ele muda; o `remotely_install.sh` baixa os dois.
- Imagem Docker montada com os agents compilados no mesmo build.
- Teste de fumaça cobre Swagger e os arquivos dos agents.

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
