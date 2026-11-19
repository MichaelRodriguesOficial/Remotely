# Remotely — fork MichaelRodriguesOficial

[![Tests](https://github.com/MichaelRodriguesOficial/Remotely/actions/workflows/run_tests.yml/badge.svg?branch=master)](https://github.com/MichaelRodriguesOficial/Remotely/actions/workflows/run_tests.yml)
[![Release](https://github.com/MichaelRodriguesOficial/Remotely/actions/workflows/release.yml/badge.svg)](https://github.com/MichaelRodriguesOficial/Remotely/actions/workflows/release.yml)
[![GitHub release](https://img.shields.io/github/v/release/MichaelRodriguesOficial/Remotely)](https://github.com/MichaelRodriguesOficial/Remotely/releases)
[![Docker](https://img.shields.io/badge/docker-michechel10%2Fremotely-2496ED?logo=docker&logoColor=white)](https://hub.docker.com/r/michechel10/remotely)

Solução de **suporte remoto**, **acesso autônomo** e **execução remota de scripts**, feita em .NET, Blazor e SignalR.

Este repositório é um fork do [immense/Remotely](https://github.com/immense/Remotely), cujo desenvolvimento foi encerrado (arquivado em dez/2024). Aqui o projeto segue mantido com correções de segurança, interface em português e servidor em **.NET 10**.

![Remotely](.github/media/ask-remote.png)

## O que muda neste fork

- **Servidor em .NET 10** (o .NET 8 saiu do suporte em novembro de 2026).
- **Interface em português (pt-BR)**, com novo visual nos temas escuro e claro e layout responsivo.
- **Segurança:** dezenas de correções. Entre elas:
  - páginas administrativas protegidas também na navegação interativa;
  - filtros de API que negam o acesso em caso de erro;
  - bloqueio de conta após 5 tentativas e rate limit no login;
  - validação do ID da organização nos instaladores (evita injeção de comandos);
  - correção de XSS com dados vindos dos dispositivos;
  - IDs de sessão com RNG criptográfico;
  - cabeçalhos de segurança;
  - Swagger desligado em produção;
  - dependências vulneráveis atualizadas.
- **Imagem Docker própria** (`michechel10/remotely`), sem root e com healthcheck.
- **Scripts de instalação e atualização** que usam os releases deste repositório e preservam banco, chaves e instaladores dos agents.

Lista completa em [CHANGELOG.md](CHANGELOG.md) e nos [releases](https://github.com/MichaelRodriguesOficial/Remotely/releases).

---

## Instalação com Docker (recomendada)

Requisitos: Docker e Docker Compose.

```bash
mkdir -p ~/remotely && cd ~/remotely
curl -fsSLO https://raw.githubusercontent.com/MichaelRodriguesOficial/Remotely/master/docker-compose/docker-compose.yml
curl -fsSL https://raw.githubusercontent.com/MichaelRodriguesOficial/Remotely/master/docker-compose/.env.example -o .env
docker compose up -d
```

Acesse `http://IP_DO_SERVIDOR:5000` e clique em **Cadastrar**. A primeira conta vira administradora do servidor e da organização. Depois disso, o cadastro público é desativado.

- **Dados:** os dados ficam no volume `remotely-data`, montado em `/app/AppData`. Lá estão o banco SQLite, os logs, as gravações e as chaves de criptografia.
- **Pasta do host em vez do volume:** monte `/sua/pasta:/app/AppData` e rode `sudo chown -R 1654:1654 /sua/pasta`, porque o container roda com o usuário `app`.
- **Versão:** defina `REMOTELY_TAG` no `.env` (ex.: `1.0.0` ou `latest`).
- **Atualizar:** `docker compose pull && docker compose up -d`.

A imagem também é publicada em `ghcr.io/michaelrodriguesoficial/remotely`.

## Instalação no Ubuntu (sem Docker)

Testado em Ubuntu 22.04 e 24.04. O script instala o ASP.NET Core Runtime 10 e o Nginx, com HTTPS opcional via Let's Encrypt. Depois baixa o servidor já compilado do último release e cria o serviço `remotely`.

```bash
curl -fsSLO https://raw.githubusercontent.com/MichaelRodriguesOficial/Remotely/master/remotely_install.sh
chmod +x remotely_install.sh
sudo ./remotely_install.sh
# versão específica:
sudo REMOTELY_VERSION=v1.0.0 ./remotely_install.sh
```

| Script | Para que serve |
|---|---|
| `remotely_install.sh` | Instalação completa (runtime, Nginx, SSL, serviço systemd). A opção 2 só verifica/renova o certificado. |
| `remotely_upgrade.sh` | Atualiza para o último release (ou `--version vX.Y.Z`). Faz backup, preserva banco, chaves e configurações, e religa a versão anterior se algo falhar. Também tem `--list-backups` e `--rollback NOME`. |
| `remotely_remove.sh` | Remove a instalação, com backup final do banco. |

Os arquivos ficam em `/var/www/remotely`, com os dados em `/var/www/remotely/AppData`. O Kestrel escuta só em `127.0.0.1:5000`; o acesso externo é feito pelo Nginx.

## HTTPS e proxy reverso

Use um proxy reverso (Nginx, Caddy, Traefik) com HTTPS na frente do Remotely. Ele precisa repassar os cabeçalhos `X-Forwarded-Proto`, `X-Forwarded-Host` e `X-Forwarded-For`, além de suportar WebSockets.

O ASP.NET Core só aceita esses cabeçalhos de proxies conhecidos:

- **loopback**: liberado automaticamente;
- **gateway da rede Docker** (`172.28.0.1`): liberado automaticamente;
- **outros proxies**: cadastre o IP em **Configuração do servidor → Proxies conhecidos**.

Se o proxy não puder ser configurado direito, ative **Forçar HTTPS nos clientes**.

## Configuração

O banco e as strings de conexão ficam nas variáveis de ambiente (`.env` / `docker-compose.yml`) ou no `appsettings.json`. O resto é configurado na página **Configuração do servidor**, logado como administrador do servidor.

Opções exclusivas deste fork (`ApplicationOptions`, ou variáveis `Remotely_ApplicationOptions__*`):

| Opção | Padrão | Descrição |
|---|---|---|
| `EnableSwagger` | `false` | Publica o Swagger (`/swagger`) fora do ambiente de desenvolvimento. |
| `AllowedFrameAncestors` | vazio | Origens que podem exibir o Remotely em `<iframe>` (ex.: `https://glpi.suaempresa.com.br`). Vazio = só o próprio Remotely. |
| `AuthRateLimitPerMinute` | `20` | Tentativas de login/cadastro/redefinição por IP por minuto. |

Banco de dados: `DbProvider` aceita `SQLite` (padrão), `SQLServer` ou `PostgreSQL`, com as strings de conexão `ConnectionStrings__SQLite`, `ConnectionStrings__SQLServer` e `ConnectionStrings__PostgreSQL`.

## Agents e clientes

Os instaladores dos agents (Windows, Linux, macOS) e os clientes de suporte instantâneo estão na página **Downloads**. A página **Implantar** traz um comando pronto para copiar e colar.

Os binários dos agents vêm do último release oficial do Remotely e são incluídos na imagem Docker e no pacote do release. O protocolo com o servidor é o mesmo.

## API

Crie uma chave em **Chaves de API** e envie no cabeçalho `X-Api-Key: {id-da-chave}:{segredo}`.

```http
POST https://remotely.suaempresa.com.br/api/Scripting/ExecuteCommand/PSCore/{deviceId}
X-Api-Key: 31fb288d-af97-4ce1-ae7b-ceebb98281ac:HLkrKaZGExYvozSPvcACZw9awKkhHnNK
Content-Type: text/plain

Get-Location
```

Para navegar pela API, ative `EnableSwagger` e acesse `/swagger`.

A API de alertas (`POST /api/Alerts/Create`) permite criar alertas no painel, enviar e-mails e chamar webhooks, como n8n ou Chatwoot.

## Desenvolvimento

- **SDK:** .NET 10 para servidor e testes; .NET 8 para agents e desktop.
- **Windows:** necessário para compilar a solução inteira (`Desktop.Win`).

```bash
dotnet build Remotely.sln
dotnet test
# só o servidor:
dotnet run --project Server
# imagem Docker local:
docker build -f Server/Dockerfile -t remotely:dev .
```

Os arquivos `.ts` do viewer (`Server/wwwroot/src`) são compilados para `.js` no build (requer Node.js) e os `.js` também ficam versionados.

### CI e releases

- **Tests:** compila a solução e roda os testes a cada push e PR.
- **Release:** em PRs, compila a imagem Docker e faz um teste de fumaça. O teste sobe o container, migra o banco, cadastra o primeiro usuário e verifica login, permissões e cabeçalhos.
  - Ao publicar um release, o workflow publica a imagem no Docker Hub (`michechel10/remotely`) e no GHCR.
  - Também anexa `Remotely_Server_Linux-x64.zip` e o SHA-256 ao release.
  - O Docker Hub exige os secrets `DOCKERHUB_USERNAME` e `DOCKERHUB_TOKEN` no repositório.

## Créditos e licença

Projeto original: [immense/Remotely](https://github.com/immense/Remotely), de Jared Goodwin e colaboradores.

Licença: [GPL-3.0](LICENSE), a mesma do projeto original. As bibliotecas de terceiros estão listadas na página **Créditos** do sistema.
