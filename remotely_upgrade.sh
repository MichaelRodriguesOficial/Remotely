#!/bin/bash
# ==============================================================================
# SCRIPT DE ATUALIZAÇÃO DO REMOTELY SERVER
# Atualiza o código mantendo dados e configurações
# Autor: Michael Rodrigues 
# Data: 10/2025
# ==============================================================================

# Cores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m'

# Variáveis globais
BACKUP_DIR="/app/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_NAME="remotely_backup_${TIMESTAMP}"
REPO_DIR="/app/Remotely"
PRODUCTION_DIR="/var/www/remotely"
REPO_URL="https://github.com/MichaelRodriguesOficial/Remotely.git"
# Indica se a pasta de produção já foi alterada (usado em check_error)
PRODUCTION_TOUCHED=false

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
        log_error "Atualização abortada!"
        if [ "$PRODUCTION_TOUCHED" = false ]; then
            # A falha ocorreu antes de mexer em produção (ex.: erro de build):
            # basta religar o serviço com a versão anterior.
            log_info "A instalação atual não foi alterada. Religando o serviço..."
            systemctl start remotely 2>/dev/null || true
        else
            log_warning "Execute o rollback: sudo ./update-remotely.sh --rollback $BACKUP_NAME"
        fi
        exit 1
    fi
}

# Função para mostrar uso
show_usage() {
    echo ""
    echo "╔═══════════════════════════════════════════════════════════════╗"
    echo "║           🔄 SCRIPT DE ATUALIZAÇÃO DO REMOTELY 🔄            ║"
    echo "╚═══════════════════════════════════════════════════════════════╝"
    echo ""
    echo "Uso:"
    echo "  sudo ./update-remotely.sh                    # Atualização normal"
    echo "  sudo ./update-remotely.sh --rollback NOME    # Restaurar backup"
    echo "  sudo ./update-remotely.sh --list-backups     # Listar backups"
    echo "  sudo ./update-remotely.sh --custom-repo URL  # Usar repositório customizado"
    echo ""
    exit 0
}

# Função para listar backups
list_backups() {
    log_step "📦 BACKUPS DISPONÍVEIS"
    
    if [ ! -d "$BACKUP_DIR" ] || [ -z "$(ls -A $BACKUP_DIR 2>/dev/null)" ]; then
        log_warning "Nenhum backup encontrado em $BACKUP_DIR"
        exit 0
    fi
    
    echo ""
    echo "Backups disponíveis:"
    echo "═══════════════════════════════════════════════════════════════"
    
    for backup in $(ls -1t $BACKUP_DIR); do
        SIZE=$(du -sh "$BACKUP_DIR/$backup" | cut -f1)
        DATE=$(echo $backup | sed 's/remotely_backup_//' | sed 's/_/ /')
        
        # Verificar se contém banco de dados
        if [ -f "$BACKUP_DIR/$backup/AppData/Remotely.db" ]; then
            DB_STATUS="✅ DB"
        else
            DB_STATUS="❌ No DB"
        fi
        
        printf "  %-40s %8s  %s\n" "$backup" "$SIZE" "$DB_STATUS"
    done
    
    echo "═══════════════════════════════════════════════════════════════"
    echo ""
    echo "Para restaurar um backup:"
    echo "  sudo ./update-remotely.sh --rollback NOME_DO_BACKUP"
    echo ""
    exit 0
}

# Função de rollback
rollback() {
    local BACKUP_TO_RESTORE="$1"
    
    if [ -z "$BACKUP_TO_RESTORE" ]; then
        log_error "Especifique o nome do backup para restaurar"
        echo "Use: sudo ./update-remotely.sh --list-backups"
        exit 1
    fi
    
    if [ ! -d "$BACKUP_DIR/$BACKUP_TO_RESTORE" ]; then
        log_error "Backup não encontrado: $BACKUP_TO_RESTORE"
        exit 1
    fi
    
    log_step "🔙 RESTAURANDO BACKUP: $BACKUP_TO_RESTORE"
    
    # Confirmar
    echo ""
    log_warning "ATENÇÃO: Isso irá substituir a instalação atual!"
    read -p "Deseja continuar? (s/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Ss]$ ]]; then
        log_info "Restauração cancelada"
        exit 0
    fi
    
    # Parar serviço
    log_info "Parando serviço Remotely..."
    systemctl stop remotely
    check_error "Falha ao parar serviço"
    
    # Fazer backup do estado atual (caso precise reverter)
    log_info "Criando backup de segurança do estado atual..."
    SAFETY_BACKUP="${BACKUP_DIR}/pre_rollback_${TIMESTAMP}"
    mkdir -p "$SAFETY_BACKUP"
    cp -r "$PRODUCTION_DIR" "$SAFETY_BACKUP/"
    log_success "Backup de segurança criado: $SAFETY_BACKUP"
    
    # Restaurar
    log_info "Restaurando arquivos de $BACKUP_TO_RESTORE..."
    PRODUCTION_TOUCHED=true
    rm -rf "$PRODUCTION_DIR"
    cp -r "$BACKUP_DIR/$BACKUP_TO_RESTORE" "$PRODUCTION_DIR"
    check_error "Falha ao restaurar arquivos"
    
    # Ajustar permissões
    log_info "Ajustando permissões..."
    chown -R www-data:www-data "$PRODUCTION_DIR"
    chmod -R 755 "$PRODUCTION_DIR"
    if [ -d "$PRODUCTION_DIR/AppData" ]; then
        chmod 750 "$PRODUCTION_DIR/AppData"
        find "$PRODUCTION_DIR/AppData" -type d -exec chmod 750 {} \;
        find "$PRODUCTION_DIR/AppData" -type f -exec chmod 640 {} \;
    fi
    
    # Reiniciar serviço
    log_info "Reiniciando serviço..."
    systemctl start remotely
    check_error "Falha ao iniciar serviço"
    
    sleep 5
    
    if systemctl is-active --quiet remotely; then
        log_success "✅ ROLLBACK CONCLUÍDO COM SUCESSO!"
        echo ""
        log_info "Serviço restaurado e rodando"
    else
        log_error "Serviço não iniciou corretamente após rollback"
        log_info "Verifique os logs: sudo journalctl -u remotely -n 50"
    fi
    
    exit 0
}

# Banner
clear
echo ""
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║                                                               ║"
echo "║           🔄 ATUALIZAÇÃO DO REMOTELY SERVER 🔄                ║"
echo "║                                                               ║"
echo "║  Este script irá:                                             ║"
echo "║  • Fazer backup completo do sistema atual                     ║"
echo "║  • Preservar banco de dados e configurações                   ║"
echo "║  • Atualizar código do repositório                            ║"
echo "║  • Recompilar e instalar nova versão                          ║"
echo "║                                                               ║"
echo "║  Autor: Michael Rodrigues            Data: 10/2025            ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

# Verificar se está rodando como root
if [ "$EUID" -ne 0 ]; then 
    log_error "Este script precisa ser executado com sudo"
    echo "Use: sudo ./update-remotely.sh"
    exit 1
fi

# Processar argumentos
case "$1" in
    --help|-h)
        show_usage
        ;;
    --list-backups|-l)
        list_backups
        ;;
    --rollback|-r)
        rollback "$2"
        ;;
    --custom-repo)
        if [ -z "$2" ]; then
            log_error "URL do repositório não especificada"
            exit 1
        fi
        REPO_URL="$2"
        log_info "Usando repositório customizado: $REPO_URL"
        ;;
    "")
        # Continuar com atualização normal
        ;;
    *)
        log_error "Opção inválida: $1"
        show_usage
        ;;
esac

# Verificar se Remotely está instalado
if [ ! -f "$PRODUCTION_DIR/Remotely_Server.dll" ]; then
    log_error "Remotely não está instalado em $PRODUCTION_DIR"
    log_info "Execute primeiro o script de instalação"
    exit 1
fi

# ============================================================================
log_step "📋 INFORMAÇÕES DO SISTEMA ATUAL"
# ============================================================================

log_info "Coletando informações..."
echo ""

# Versão atual (tentar extrair do AssemblyInfo ou arquivo de versão)
if [ -f "$PRODUCTION_DIR/Remotely_Server.dll" ]; then
    CURRENT_VERSION=$(strings "$PRODUCTION_DIR/Remotely_Server.dll" | grep -E "^[0-9]+\.[0-9]+\.[0-9]+" | head -n 1 || echo "Desconhecida")
    echo "Versão instalada: $CURRENT_VERSION"
fi

# Status do serviço
SERVICE_STATUS=$(systemctl is-active remotely)
echo "Status do serviço: $SERVICE_STATUS"

# Tamanho do banco de dados
if [ -f "$PRODUCTION_DIR/AppData/Remotely.db" ]; then
    DB_SIZE=$(du -sh "$PRODUCTION_DIR/AppData/Remotely.db" | cut -f1)
    echo "Tamanho do banco: $DB_SIZE"
else
    log_warning "Banco de dados não encontrado!"
fi

# Espaço em disco
DISK_SPACE=$(df -h / | awk 'NR==2 {print $4}')
echo "Espaço disponível: $DISK_SPACE"

echo ""
read -p "Deseja continuar com a atualização? (s/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Ss]$ ]]; then
    log_warning "Atualização cancelada pelo usuário"
    exit 0
fi

# ============================================================================
# O serviço é parado ANTES do backup: copiar o SQLite com o servidor rodando
# pode gerar um backup inconsistente.
log_step "1/8 - PARANDO SERVIÇO"
# ============================================================================

log_info "Parando serviço Remotely..."
systemctl stop remotely
check_error "Falha ao parar serviço"

# Aguardar processo terminar
sleep 3

if pgrep -f "Remotely_Server.dll" > /dev/null; then
    log_warning "Processo ainda rodando, forçando término..."
    pkill -9 -f "Remotely_Server.dll"
    sleep 2
fi

log_success "Serviço parado"

# ============================================================================
log_step "2/8 - CRIANDO BACKUP COMPLETO"
# ============================================================================

log_info "Criando diretório de backup..."
mkdir -p "$BACKUP_DIR"
# Os backups contêm o banco de dados e chaves: acesso só para o root.
chmod 700 "$BACKUP_DIR"

log_info "Criando backup: $BACKUP_NAME"
log_warning "Isso pode demorar alguns minutos..."

# Copiar tudo de produção
if ! cp -a "$PRODUCTION_DIR" "$BACKUP_DIR/$BACKUP_NAME"; then
    log_error "Falha ao criar backup. Nada foi alterado."
    log_info "Reiniciando o serviço..."
    systemctl start remotely
    exit 1
fi

# Criar manifesto do backup
cat > "$BACKUP_DIR/$BACKUP_NAME/backup_info.txt" <<EOF
Backup criado em: $(date)
Versão: $CURRENT_VERSION
Status do serviço: $SERVICE_STATUS
Tamanho do banco: ${DB_SIZE:-N/A}
Comando para restaurar: sudo ./update-remotely.sh --rollback $BACKUP_NAME
EOF

BACKUP_SIZE=$(du -sh "$BACKUP_DIR/$BACKUP_NAME" | cut -f1)
log_success "Backup criado com sucesso! ($BACKUP_SIZE)"
log_info "Localização: $BACKUP_DIR/$BACKUP_NAME"


# ============================================================================
log_step "3/8 - PRESERVANDO DADOS CRÍTICOS"
# ============================================================================

log_info "Criando backup temporário de dados..."
TEMP_DATA=$(mktemp -d /tmp/remotely_data_XXXXXX)
chmod 700 "$TEMP_DATA"

# Preservar banco de dados
if [ -f "$PRODUCTION_DIR/AppData/Remotely.db" ]; then
    log_info "Preservando banco de dados..."
    cp -r "$PRODUCTION_DIR/AppData" "$TEMP_DATA/"
    log_success "Banco de dados preservado"
else
    log_warning "Banco de dados não encontrado (primeira instalação?)"
fi

# Preservar instaladores/agents (wwwroot/Content). Eles foram baixados na
# instalação e não fazem parte do build: sem isso, a página de Downloads e a
# atualização automática dos agents quebravam após cada atualização.
if [ -d "$PRODUCTION_DIR/wwwroot/Content" ]; then
    log_info "Preservando instaladores (wwwroot/Content)..."
    mkdir -p "$TEMP_DATA/wwwroot"
    cp -a "$PRODUCTION_DIR/wwwroot/Content" "$TEMP_DATA/wwwroot/"
fi

# Preservar appsettings.json customizado (se existir)
if [ -f "$PRODUCTION_DIR/appsettings.json" ]; then
    log_info "Preservando appsettings.json..."
    cp "$PRODUCTION_DIR/appsettings.json" "$TEMP_DATA/appsettings.json.backup"
fi

# Preservar certificados SSL (se existirem)
if [ -d "$PRODUCTION_DIR/DataProtection-Keys" ]; then
    log_info "Preservando chaves de criptografia..."
    cp -r "$PRODUCTION_DIR/DataProtection-Keys" "$TEMP_DATA/"
fi

log_success "Dados críticos preservados em: $TEMP_DATA"

# ============================================================================
log_step "4/8 - ATUALIZANDO CÓDIGO DO REPOSITÓRIO"
# ============================================================================

if [ -d "$REPO_DIR" ]; then
    log_info "Repositório já existe, atualizando..."
    cd "$REPO_DIR"
    
    # Versões antigas do instalador alteravam o libman.json localmente.
    # Essa alteração não é mais necessária e é descartada automaticamente.
    git checkout -- Server/libman.json 2>/dev/null || true

    # Verificar se há modificações locais
    if ! git diff --quiet || ! git diff --cached --quiet; then
        log_warning "Há modificações locais não commitadas"
        read -p "Deseja descartar modificações locais? (s/N): " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Ss]$ ]]; then
            git reset --hard HEAD
            git clean -fd
        else
            log_error "Não é possível continuar com modificações locais"
            exit 1
        fi
    fi
    
    # Atualizar
    log_info "Baixando atualizações..."
    git fetch origin
    git pull origin master
    git submodule update --init --recursive --force
    check_error "Falha ao atualizar repositório"
else
    log_info "Clonando repositório..."
    mkdir -p /app
    cd /app
    git clone "$REPO_URL" --recurse-submodules --depth 1
    check_error "Falha ao clonar repositório"
fi

log_success "Código atualizado"

# Obter nova versão (se disponível)
cd "$REPO_DIR"
NEW_VERSION=$(git describe --tags --abbrev=0 2>/dev/null || git rev-parse --short HEAD)
log_info "Nova versão: $NEW_VERSION"

# ============================================================================
log_step "5/8 - RESTAURANDO DEPENDÊNCIAS"
# ============================================================================

cd "$REPO_DIR/Server"

log_info "Limpando builds anteriores..."
dotnet clean > /dev/null 2>&1
rm -rf bin/ obj/

# Remover pasta wwwroot/Components se existir (correção do bug)
if [ -d "wwwroot/Components" ]; then
    log_warning "Removendo pasta wwwroot/Components (arquivos gerados)..."
    rm -rf "wwwroot/Components"
fi

log_info "Restaurando dependências do .NET..."
dotnet restore
check_error "Falha ao restaurar dependências do .NET"

log_info "Restaurando bibliotecas do LibMan..."
export PATH="$PATH:/root/.dotnet/tools"
/root/.dotnet/tools/libman cache clean
/root/.dotnet/tools/libman restore
check_error "Falha ao restaurar bibliotecas do LibMan"

log_success "Dependências restauradas"

# ============================================================================
log_step "6/8 - COMPILANDO NOVA VERSÃO"
# ============================================================================

log_info "Compilando servidor (pode demorar 5-10 minutos)..."
dotnet publish -c Release -o bin/publish --no-restore
check_error "Falha ao compilar servidor"

# Verificar se compilou
if [ ! -f "bin/publish/Remotely_Server.dll" ]; then
    log_error "Arquivo Remotely_Server.dll não foi gerado!"
    exit 1
fi

log_success "Servidor compilado com sucesso"

# ============================================================================
log_step "7/8 - INSTALANDO NOVA VERSÃO"
# ============================================================================

log_info "Removendo versão antiga..."
PRODUCTION_TOUCHED=true
rm -rf "$PRODUCTION_DIR"/*
check_error "Falha ao limpar diretório de produção"

log_info "Copiando nova versão..."
cp -r "$REPO_DIR/Server/bin/publish/"* "$PRODUCTION_DIR/"
check_error "Falha ao copiar nova versão"

# Restaurar dados preservados
log_info "Restaurando dados preservados..."

if [ -d "$TEMP_DATA/AppData" ]; then
    cp -r "$TEMP_DATA/AppData" "$PRODUCTION_DIR/"
    log_success "Banco de dados restaurado"
fi

if [ -d "$TEMP_DATA/wwwroot/Content" ]; then
    mkdir -p "$PRODUCTION_DIR/wwwroot"
    cp -a "$TEMP_DATA/wwwroot/Content" "$PRODUCTION_DIR/wwwroot/"
    log_success "Instaladores restaurados"
fi

if [ -f "$TEMP_DATA/appsettings.json.backup" ]; then
    # Mantém a configuração em uso; a versão nova fica ao lado para comparação.
    if ! cmp -s "$TEMP_DATA/appsettings.json.backup" "$PRODUCTION_DIR/appsettings.json"; then
        cp "$PRODUCTION_DIR/appsettings.json" "$PRODUCTION_DIR/appsettings.json.new"
        cp "$TEMP_DATA/appsettings.json.backup" "$PRODUCTION_DIR/appsettings.json"
        log_warning "appsettings.json anterior mantido. Novo modelo salvo em appsettings.json.new"
    fi
fi

if [ -d "$TEMP_DATA/DataProtection-Keys" ]; then
    cp -r "$TEMP_DATA/DataProtection-Keys" "$PRODUCTION_DIR/"
    log_success "Chaves de criptografia restauradas"
fi

# Ajustar permissões
log_info "Ajustando permissões..."
chown -R www-data:www-data "$PRODUCTION_DIR"
chmod -R 755 "$PRODUCTION_DIR"
# AppData contém o banco e as chaves de criptografia: só o serviço lê.
if [ -d "$PRODUCTION_DIR/AppData" ]; then
    chmod 750 "$PRODUCTION_DIR/AppData"
    find "$PRODUCTION_DIR/AppData" -type d -exec chmod 750 {} \;
    find "$PRODUCTION_DIR/AppData" -type f -exec chmod 640 {} \;
fi

log_success "Nova versão instalada"

# Limpar dados temporários
rm -rf "$TEMP_DATA"

# ============================================================================
log_step "8/8 - REINICIANDO SERVIÇO"
# ============================================================================

log_info "Recarregando configurações do systemd..."
systemctl daemon-reload

log_info "Iniciando serviço..."
systemctl start remotely
check_error "Falha ao iniciar serviço"

log_info "Aguardando inicialização (20 segundos)..."
sleep 20

# Verificar se está rodando
if pgrep -f "Remotely_Server.dll" > /dev/null; then
    log_success "✅ Processo Remotely está RODANDO"
    
    if ss -tln | grep -q ':5000'; then
        log_success "✅ Serviço está ouvindo na porta 5000"
    fi
    
    if systemctl is-active --quiet remotely; then
        log_success "✅ Systemd reporta serviço como ATIVO"
    fi
else
    log_error "❌ Processo Remotely NÃO está rodando!"
    echo ""
    log_error "Executando rollback automático..."
    rollback "$BACKUP_NAME"
    exit 1
fi

# ============================================================================
# RELATÓRIO FINAL
# ============================================================================
clear
echo ""
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║                                                               ║"
echo "║           ✅ ATUALIZAÇÃO CONCLUÍDA COM SUCESSO! ✅            ║"
echo "║                                                               ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 📊 RESUMO DA ATUALIZAÇÃO                                    │"
echo "├─────────────────────────────────────────────────────────────┤"
echo "│ Versão anterior:  $CURRENT_VERSION"
echo "│ Nova versão:      $NEW_VERSION"
echo "│ Backup criado:    $BACKUP_NAME"
echo "│ Localização:      $BACKUP_DIR/$BACKUP_NAME"
echo "│ Tamanho:          $BACKUP_SIZE"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 📋 COMANDOS ÚTEIS                                           │"
echo "├─────────────────────────────────────────────────────────────┤"
echo "│ Ver status:       sudo systemctl status remotely            │"
echo "│ Ver logs:         sudo journalctl -u remotely -f            │"
echo "│ Listar backups:   sudo ./update-remotely.sh --list-backups  │"
echo "│ Fazer rollback:   sudo ./update-remotely.sh --rollback ...  │"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 🔄 ROLLBACK (SE NECESSÁRIO)                                 │"
echo "├─────────────────────────────────────────────────────────────┤"
echo "│ Se houver problemas, restaure o backup anterior:            │"
echo "│                                                              │"
echo "│ sudo ./update-remotely.sh --rollback $BACKUP_NAME"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

log_success "Atualização finalizada com sucesso!"
echo ""