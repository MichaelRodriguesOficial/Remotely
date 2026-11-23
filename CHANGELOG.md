# Changelog

## v1.3.2 — 2026-11-23

### Downloads
- O cliente de suporte agora baixa com nome simples: **`Remotely_Desktop_x64.exe`**, **`Remotely_Desktop_x86.exe`** e **`Remotely_Desktop`** (Linux), sem o endereço do servidor no nome.
- O endereço do servidor e a organização passam a ir gravados no final do próprio executável, e o cliente lê de lá ao abrir. Renomear o arquivo não quebra mais a conexão. Clientes baixados antes continuam funcionando (o formato antigo no nome ainda é aceito).
- Vale também para os binários personalizados (`/api/custom-binaries`).

## v1.3.1 — 2026-11-23

### Correções
- Downloads do cliente de suporte e dos instaladores com `Cache-Control: no-store`. Antes o navegador podia reaproveitar um download anterior e manter o nome antigo do arquivo (por exemplo, com `http~`), mesmo depois de o proxy ou o servidor já estarem corretos.

## v1.3.0 — 2026-11-23

### Inventário dos dispositivos
- Nova aba **Inventário** nos detalhes do dispositivo, no estilo do GLPI Inventory, com seções:
  - **Sistema operacional**: nome, edição, versão (25H2), build, arquitetura, **número de série do SO** (ID do produto), status da licença, idioma, fuso, instalação e última inicialização.
  - **Componentes**: BIOS/firmware (versão, data, UEFI, Secure Boot), placa-mãe, processadores, memórias (slot, tipo, frequência, fabricante, part number, série), discos (SSD/HD, NVMe/SATA, firmware, série, saúde), placas de vídeo, monitores, placas de som, controladoras, bateria (saúde) e TPM.
  - **Volumes**: uso, sistema de arquivos e BitLocker.
  - **Softwares**: lista com busca por nome, versão ou editor.
  - **Portas de rede**: MAC, IPv4/IPv6, máscara, gateway, DNS, DHCP, velocidade e status (adaptadores virtuais ficam ocultos por padrão).
  - **Antivírus**: produto, versão, se está ativo e atualizado (Central de Segurança e Microsoft Defender).
- Resumo no topo dos detalhes e no card: **tipo** (Desktop, Notebook, Servidor, Máquina virtual...), **fabricante**, **modelo**, **número de série**, **UUID**.
- Campos manuais **Número de inventário** (placa de patrimônio) e **Localização**, editáveis no card e nos detalhes.
- A busca de dispositivos encontra por número de série, patrimônio, modelo, fabricante, localização e tipo.
- O agente coleta ao conectar e a cada 12 horas; o botão **Atualizar inventário** pede uma coleta na hora. Windows via WMI e registro; Linux via DMI, `/proc`, `lsblk`, `lspci`, `dmidecode` e `dpkg`/`rpm`; macOS com dados básicos.
- API: `GET /api/Devices/{id}/inventory`.
- Migração de banco `Add_DeviceInventory` (SQLite, SQL Server e PostgreSQL), aplicada automaticamente ao iniciar.

### Nome do usuário
- O agente do Windows envia **nome e sobrenome em campos separados** (vindos do Active Directory). Para agentes antigos ou contas fora do domínio, o servidor separa o nome de exibição ("Silva, Michael" → nome Michael, sobrenome Silva).
- Nova opção em **Configuração → Geral → Exibição do nome do usuário**: "Nome Sobrenome" (padrão no Brasil), "Sobrenome, Nome" (EUA) ou como o sistema informa.

### Login e downloads
- Removido o link **"Entrar em uma sessão"** da tela de login.
- **Download do cliente sem `http~` atrás de proxy HTTPS:** o servidor agora identifica o HTTPS também pelo endereço da página que fez o download (e por `X-Forwarded-Ssl`, `X-Forwarded-Scheme` e `Front-End-Https`). O arquivo sai como `Remotely_Desktop_x86 (suporte.empresa.com.br).exe` mesmo quando o aaPanel não envia `X-Forwarded-Proto`. Vale também para os instaladores dos agentes.

### Correções
- O agente chamava `CheckForPendingSriptRuns` (com erro de digitação) ao conectar, e os scripts pendentes não eram executados até o próximo heartbeat.
- Execução de processos externos podia travar quando a saída era grande (a saída era lida só depois de o processo terminar).
- Os detalhes do dispositivo salvavam sem esperar o banco e sem conferir de novo o acesso ao dispositivo.

## v1.2.1 — 2026-11-23

### Login e downloads
- No login, o título, o texto e a área **"Precisa de suporte agora?"** ficam centralizados no painel esquerdo, também em telas grandes.
- **"Reenviar confirmação de e-mail"** só aparece quando o servidor exige confirmar o e-mail antes do primeiro acesso.
- Página pública de **Downloads** centralizada, com os cards ocupando a largura.
- **Nome curto no download do cliente de suporte:** `Remotely_Desktop_x64 (suporte.empresa.com.br).exe`, `Remotely_Desktop_x86 (...)` e `Remotely_Desktop (...)` no Linux, no lugar de `Remotely_Desktop[gq5Pcm...]`. O endereço entre parênteses é o que o cliente usa para achar o servidor; o formato antigo continua aceito.

### Dispositivos
- O card e os detalhes mostram o **nome e sobrenome** do usuário conectado, além do usuário (ex.: "Michael Rodrigues · ums112").
- **Sistema operacional com nome comercial e versão separados**: "Microsoft Windows 11 Enterprise" e, abaixo, "10.0.26300 · 25H2". No Linux, o nome da distribuição e a versão do kernel. Agentes antigos já aparecem como "Microsoft Windows 11" + versão.

### Proxy reverso
- Em Docker, o servidor passa a aceitar os cabeçalhos `X-Forwarded-*` de proxies em redes privadas (opção `TrustPrivateNetworkProxies`). Antes, atrás do aaPanel/Nginx o acesso era visto como `http` e o IP público dos dispositivos aparecia como o IP interno do Docker (ex.: `172.19.0.1`).

## v1.2.0 — 2026-11-23

### Perfis de acesso
- Nova seção **Perfis de acesso** na página Organização: crie perfis como "Técnico N1" ou "Somente consulta" e escolha o perfil de cada usuário direto na tabela de usuários.
- Cada perfil define:
  - **Acesso remoto** (controle remoto, somente visualizar e chat);
  - **Scripts e terminal** (terminal, executar, criar e agendar scripts);
  - **Gerenciar dispositivos** (editar apelido/grupo/observações, enviar arquivos, ligar via WoL, desinstalar o agente);
  - **Grupos visíveis**: com "Só os grupos escolhidos", os usuários do perfil passam a ver só os dispositivos desses grupos.
- As permissões valem no servidor (não só escondem botões): ações bloqueadas mostram um aviso e ficam registradas no log.
- Administradores sempre têm acesso total; usuários sem perfil continuam como antes.
- Os perfis ficam na tabela de configurações do banco, sem migração.


### Interface
- **Uma tela só para quem não está logado:** a página inicial pública foi removida e o endereço raiz abre direto o login.
- O login ganhou a área **"Precisa de suporte agora?"** com os clientes de suporte instantâneo (Windows 64/32 bits e Linux), além de links para entrar em uma sessão, todos os downloads e **Sobre**.
- Páginas públicas (Downloads, Sobre, Solicitar suporte) usam uma barra superior simples, sem o menu lateral.
- **"Criar uma conta"** só aparece enquanto o cadastro estiver liberado (pelo padrão, antes da primeira conta). Num servidor novo, o login mostra o aviso de primeiro acesso.
- **Avisos visíveis:** as mensagens de "salvo", "criado" e de erro agora aparecem como cartão no canto superior direito, com ícone e cor, por mais tempo. Antes ficavam escondidas atrás da barra de salvar e pareciam não acontecer.

### Scripts
- Aba **Biblioteca** com lista por pasta, busca, filtro "Todos/Meus", editor com contagem de linhas, chaves para "Público" e "Script rápido" e alertas de erro recolhíveis.
- Aba **Executar** em dois passos (script e onde executar), com seleção de grupos e dispositivos lado a lado, filtro de online, resumo e opção de executar nos offline quando conectarem.
- Aba **Agendamentos** com lista mostrando a próxima execução, formulário único (nome, script, primeira execução, repetição, recuperar atrasados) e o mesmo seletor de grupos e dispositivos.

### Conexão em tempo real
- **Correção: botões sem resposta na imagem Docker.** A imagem da v1.1.0 não trazia o script do Blazor (`_framework/blazor.web.js`, resposta 404), então as páginas abriam mas nada era interativo (salvar, criar grupo etc.). A imagem agora é publicada para `linux-x64`, como o pacote do release, o build confere se o script está presente e o servidor usa `MapStaticAssets()`.
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
