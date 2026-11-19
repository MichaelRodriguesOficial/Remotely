#!/bin/bash
# ==============================================================================
# SCRIPT DE INSTALAÇÃO COMPLETA DO REMOTELY - UBUNTU 22.04/24.04
# COM SSL E PROXY REVERSO - Customizado para instalação com todos os componentes
# Autor: Michael Rodrigues 
# Data: 10/2025
# ==============================================================================

# Cores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

# Função para log de sucesso
log_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

# Função para log de info
log_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

# Função para log de warning
log_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

# Função para log de erro
log_error() {
    echo -e "${RED}❌ ERRO: $1${NC}"
}

# Função para log de etapa
log_step() {
    echo ""
    echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN}$1${NC}"
    echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
}

# Função para verificar último comando
check_error() {
    if [ $? -ne 0 ]; then
        log_error "$1"
        echo ""
        log_error "Instalação abortada. Verifique os logs acima."
        exit 1
    fi
}

# Função para obter IP externo
get_external_ip() {
    log_info "Obtendo IP externo..."
    EXTERNAL_IP=$(curl -s -4 --connect-timeout 5 https://ifconfig.me/ip || curl -s -4 --connect-timeout 5 https://api.ipify.org || curl -s -4 --connect-timeout 5 https://checkip.amazonaws.com || echo "Não detectado")
    
    if [ "$EXTERNAL_IP" = "Não detectado" ] || [ -z "$EXTERNAL_IP" ]; then
        log_warning "Não foi possível obter o IP externo automaticamente"
        echo ""
        log_info "Para acesso externo, você precisará:"
        log_info "1. Configurar port forwarding no seu roteador"
        log_info "2. Apontar as portas 80 e 443 para o IP interno: $SERVER_IP"
        log_info "3. Usar seu IP público ou DNS dinâmico"
        echo ""
    else
        log_success "IP externo detectado: $EXTERNAL_IP"
    fi
}

# Função para verificar e renovar SSL
check_renew_ssl() {
    log_step "🔐 VERIFICAÇÃO E RENOVAÇÃO SSL"
    
    if command -v certbot &> /dev/null; then
        log_info "Verificando certificados SSL existentes..."
        sudo certbot certificates
        
        echo ""
        read -p "Deseja renovar certificados SSL agora? (s/N): " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Ss]$ ]]; then
            log_info "Iniciando renovação de certificados SSL..."
            sudo certbot renew
            
            if [ $? -eq 0 ]; then
                log_success "Renovação de certificados concluída!"
                
                # Verificar se precisa recarregar o Nginx
                if systemctl is-active --quiet nginx; then
                    log_info "Recarregando configuração do Nginx..."
                    sudo systemctl reload nginx
                    log_success "Nginx recarregado com sucesso"
                fi
            else
                log_error "Falha na renovação dos certificados"
            fi
        else
            log_info "Renovação de SSL cancelada pelo usuário"
        fi
    else
        log_warning "Certbot não está instalado. Nenhum certificado SSL para verificar."
    fi
    
    echo ""
}

# Banner
clear
echo ""
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║                                                               ║"
echo "║        🚀 INSTALAÇÃO COMPLETA DO REMOTELY SERVER 🚀           ║"
echo "║                 COM SSL E ACESSO EXTERNO                      ║"
echo "║                                                               ║"
echo "║  Este script irá instalar:                                    ║"
echo "║  • ASP.NET Core Runtime 10                                    ║"
echo "║  • Remotely Server (release do GitHub do fork)                ║"
echo "║  • Agents para Windows, Linux e MacOS                         ║"
echo "║  • Nginx com SSL (Let's Encrypt)                              ║"
echo "║  • Proxy reverso para acesso externo seguro                   ║"
echo "║                                                               ║"
echo "║                                                               ║"
echo "║  Autor: Michael Rodrigues            Data: 10/2025            ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

# Verificar se está rodando como root/sudo
if [ "$EUID" -ne 0 ]; then 
    log_error "Este script precisa ser executado com sudo"
    echo "Use: sudo ./install-remotely.sh"
    exit 1
fi

# Menu inicial para verificar/renovar SSL
echo ""
echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 🎯 OPÇÕES DISPONÍVEIS                                       │"
echo "├─────────────────────────────────────────────────────────────┤"
echo "│ 1. Instalação Completa do Remotely Server                   │"
echo "│ 2. Verificar e Renovar Certificados SSL                     │"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

read -p "Selecione uma opção (1 ou 2): " -n 1 -r
echo

if [[ $REPLY =~ ^[2]$ ]]; then
    check_renew_ssl
    # A opção 2 só verifica/renova o SSL. Continuar aqui reinstalaria o
    # servidor (movendo /var/www/remotely para backup), o que não é esperado.
    log_info "Verificação de SSL concluída. Nenhuma alteração foi feita na instalação."
    exit 0
fi

# Solicitar configurações de domínio e SSL
echo ""
echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 🌐 CONFIGURAÇÃO DE DOMÍNIO E SSL                            │"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

read -p "Deseja configurar SSL com Let's Encrypt? (s/N): " -n 1 -r
echo
if [[ $REPLY =~ ^[Ss]$ ]]; then
    CONFIGURE_SSL=true
    echo ""
    log_info "Configuração SSL selecionada"
    read -p "📧 Digite seu e-mail para o Let's Encrypt: " SSL_EMAIL
    read -p "🌐 Digite seu domínio (ex: remotely.seudominio.com): " DOMAIN_NAME
    
    if [ -z "$SSL_EMAIL" ] || [ -z "$DOMAIN_NAME" ]; then
        log_error "E-mail e domínio são obrigatórios para SSL"
        exit 1
    fi
    
    log_success "SSL será configurado para: $DOMAIN_NAME"
    log_success "E-mail Let's Encrypt: $SSL_EMAIL"
else
    CONFIGURE_SSL=false
    log_warning "SSL não será configurado. O acesso será apenas por HTTP."
fi

# Confirmação da instalação
echo ""
read -p "Deseja continuar com a instalação? (s/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Ss]$ ]]; then
    log_warning "Instalação cancelada pelo usuário"
    exit 0
fi

# ============================================================================
log_step "1/10 - ATUALIZAÇÃO DO SISTEMA"
# ============================================================================
log_info "Atualizando pacotes do sistema..."
apt update && apt upgrade -y
check_error "Falha ao atualizar o sistema"
log_success "Sistema atualizado"

# ============================================================================
log_step "2/10 - CONFIGURAÇÃO DE TIMEZONE"
# ============================================================================
if [ -f /etc/timezone ] && grep -q "America/Sao_Paulo" /etc/timezone; then
    log_warning "Timezone já configurado para São Paulo"
else
    log_info "Configurando timezone para America/Sao_Paulo..."
    apt install -y tzdata
    ln -fs /usr/share/zoneinfo/America/Sao_Paulo /etc/localtime
    dpkg-reconfigure --frontend noninteractive tzdata
    check_error "Falha ao configurar timezone"
    log_success "Timezone configurado: $(date)"
fi

# ============================================================================
log_step "3/10 - INSTALAÇÃO DE UTILITÁRIOS BÁSICOS"
# ============================================================================
log_info "Instalando utilitários básicos..."
apt install -y nano curl wget unzip jq net-tools lsb-release
check_error "Falha ao instalar utilitários"
log_success "Utilitários instalados"

# ============================================================================
log_step "4/10 - INSTALAÇÃO DO ASP.NET CORE RUNTIME 10"
# ============================================================================
# O servidor vem compilado no release do GitHub: basta o runtime (sem SDK,
# Node.js, LibMan ou PowerShell no servidor).
if dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App 10\."; then
    log_warning "ASP.NET Core Runtime 10 já está instalado"
else
    log_info "Instalando ASP.NET Core Runtime 10..."
    UBUNTU_VERSION=$(lsb_release -rs)
    if [ "$UBUNTU_VERSION" != "24.04" ]; then
        # Ubuntu 22.04: o .NET 10 vem do PPA oficial de backports do Ubuntu.
        apt install -y software-properties-common
        add-apt-repository -y ppa:dotnet/backports
    fi
    apt update
    apt install -y aspnetcore-runtime-10.0
    check_error "Falha ao instalar o ASP.NET Core Runtime 10"
    log_success "Runtime instalado: $(dotnet --list-runtimes | grep AspNetCore | tail -n 1)"
fi

# ============================================================================
log_step "5/10 - INSTALAÇÃO DO NGINX E CERTBOT"
# ============================================================================
log_info "Instalando Nginx e Certbot..."
apt install -y nginx certbot python3-certbot-nginx
check_error "Falha ao instalar Nginx e Certbot"

log_info "Configurando firewall..."
ufw allow 'Nginx Full'
ufw allow 'OpenSSH'
log_success "Nginx e Certbot instalados"

# ============================================================================
log_step "6/10 - DOWNLOAD DO REMOTELY (RELEASE DO GITHUB)"
# ============================================================================
GITHUB_REPO="MichaelRodriguesOficial/Remotely"
PACKAGE_NAME="Remotely_Server_Linux-x64.zip"
# Desde a v1.1.0 os agents vêm num pacote separado (o do servidor ficou bem menor).
AGENTS_PACKAGE="Remotely_Agents.zip"
# Versão específica: sudo REMOTELY_VERSION=v1.0.0 ./remotely_install.sh
REMOTELY_VERSION="${REMOTELY_VERSION:-latest}"

if [ "$REMOTELY_VERSION" = "latest" ]; then
    log_info "Obtendo a versão mais recente..."
    REMOTELY_VERSION=$(curl -fsSL "https://api.github.com/repos/$GITHUB_REPO/releases/latest" | jq -r .tag_name)
fi
if [ -z "$REMOTELY_VERSION" ] || [ "$REMOTELY_VERSION" = "null" ]; then
    log_error "Não foi possível descobrir a versão em github.com/$GITHUB_REPO/releases"
    exit 1
fi
log_success "Versão: $REMOTELY_VERSION"

RELEASE_DIR=$(mktemp -d /tmp/remotely_release_XXXXXX)
RELEASE_URL="https://github.com/$GITHUB_REPO/releases/download/$REMOTELY_VERSION"
log_info "Baixando $PACKAGE_NAME..."
curl -fSL --progress-bar "$RELEASE_URL/$PACKAGE_NAME" -o "$RELEASE_DIR/$PACKAGE_NAME"
check_error "Falha ao baixar $RELEASE_URL/$PACKAGE_NAME"

if curl -fsSL "$RELEASE_URL/$PACKAGE_NAME.sha256" -o "$RELEASE_DIR/$PACKAGE_NAME.sha256"; then
    (cd "$RELEASE_DIR" && sha256sum -c "$PACKAGE_NAME.sha256")
    check_error "Checksum SHA-256 do pacote não confere"
    log_success "Checksum SHA-256 conferido"
else
    log_warning "Release sem arquivo .sha256; checksum não verificado"
fi

mkdir -p "$RELEASE_DIR/app"
unzip -q "$RELEASE_DIR/$PACKAGE_NAME" -d "$RELEASE_DIR/app"
check_error "Falha ao extrair o pacote"

log_info "Baixando $AGENTS_PACKAGE (agents e clientes de suporte)..."
if curl -fSL --progress-bar "$RELEASE_URL/$AGENTS_PACKAGE" -o "$RELEASE_DIR/$AGENTS_PACKAGE"; then
    if curl -fsSL "$RELEASE_URL/$AGENTS_PACKAGE.sha256" -o "$RELEASE_DIR/$AGENTS_PACKAGE.sha256"; then
        (cd "$RELEASE_DIR" && sha256sum -c "$AGENTS_PACKAGE.sha256")
        check_error "Checksum SHA-256 do pacote de agents não confere"
    fi
    mkdir -p "$RELEASE_DIR/app/wwwroot/Content"
    unzip -oq "$RELEASE_DIR/$AGENTS_PACKAGE" -d "$RELEASE_DIR/app/wwwroot/Content"
    check_error "Falha ao extrair o pacote de agents"
elif [ ! -f "$RELEASE_DIR/app/wwwroot/Content/Remotely-Win-x64.zip" ]; then
    log_error "Não foi possível baixar $AGENTS_PACKAGE"
    exit 1
else
    # Releases antigos (v1.0.x) traziam os agents dentro do pacote do servidor.
    log_info "Release com os agents dentro do pacote do servidor"
fi

if [ ! -f "$RELEASE_DIR/app/Remotely_Server.dll" ] || [ ! -f "$RELEASE_DIR/app/wwwroot/Content/Remotely-Win-x64.zip" ]; then
    log_error "Pacote incompleto (servidor ou instaladores dos agents ausentes)"
    exit 1
fi
log_success "Pacote do servidor e instaladores dos agents prontos"

# ============================================================================
log_step "7/10 - CÓPIA PARA PRODUÇÃO"
# ============================================================================
log_info "Preparando diretório de produção..."

# Backup se existir
KEEP_EXISTING_DATA=false
if [ -d "/var/www/remotely" ]; then
    BACKUP_NAME="remotely.backup.$(date +%Y%m%d_%H%M%S)"
    BACKUP_PATH="/app/$BACKUP_NAME"
    log_warning "Criando backup: $BACKUP_PATH"
    mv /var/www/remotely "$BACKUP_PATH"
    chmod 700 "$BACKUP_PATH"
    log_info "Backup criado em: $BACKUP_PATH"

    # Sem esta etapa, a reinstalação começaria com um banco de dados vazio
    # (usuários, dispositivos e configurações ficariam apenas no backup).
    if [ -d "$BACKUP_PATH/AppData" ] || [ -f "$BACKUP_PATH/Remotely.db" ]; then
        echo ""
        read -p "Deseja manter o banco de dados e as configurações existentes? (S/n): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Nn]$ ]]; then
            KEEP_EXISTING_DATA=true
        fi
    fi
fi

log_info "Copiando arquivos para /var/www/remotely..."
mkdir -p /var/www/remotely
cp -a "$RELEASE_DIR/app/." /var/www/remotely/
check_error "Falha ao copiar arquivos"

log_info "Criando diretório de dados..."
mkdir -p /var/www/remotely/AppData

if [ "$KEEP_EXISTING_DATA" = true ]; then
    log_info "Restaurando banco de dados e configurações do backup..."
    [ -d "$BACKUP_PATH/AppData" ] && cp -a "$BACKUP_PATH/AppData/." /var/www/remotely/AppData/
    # Instalações antigas gravavam o banco na raiz (/var/www/remotely/Remotely.db)
    if [ ! -f /var/www/remotely/AppData/Remotely.db ] && [ -f "$BACKUP_PATH/Remotely.db" ]; then
        for f in Remotely.db Remotely.db-wal Remotely.db-shm; do
            [ -f "$BACKUP_PATH/$f" ] && cp -a "$BACKUP_PATH/$f" /var/www/remotely/AppData/
        done
    fi
    if [ -f "$BACKUP_PATH/appsettings.json" ]; then
        cp /var/www/remotely/appsettings.json /var/www/remotely/appsettings.json.new
        cp "$BACKUP_PATH/appsettings.json" /var/www/remotely/appsettings.json
    fi
    log_success "Dados existentes preservados"
fi

# Guarda o checksum do pacote de agents para o remotely_upgrade.sh só baixá-lo de novo quando mudar.
if [ -f "$RELEASE_DIR/$AGENTS_PACKAGE.sha256" ]; then
    cp "$RELEASE_DIR/$AGENTS_PACKAGE.sha256" /var/www/remotely/AppData/.agents-package.sha256
fi

log_info "Ajustando permissões..."
chown -R www-data:www-data /var/www/remotely
chmod -R 755 /var/www/remotely
# AppData contém o banco (hashes de senha, chaves de API, senha SMTP) e as
# chaves de criptografia: somente o usuário do serviço pode ler.
chmod 750 /var/www/remotely/AppData
find /var/www/remotely/AppData -type d -exec chmod 750 {} \;
find /var/www/remotely/AppData -type f -exec chmod 640 {} \;

log_success "Arquivos copiados para produção"

# ============================================================================
log_step "8/10 - CONFIGURAÇÃO DO SERVIÇO SYSTEMD"
# ============================================================================
# Parar serviço se estiver rodando
systemctl stop remotely 2>/dev/null || true

log_info "Criando serviço systemd..."
cat > /etc/systemd/system/remotely.service <<'SERVICE_EOF'
[Unit]
Description=Remotely Server
After=network.target
StartLimitIntervalSec=0

[Service]
Type=exec
WorkingDirectory=/var/www/remotely
ExecStart=/usr/bin/dotnet /var/www/remotely/Remotely_Server.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=remotely
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Remotely_ConnectionStrings__SQLite=DataSource=/var/www/remotely/AppData/Remotely.db
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
TimeoutStartSec=180
TimeoutStopSec=30
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
SERVICE_EOF

check_error "Falha ao criar serviço"
log_success "Serviço criado"

# ============================================================================
log_step "9/10 - CONFIGURAÇÃO NGINX E SSL"
# ============================================================================

# ✅ CORREÇÃO: Limpeza completa das configurações anteriores
log_info "Limpando configurações anteriores do Nginx..."
rm -f /etc/nginx/sites-enabled/remotely*
rm -f /etc/nginx/sites-available/remotely*
rm -f /etc/nginx/sites-enabled/default

# Criar configuração básica temporária
cat > /etc/nginx/sites-available/remotely-temp <<NGINX_TEMP_EOF
server {
    listen 80;
    server_name _;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
        proxy_buffering off;
        client_max_body_size 100M;
    }
}
NGINX_TEMP_EOF

# Ativar configuração temporária
ln -sf /etc/nginx/sites-available/remotely-temp /etc/nginx/sites-enabled/

log_info "Testando configuração temporária..."
nginx -t
check_error "Configuração temporária do Nginx inválida"

log_info "Iniciando Nginx com configuração temporária..."
systemctl start nginx
check_error "Falha ao iniciar Nginx"

if [ "$CONFIGURE_SSL" = true ]; then
    log_info "Configurando SSL para domínio: $DOMAIN_NAME"
    
    # ✅ CORREÇÃO: Obter certificado usando método STANDALONE (mais confiável)
    log_info "Obtendo certificado SSL usando método standalone..."
    
    # Parar Nginx temporariamente para liberar porta 80
    log_info "Parando Nginx temporariamente para obter certificado..."
    systemctl stop nginx
    
    # Obter certificado em modo standalone
    # Os hooks ficam salvos na configuração de renovação. Sem eles, o
    # "certbot renew" automático falharia (porta 80 ocupada pelo Nginx) e o
    # certificado expiraria em 90 dias.
    if certbot certonly --standalone -d "$DOMAIN_NAME" --non-interactive --agree-tos --email "$SSL_EMAIL" --preferred-challenges http \
        --pre-hook "systemctl stop nginx" \
        --post-hook "systemctl start nginx"; then
        log_success "✅ Certificado SSL obtido com sucesso via standalone!"
    else
        log_error "❌ Falha ao obter certificado SSL"
        log_warning "Continuando sem SSL..."
        CONFIGURE_SSL=false
    fi
    
    # Reiniciar Nginx
    log_info "Reiniciando Nginx..."
    systemctl start nginx
    check_error "Falha ao reiniciar Nginx"
    
    # ✅ CORREÇÃO: Criar configuração final baseada no sucesso do SSL
    if [ "$CONFIGURE_SSL" = true ] && [ -f "/etc/letsencrypt/live/$DOMAIN_NAME/fullchain.pem" ]; then
        log_info "Criando configuração final do Nginx com SSL..."
        
        cat > /etc/nginx/sites-available/remotely <<NGINX_EOF
# Redirecionamento HTTP para HTTPS
server {
    listen 80;
    server_name $DOMAIN_NAME;
    return 301 https://\$server_name\$request_uri;
}

# Servidor HTTPS
server {
    listen 443 ssl http2;
    server_name $DOMAIN_NAME;

    ssl_certificate /etc/letsencrypt/live/$DOMAIN_NAME/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/$DOMAIN_NAME/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-RSA-AES256-GCM-SHA512:DHE-RSA-AES256-GCM-SHA512:ECDHE-RSA-AES256-GCM-SHA384:DHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers off;

    # Configurações de segurança
    # (X-Frame-Options, nosniff e Referrer-Policy já são enviados pelo Remotely;
    # duplicá-los aqui gera valores conflitantes que o navegador ignora.)
    server_tokens off;
    add_header Strict-Transport-Security "max-age=31536000" always;

    # Proxy para aplicação Remotely
    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
        proxy_buffering off;
        client_max_body_size 100M;
    }

    # Configurações de timeout
    proxy_connect_timeout 600s;
    proxy_send_timeout 600s;
    proxy_read_timeout 600s;
}
NGINX_EOF

        # Verificar renovação automática
        if systemctl is-active --quiet certbot.timer; then
            log_success "🔄 Renovação automática CONFIGURADA"
        else
            log_info "Ativando timer de renovação automática..."
            systemctl enable certbot.timer
            systemctl start certbot.timer
        fi
        
    else
        log_warning "Usando configuração HTTP devido à falha no SSL"
        CONFIGURE_SSL=false
    fi
fi

# ✅ CORREÇÃO: Se SSL falhou ou não foi configurado, usar HTTP
if [ "$CONFIGURE_SSL" = false ]; then
    log_info "Criando configuração final do Nginx para HTTP..."
    
    cat > /etc/nginx/sites-available/remotely <<NGINX_HTTP_EOF
server {
    listen 80;
    server_name _;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
        proxy_buffering off;
        client_max_body_size 100M;
    }
}
NGINX_HTTP_EOF
fi

# Remover configuração temporária e ativar a final
rm -f /etc/nginx/sites-enabled/remotely-temp
ln -sf /etc/nginx/sites-available/remotely /etc/nginx/sites-enabled/

# Testar e reiniciar Nginx
log_info "Testando configuração final do Nginx..."
nginx -t
check_error "Configuração final do Nginx inválida"

log_info "Reiniciando Nginx com configuração final..."
systemctl restart nginx
check_error "Falha ao reiniciar Nginx"

log_success "Nginx configurado com sucesso!"

# ============================================================================
log_step "10/10 - INICIALIZAÇÃO DO SERVIÇO"
# ============================================================================
log_info "Recarregando configurações do systemd..."
systemctl daemon-reload

log_info "Habilitando serviço para iniciar no boot..."
systemctl enable remotely
check_error "Falha ao habilitar serviço"

log_info "Iniciando serviço Remotely..."
systemctl start remotely
check_error "Falha ao iniciar serviço"

# Aguardar um pouco mais e verificar de forma mais inteligente
log_info "Aguardando serviço inicializar (20 segundos)..."
sleep 20

# Verificação mais tolerante - verificar se o processo está rodando
if pgrep -f "Remotely_Server.dll" > /dev/null; then
    log_success "Processo Remotely está rodando (PID: $(pgrep -f 'Remotely_Server.dll'))"
    
    # Verificar se está ouvindo na porta
    if ss -tln | grep -q ':5000'; then
        log_success "Serviço está ouvindo na porta 5000"
        SERVICE_STATUS="active"
    else
        log_warning "Serviço iniciado mas não está na porta 5000 ainda..."
        SERVICE_STATUS="starting"
    fi
else
    log_error "Processo Remotely NÃO está rodando!"
    echo ""
    log_info "Últimas linhas do log:"
    journalctl -u remotely -n 20 --no-pager
    exit 1
fi

# Verificação final do systemd (mais tolerante)
if systemctl is-active --quiet remotely; then
    log_success "Serviço systemd reporta como ATIVO"
else
    log_warning "Serviço systemd ainda está iniciando, mas o processo está rodando"
    log_info "Isso é normal para aplicações ASP.NET Core"
fi

# ============================================================================
# VERIFICAÇÃO FINAL E RELATÓRIO
# ============================================================================
clear
echo ""
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║                                                               ║"
echo "║           ✅ INSTALAÇÃO CONCLUÍDA COM SUCESSO! ✅             ║"
echo "║                                                               ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

# Verificação final tolerante
log_info "Verificando status final do serviço..."
sleep 5

if pgrep -f "Remotely_Server.dll" > /dev/null; then
    log_success "✅ Processo Remotely está RODANDO (PID: $(pgrep -f 'Remotely_Server.dll'))"
    
    # Verificar porta usando ss (mais moderno que netstat)
    if ss -tln | grep -q ':5000'; then
        log_success "✅ Serviço está ouvindo na porta 5000"
    else
        log_warning "⚠️  Serviço rodando mas porta 5000 não detectada"
    fi
    
    # Verificar systemd
    SYSTEMD_STATUS=$(systemctl is-active remotely)
    if [ "$SYSTEMD_STATUS" = "active" ]; then
        log_success "✅ Systemd reporta serviço como ATIVO"
    else
        log_warning "⚠️  Systemd reporta: $SYSTEMD_STATUS (mas processo está rodando)"
    fi
    
else
    log_error "❌ Processo Remotely NÃO está rodando!"
    echo ""
    log_info "Últimas linhas do log:"
    journalctl -u remotely -n 20 --no-pager
    exit 1
fi

# Informações do sistema
SERVER_IP=$(hostname -I | awk '{print $1}')

# Obter IP externo
get_external_ip

echo ""
echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 📊 INFORMAÇÕES DO SISTEMA                                   │"
echo "├─────────────────────────────────────────────────────────────┤"
if [ "$CONFIGURE_SSL" = true ]; then
    echo "│ 🌐 URL Principal:  https://$DOMAIN_NAME                    "
    echo "│ 🔄 Redireciona:    http → https automaticamente             │"
else
    echo "│ 🌐 URL Local:      http://$SERVER_IP                       "
    if [ "$EXTERNAL_IP" != "Não detectado" ] && [ -n "$EXTERNAL_IP" ]; then
        echo "│ 🌐 URL Externa:    http://$EXTERNAL_IP                    "
    else
        echo "│ 🌐 URL Externa:    Configure port forwarding            │"
    fi
fi
echo "│ 🗂️  Diretório:     /var/www/remotely                         │"
echo "│ 💾 Banco de Dados: /var/www/remotely/AppData/Remotely.db    │"
echo "│ 📝 Logs:           /var/www/remotely/AppData/logs/          │"
if [ -n "$BACKUP_PATH" ] && [ -d "$BACKUP_PATH" ]; then
    echo "│ 💾 Backup anterior: $BACKUP_PATH │"
fi
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

# Adicionar informações SSL se configurado
if [ "$CONFIGURE_SSL" = true ]; then
    echo "┌─────────────────────────────────────────────────────────────┐"
    echo "│ 🔐 INFORMAÇÕES SSL                                         │"
    echo "├─────────────────────────────────────────────────────────────┤"
    if [ -f "/etc/letsencrypt/live/$DOMAIN_NAME/fullchain.pem" ]; then
        EXPIRY_DATE=$(sudo openssl x509 -in /etc/letsencrypt/live/$DOMAIN_NAME/fullchain.pem -noout -enddate | cut -d= -f2)
        echo "│ ✅ SSL Configurado: https://$DOMAIN_NAME                  "
        echo "│ 📅 Expira em: $EXPIRY_DATE                       │"
        echo "│ 🔄 Renovação: Automática (a cada 90 dias)                   │"
        echo "│ ⚡ Timer: Ativo - Verifique com: sudo certbot certificates  │"
    else
        echo "│ ❌ SSL Não configurado                                   │"
        echo "│ ⚠️  Execute: certbot --nginx -d $DOMAIN_NAME            │"
    fi
    echo "└─────────────────────────────────────────────────────────────┘"
    echo ""
fi

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 📦 INSTALADORES DISPONÍVEIS                                 │"
echo "├─────────────────────────────────────────────────────────────┤"
ls -lh /var/www/remotely/wwwroot/Content/*.zip | while read line; do
    FILE=$(echo $line | awk '{print $9}' | xargs basename)
    SIZE=$(echo $line | awk '{print $5}')
    printf "│ %-40s %15s    │\n" "$FILE" "$SIZE"
done
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 📋 COMANDOS ÚTEIS                                           │"
echo "├─────────────────────────────────────────────────────────────┤"
echo "│ Ver status:    sudo systemctl status remotely               │"
echo "│ Ver logs:      sudo journalctl -u remotely -f               │"
echo "│ Nginx status:  sudo systemctl status nginx                  │"
echo "│ Reiniciar:     sudo systemctl restart remotely              │"
if [ "$CONFIGURE_SSL" = true ]; then
    echo "│ Ver SSL:      sudo certbot certificates                     │"
    echo "│ Renovar SSL:  sudo certbot renew                             │"
fi
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 🚀 PRÓXIMOS PASSOS                                          │"
echo "├─────────────────────────────────────────────────────────────┤"
if [ "$CONFIGURE_SSL" = true ]; then
    echo "│ 1. Acesse: https://$DOMAIN_NAME                           "
    echo "│ 2. Configure DNS para apontar para: $EXTERNAL_IP         │"
else
    echo "│ 1. Acesse: http://$SERVER_IP                              "
    echo "│ 2. Para SSL, execute o script novamente                  │"
fi
echo "│ 3. Clique em 'Register' para criar sua conta                │"
echo "│ 4. Esta conta será o Admin do servidor                      │"
echo "│ 5. Baixe os agents na seção 'Downloads'                     │"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

log_success "Instalação finalizada com sucesso!"
echo ""