# Remotely Server - imagem multi-stage (.NET 10)
#
# Build (na raiz do repositório):
#   docker build -f Server/Dockerfile -t michechel10/remotely:dev .
#
# Instaladores/agents (wwwroot/Content):
#   - no CI, os agents compilados no mesmo build (.NET 10) são colocados em docker/agents;
#   - num build local sem essa pasta, o pacote Remotely_Agents.zip do último release
#     deste fork é baixado (AGENTS_URL). Para gerar os agents localmente, rode
#     Utilities/Build-Agents.ps1 -OutDir docker/agents no Windows.

ARG DOTNET_VERSION=10.0

# ---------- Build do servidor ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG VERSION=1.0.0
WORKDIR /src

# A restauração precisa do código-fonte completo: o SDK só inclui o pacote com o
# script do Blazor (_framework/blazor.web.js) quando enxerga os componentes .razor.
# Restaurar só com o .csproj gerava uma imagem sem esse script e sem botões funcionando.
COPY Directory.Build.props ./
COPY Shared/ Shared/
COPY Server/ Server/
RUN dotnet restore Server/Server.csproj -r linux-x64

# Os .js do viewer já estão versionados, então o build do TypeScript (que
# exigiria Node.js) é desativado aqui.
# Publicado para linux-x64 (o mesmo do pacote do release), usando o runtime da imagem.
RUN dotnet publish Server/Server.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish \
    --no-restore \
    -p:DebugType=None \
    -p:TypeScriptCompileBlocked=true \
    -p:Version=${VERSION} \
    -p:InformationalVersion=${VERSION}

# O script do Blazor (_framework/blazor.web.js) é o que liga os botões e formulários.
# O teste de fumaça do CI falha (com a lista de arquivos) se ele não estiver na imagem.
RUN ls -la /app/publish/wwwroot/_framework || echo "AVISO: _framework/blazor.web.js não foi gerado"

# ---------- Instaladores dos agents ----------
FROM alpine:3.20 AS content
ARG AGENTS_URL=https://github.com/MichaelRodriguesOficial/Remotely/releases/latest/download/Remotely_Agents.zip
COPY Server/wwwroot/Content/ /content/
COPY docker/agents/ /tmp/agents/
RUN set -e; \
    if [ -f /tmp/agents/Remotely-Win-x64.zip ]; then \
        echo "Usando os agents compilados neste build"; \
        cp -a /tmp/agents/. /content/; \
    elif [ -n "$AGENTS_URL" ]; then \
        echo "Baixando agents de $AGENTS_URL"; \
        apk add --no-cache curl unzip > /dev/null; \
        curl -fsSL "$AGENTS_URL" -o /tmp/agents.zip; \
        unzip -oq /tmp/agents.zip -d /content; \
        rm -f /tmp/agents.zip; \
    else \
        echo "AVISO: imagem sem os instaladores dos agents"; \
    fi; \
    rm -f /content/README.md; \
    rm -rf /tmp/agents

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}
ARG VERSION=1.0.0

LABEL org.opencontainers.image.title="Remotely" \
      org.opencontainers.image.description="Remotely Server (fork MichaelRodriguesOficial) - suporte remoto, acesso autônomo e scripts" \
      org.opencontainers.image.source="https://github.com/MichaelRodriguesOficial/Remotely" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.licenses="GPL-3.0"

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
COPY --from=content /content ./wwwroot/Content

# AppData guarda banco SQLite, logs, gravações e chaves de criptografia.
# O processo roda sem root (usuário "app", UID 1654, padrão das imagens .NET).
RUN mkdir -p /app/AppData /app/temp \
    && chown -R $APP_UID:$APP_UID /app/AppData /app/temp

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=5000 \
    DOTNET_PRINT_TELEMETRY_MESSAGE=false \
    Remotely_ApplicationOptions__DbProvider=SQLite \
    Remotely_ConnectionStrings__SQLite="Data Source=/app/AppData/Remotely.db"

USER $APP_UID
EXPOSE 5000
VOLUME ["/app/AppData"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fs http://localhost:5000/api/healthcheck || exit 1

ENTRYPOINT ["dotnet", "Remotely_Server.dll"]
