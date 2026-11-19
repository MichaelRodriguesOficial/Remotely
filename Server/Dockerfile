# Remotely Server - imagem multi-stage (.NET 10)
#
# Build (na raiz do repositório):
#   docker build -f Server/Dockerfile -t michechel10/remotely:dev .
#
# Os instaladores/agents (wwwroot/Content) vêm do último release oficial do
# Remotely, já que os agents não são compilados neste repositório.

ARG DOTNET_VERSION=10.0

# ---------- Build do servidor ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG VERSION=1.0.0
WORKDIR /src

# Restaura dependências primeiro (melhor uso de cache)
COPY Directory.Build.props ./
COPY Shared/Shared.csproj Shared/
COPY Server/Server.csproj Server/
RUN dotnet restore Server/Server.csproj

COPY Shared/ Shared/
COPY Server/ Server/

# Os .js do viewer já estão versionados, então o build do TypeScript (que
# exigiria Node.js) é desativado aqui.
RUN dotnet publish Server/Server.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:TypeScriptCompileBlocked=true \
    -p:Version=${VERSION} \
    -p:InformationalVersion=${VERSION}

# ---------- Instaladores dos agents ----------
FROM alpine:3.20 AS content
ARG AGENTS_URL=https://github.com/immense/Remotely/releases/latest/download/Remotely_Server_Linux-x64.zip
RUN apk add --no-cache curl unzip \
    && curl -fsSL "$AGENTS_URL" -o /tmp/server.zip \
    && unzip -q /tmp/server.zip "wwwroot/Content/*" -d /tmp/extract \
    && mv /tmp/extract/wwwroot/Content /content \
    && rm -rf /tmp/server.zip /tmp/extract

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
