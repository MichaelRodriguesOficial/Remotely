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
echo "║  • Dependências (Node.js, .NET, PowerShell)                   ║"
echo "║  • Remotely Server                                            ║"
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
log_step "1/16 - ATUALIZAÇÃO DO SISTEMA"
# ============================================================================
log_info "Atualizando pacotes do sistema..."
apt update && apt upgrade -y
check_error "Falha ao atualizar o sistema"
log_success "Sistema atualizado"

# ============================================================================
log_step "2/16 - CONFIGURAÇÃO DE TIMEZONE"
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
log_step "3/16 - INSTALAÇÃO DE UTILITÁRIOS BÁSICOS"
# ============================================================================
log_info "Instalando utilitários básicos..."
apt install -y nano git curl wget unzip net-tools
check_error "Falha ao instalar utilitários"
log_success "Utilitários instalados"

# ============================================================================
log_step "4/16 - INSTALAÇÃO DO NODE.JS 20.x"
# ============================================================================
if command -v node &> /dev/null; then
    NODE_VERSION=$(node --version)
    log_warning "Node.js já está instalado ($NODE_VERSION)"
else
    log_info "Instalando Node.js 20.x..."
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
    apt install -y nodejs
    check_error "Falha ao instalar Node.js"
    log_success "Node.js instalado: $(node --version)"
fi

# ============================================================================
log_step "5/16 - INSTALAÇÃO DO .NET 8 SDK"
# ============================================================================
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    log_warning ".NET SDK já está instalado ($DOTNET_VERSION)"
else
    log_info "Instalando .NET 8 SDK..."
    
    # Detectar versão do Ubuntu
    UBUNTU_VERSION=$(lsb_release -rs)
    
    if [ "$UBUNTU_VERSION" == "24.04" ]; then
        wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    else
        wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    fi
    
    dpkg -i packages-microsoft-prod.deb
    rm packages-microsoft-prod.deb
    apt update
    apt install -y dotnet-sdk-8.0
    check_error "Falha ao instalar .NET SDK"
    log_success ".NET SDK instalado: $(dotnet --version)"
fi

# ============================================================================
log_step "6/16 - INSTALAÇÃO DO POWERSHELL"
# ============================================================================
if command -v pwsh &> /dev/null; then
    PWSH_VERSION=$(pwsh --version | head -n 1)
    log_warning "PowerShell já está instalado ($PWSH_VERSION)"
else
    log_info "Instalando PowerShell..."
    apt install -y powershell
    check_error "Falha ao instalar PowerShell"
    log_success "PowerShell instalado: $(pwsh --version | head -n 1)"
fi

# ============================================================================
log_step "7/16 - INSTALAÇÃO DO NGINX E CERTBOT"
# ============================================================================
log_info "Instalando Nginx e Certbot..."
apt install -y nginx certbot python3-certbot-nginx
check_error "Falha ao instalar Nginx e Certbot"

log_info "Configurando firewall..."
ufw allow 'Nginx Full'
ufw allow 'OpenSSH'
log_success "Nginx e Certbot instalados"

# ============================================================================
log_step "8/16 - CLONANDO REPOSITÓRIO REMOTELY"
# ============================================================================
if [ -d "/app/Remotely" ]; then
    log_warning "Repositório já existe em /app/Remotely"
    log_info "Atualizando repositório..."
    cd /app/Remotely
    # Versões antigas deste script alteravam o libman.json localmente, o que
    # impedia o "git pull". Descarta só essa alteração antes de atualizar.
    git checkout -- Server/libman.json 2>/dev/null || true
    git pull
    git submodule update --init --recursive
else
    log_info "Clonando repositório do GitHub..."
    mkdir -p /app
    cd /app
    git clone https://github.com/MichaelRodriguesOficial/Remotely.git --recurse-submodules
    check_error "Falha ao clonar repositório"
    log_success "Repositório clonado"
fi

cd /app/Remotely

# ============================================================================
log_step "9/16 - INSTALAÇÃO DO LIBMAN"
# ============================================================================
export PATH="$PATH:/root/.dotnet/tools"

if command -v libman &> /dev/null; then
    log_warning "LibMan já está instalado"
    log_info "Atualizando LibMan..."
    dotnet tool update -g Microsoft.Web.LibraryManager.CLI 2>/dev/null || true
else
    log_info "Instalando LibMan..."
    dotnet tool install -g Microsoft.Web.LibraryManager.CLI
    check_error "Falha ao instalar LibMan"
fi

# Adicionar ao PATH permanente
if ! grep -q "/.dotnet/tools" /root/.bashrc; then
    echo 'export PATH="$PATH:/root/.dotnet/tools"' >> /root/.bashrc
fi

log_success "LibMan configurado"

# ============================================================================
log_step "10/16 - VERIFICAÇÃO DO LIBMAN.JSON"
# ============================================================================
# O libman.json do repositório já usa o jsDelivr. Ele não é mais sobrescrito
# aqui, para não deixar o repositório com alterações locais (o que travava o
# script de atualização).
cd /app/Remotely/Server
git checkout -- libman.json 2>/dev/null || true
log_success "libman.json do repositório será usado"

# ============================================================================
log_step "11/16 - RESTAURAÇÃO DE DEPENDÊNCIAS"
# ============================================================================
log_info "Restaurando dependências do .NET..."
cd /app/Remotely/Server
dotnet restore
check_error "Falha ao restaurar dependências do .NET"

log_info "Limpando cache do LibMan..."
/root/.dotnet/tools/libman cache clean

log_info "Restaurando bibliotecas do LibMan..."
/root/.dotnet/tools/libman restore
check_error "Falha ao restaurar bibliotecas do LibMan"

log_success "Dependências restauradas"

# ============================================================================
log_step "12/16 - BUILD DO SERVIDOR"
# ============================================================================
log_info "Compilando servidor Remotely (pode demorar 5-10 minutos)..."
cd /app/Remotely/Server
dotnet publish -c Release -o bin/publish
check_error "Falha ao compilar servidor"

# Verificar se gerou o arquivo principal
if [ ! -f "bin/publish/Remotely_Server.dll" ]; then
    log_error "Arquivo Remotely_Server.dll não foi gerado!"
    exit 1
fi

log_success "Servidor compilado com sucesso"

# ============================================================================
log_step "13/16 - DOWNLOAD DOS INSTALADORES"
# ============================================================================

# ✅ CORREÇÃO: Instalar dependências necessárias
log_info "Instalando dependências necessárias..."
apt-get update > /dev/null 2>&1
apt-get install -y jq unzip wget > /dev/null 2>&1

log_info "Criando pasta Content para instaladores..."
mkdir -p /app/Remotely/Server/bin/publish/wwwroot/Content/
cd /app/Remotely/Server/bin/publish/wwwroot/Content/

log_info "Baixando instaladores do GitHub..."
log_warning "Isso pode demorar alguns minutos dependendo da conexão..."

# ============================================================
# 1️⃣ Obter tag da última release no GitHub
# ============================================================
log_info "Obtendo informações da última release..."
latest_tag=$(curl -s https://api.github.com/repos/immense/Remotely/releases/latest | jq -r .tag_name)
if [ -z "$latest_tag" ] || [ "$latest_tag" == "null" ]; then
    log_error "Falha ao obter a versão mais recente do Remotely."
    exit 1
fi
log_success "Última versão detectada: $latest_tag"

# ============================================================
# 2️⃣ Baixar e extrair o conteúdo do servidor (Content/)
# ============================================================
SERVER_ZIP="Remotely_Server_Linux-x64.zip"
SERVER_URL="https://github.com/immense/Remotely/releases/download/${latest_tag}/${SERVER_ZIP}"

log_info "Baixando arquivos do servidor ($SERVER_ZIP)..."
wget -q --show-progress "$SERVER_URL" -O "$SERVER_ZIP"

if [ $? -eq 0 ]; then
    log_success "$SERVER_ZIP baixado ($(du -h $SERVER_ZIP | cut -f1))"
    log_info "Extraindo conteúdo da pasta 'wwwroot/Content'..."
    unzip -o "$SERVER_ZIP" "wwwroot/Content/*" -d /tmp/remotely_server >/dev/null
    cp -r /tmp/remotely_server/wwwroot/Content/* ./
    rm -rf /tmp/remotely_server "$SERVER_ZIP"
    log_success "Conteúdo do servidor copiado para $(pwd)"
else
    log_error "Falha crítica ao baixar $SERVER_ZIP"
    exit 1
fi

# ============================================================
# 3️⃣ Verificar se os instaladores principais estão presentes
# ============================================================
if [ ! -f "Remotely-Win-x64.zip" ]; then
    log_error "Instalador principal (Win-x64) não foi encontrado!"
    exit 1
fi

log_success "Instaladores prontos"

# ============================================================================
log_step "14/16 - CÓPIA PARA PRODUÇÃO"
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
    if [ -d "$BACKUP_PATH/AppData" ]; then
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
cp -r /app/Remotely/Server/bin/publish/* /var/www/remotely/
check_error "Falha ao copiar arquivos"

log_info "Criando diretório de dados..."
mkdir -p /var/www/remotely/AppData

if [ "$KEEP_EXISTING_DATA" = true ]; then
    log_info "Restaurando banco de dados e configurações do backup..."
    cp -a "$BACKUP_PATH/AppData/." /var/www/remotely/AppData/
    if [ -f "$BACKUP_PATH/appsettings.json" ]; then
        cp /var/www/remotely/appsettings.json /var/www/remotely/appsettings.json.new
        cp "$BACKUP_PATH/appsettings.json" /var/www/remotely/appsettings.json
    fi
    log_success "Dados existentes preservados"
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
log_step "15/16 - CONFIGURAÇÃO DO SERVIÇO SYSTEMD"
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
log_step "16/16 - CONFIGURAÇÃO NGINX E SSL"
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
log_step "17/16 - INICIALIZAÇÃO DO SERVIÇO"
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