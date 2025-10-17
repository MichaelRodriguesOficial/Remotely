#!/bin/bash
# ==============================================================================
# SCRIPT DE REMOÇÃO COMPLETA DO REMOTELY SERVER
# Preserva Certbot mas permite gerenciar certificados SSL
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

# Funções de log
log_success() { echo -e "${GREEN}✅ $1${NC}"; }
log_info() { echo -e "${BLUE}ℹ️  $1${NC}"; }
log_warning() { echo -e "${YELLOW}⚠️  $1${NC}"; }
log_error() { echo -e "${RED}❌ $1${NC}"; }
log_step() {
    echo ""
    echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN}$1${NC}"
    echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
}

# Banner
clear
echo ""
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║                                                               ║"
echo "║        🗑️  REMOÇÃO COMPLETA DO REMOTELY SERVER 🗑️            ║"
echo "║             (Preserva Certbot e gerencia SSL)                 ║"
echo "║                                                               ║"
echo "║  Autor: Michael Rodrigues            Data: 10/2025            ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

# Verificar se está rodando como root/sudo
if [ "$EUID" -ne 0 ]; then 
    log_error "Este script precisa ser executado com sudo"
    echo "Use: sudo ./uninstall-remotely.sh"
    exit 1
fi

# Menu de opções
echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 🎯 O QUE SERÁ FEITO:                                        │"
echo "├─────────────────────────────────────────────────────────────┤"
echo "│ ✅ Parar e remover serviço Remotely                         │"
echo "│ ✅ Remover arquivos de produção (/var/www/remotely)         │"
echo "│ ✅ Remover repositório clonado (/app/Remotely)              │"
echo "│ ✅ Remover configurações do Nginx                           │"
echo "│ ✅ Limpar cache de ferramentas .NET                         │"
echo "│ ⚠️  Certificados SSL: Você escolhe o que fazer              │"
echo "│ ❌ NÃO remove: .NET SDK, Node.js, PowerShell, Nginx         │"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

log_warning "ATENÇÃO: Esta ação NÃO PODE SER DESFEITA!"
echo ""
read -p "Deseja continuar com a remoção? (s/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Ss]$ ]]; then
    log_info "Remoção cancelada pelo usuário"
    exit 0
fi

# ============================================================================
log_step "1/7 - PARAR E REMOVER SERVIÇO REMOTELY"
# ============================================================================
log_info "Parando serviço Remotely..."
systemctl stop remotely 2>/dev/null && log_success "Serviço parado" || log_info "Serviço não estava rodando"

log_info "Desabilitando serviço do boot..."
systemctl disable remotely 2>/dev/null && log_success "Serviço desabilitado" || log_info "Serviço não estava habilitado"

log_info "Removendo arquivo de serviço..."
if [ -f "/etc/systemd/system/remotely.service" ]; then
    rm -f /etc/systemd/system/remotely.service
    log_success "Arquivo de serviço removido"
else
    log_info "Arquivo de serviço não encontrado"
fi

log_info "Recarregando daemon do systemd..."
systemctl daemon-reload
log_success "Daemon recarregado"

# ============================================================================
log_step "2/7 - REMOVER ARQUIVOS DE PRODUÇÃO"
# ============================================================================
if [ -d "/var/www/remotely" ]; then
    log_info "Criando backup antes de remover..."
    BACKUP_NAME="remotely.final-backup.$(date +%Y%m%d_%H%M%S)"
    BACKUP_PATH="/app/$BACKUP_NAME"
    
    tar -czf "${BACKUP_PATH}.tar.gz" -C /var/www remotely 2>/dev/null
    if [ $? -eq 0 ]; then
        log_success "Backup criado: ${BACKUP_PATH}.tar.gz"
        log_info "Tamanho: $(du -h ${BACKUP_PATH}.tar.gz | cut -f1)"
    else
        log_warning "Falha ao criar backup (continuando...)"
    fi
    
    log_info "Removendo diretório /var/www/remotely..."
    rm -rf /var/www/remotely
    log_success "Diretório de produção removido"
else
    log_info "Diretório /var/www/remotely não encontrado"
fi

# Remover backups antigos
BACKUP_COUNT=$(find /var/www -maxdepth 1 -name "remotely.backup.*" 2>/dev/null | wc -l)
if [ $BACKUP_COUNT -gt 0 ]; then
    log_info "Encontrados $BACKUP_COUNT backup(s) antigo(s)"
    echo ""
    read -p "Deseja remover os backups antigos? (s/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Ss]$ ]]; then
        rm -rf /var/www/remotely.backup.*
        log_success "Backups antigos removidos"
    else
        log_info "Backups preservados em /var/www/"
    fi
fi

# ============================================================================
log_step "3/7 - REMOVER REPOSITÓRIO CLONADO"
# ============================================================================
if [ -d "/app/Remotely" ]; then
    log_info "Removendo repositório /app/Remotely..."
    rm -rf /app/Remotely
    log_success "Repositório removido"
else
    log_info "Repositório /app/Remotely não encontrado"
fi

# Verificar se pasta /app está vazia
if [ -d "/app" ]; then
    if [ -z "$(ls -A /app 2>/dev/null)" ]; then
        log_info "Removendo diretório /app vazio..."
        rmdir /app
        log_success "Diretório /app removido"
    else
        log_warning "Diretório /app contém outros arquivos, preservando..."
    fi
fi

# ============================================================================
log_step "4/7 - REMOVER CONFIGURAÇÕES DO NGINX"
# ============================================================================
log_info "Removendo configurações do Nginx..."

# Remover links simbólicos
REMOVED_COUNT=0
for config in /etc/nginx/sites-enabled/remotely*; do
    if [ -f "$config" ] || [ -L "$config" ]; then
        rm -f "$config"
        log_success "Removido: $(basename $config)"
        ((REMOVED_COUNT++))
    fi
done

# Remover arquivos de configuração
for config in /etc/nginx/sites-available/remotely*; do
    if [ -f "$config" ]; then
        rm -f "$config"
        log_success "Removido: $(basename $config)"
        ((REMOVED_COUNT++))
    fi
done

if [ $REMOVED_COUNT -eq 0 ]; then
    log_info "Nenhuma configuração do Nginx encontrada"
else
    log_info "Testando configuração do Nginx..."
    if nginx -t 2>/dev/null; then
        log_success "Configuração do Nginx válida"
        log_info "Recarregando Nginx..."
        systemctl reload nginx
        log_success "Nginx recarregado"
    else
        log_warning "Erros na configuração do Nginx (verifique manualmente)"
    fi
fi

# ============================================================================
log_step "5/7 - LIMPAR CACHE DE FERRAMENTAS .NET"
# ============================================================================
if [ -f "/root/.dotnet/tools/libman" ]; then
    log_info "Limpando cache do LibMan..."
    /root/.dotnet/tools/libman cache clean 2>/dev/null && log_success "Cache do LibMan limpo" || log_info "Cache já estava limpo"
else
    log_info "LibMan não está instalado"
fi

# Limpar cache NuGet (opcional)
if command -v dotnet &> /dev/null; then
    echo ""
    read -p "Deseja limpar o cache do NuGet também? (s/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Ss]$ ]]; then
        log_info "Limpando cache do NuGet..."
        dotnet nuget locals all --clear 2>/dev/null && log_success "Cache do NuGet limpo" || log_warning "Falha ao limpar cache"
    fi
fi

# ============================================================================
log_step "6/7 - GERENCIAR CERTIFICADOS SSL"
# ============================================================================
if command -v certbot &> /dev/null; then
    log_info "Listando certificados SSL instalados..."
    
    # Obter lista de certificados
    CERT_LIST=$(certbot certificates 2>/dev/null | grep "Certificate Name:" | awk '{print $3}')
    
    if [ -z "$CERT_LIST" ]; then
        log_info "Nenhum certificado SSL encontrado"
    else
        echo ""
        echo "┌─────────────────────────────────────────────────────────────┐"
        echo "│ 🔐 CERTIFICADOS SSL ENCONTRADOS                             │"
        echo "└─────────────────────────────────────────────────────────────┘"
        
        # Exibir detalhes dos certificados
        certbot certificates 2>/dev/null | grep -A 5 "Certificate Name:"
        
        echo ""
        log_warning "O Remotely pode estar usando algum destes certificados"
        echo ""
        read -p "Deseja remover algum certificado SSL? (s/N): " -n 1 -r
        echo
        
        if [[ $REPLY =~ ^[Ss]$ ]]; then
            echo ""
            log_info "Certificados disponíveis:"
            
            # Criar array de certificados
            CERT_ARRAY=()
            INDEX=1
            while IFS= read -r cert; do
                echo "  [$INDEX] $cert"
                CERT_ARRAY+=("$cert")
                ((INDEX++))
            done <<< "$CERT_LIST"
            
            echo "  [0] Cancelar / Não remover nenhum"
            echo ""
            read -p "Digite o número do certificado para remover (0 para cancelar): " CERT_CHOICE
            
            if [ "$CERT_CHOICE" -gt 0 ] && [ "$CERT_CHOICE" -le "${#CERT_ARRAY[@]}" ]; then
                SELECTED_CERT="${CERT_ARRAY[$((CERT_CHOICE-1))]}"
                log_warning "Você escolheu remover: $SELECTED_CERT"
                echo ""
                read -p "Confirma a remoção deste certificado? (s/N): " -n 1 -r
                echo
                
                if [[ $REPLY =~ ^[Ss]$ ]]; then
                    log_info "Removendo certificado $SELECTED_CERT..."
                    certbot delete --cert-name "$SELECTED_CERT"
                    
                    if [ $? -eq 0 ]; then
                        log_success "Certificado removido com sucesso!"
                    else
                        log_error "Falha ao remover certificado"
                    fi
                else
                    log_info "Remoção de certificado cancelada"
                fi
            else
                log_info "Nenhum certificado será removido"
            fi
        else
            log_info "Certificados SSL preservados"
        fi
    fi
else
    log_info "Certbot não está instalado"
fi

# ============================================================================
log_step "7/7 - VERIFICAÇÃO FINAL"
# ============================================================================
echo ""
echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 🔍 VERIFICANDO REMOÇÃO                                      │"
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

ERRORS=0

# Verificar serviço
if [ ! -f "/etc/systemd/system/remotely.service" ]; then
    log_success "Serviço removido"
else
    log_error "Serviço ainda existe"
    ((ERRORS++))
fi

# Verificar diretório de produção
if [ ! -d "/var/www/remotely" ]; then
    log_success "/var/www/remotely removido"
else
    log_error "/var/www/remotely ainda existe"
    ((ERRORS++))
fi

# Verificar repositório
if [ ! -d "/app/Remotely" ]; then
    log_success "/app/Remotely removido"
else
    log_error "/app/Remotely ainda existe"
    ((ERRORS++))
fi

# Verificar configurações Nginx
NGINX_CONFIGS=$(find /etc/nginx/sites-* -name "remotely*" 2>/dev/null | wc -l)
if [ $NGINX_CONFIGS -eq 0 ]; then
    log_success "Configurações do Nginx removidas"
else
    log_warning "Ainda existem $NGINX_CONFIGS configuração(ões) do Nginx"
fi

# Verificar processo rodando
if pgrep -f "Remotely_Server.dll" > /dev/null; then
    log_error "Processo Remotely ainda está rodando!"
    log_info "PID: $(pgrep -f 'Remotely_Server.dll')"
    ((ERRORS++))
else
    log_success "Nenhum processo Remotely rodando"
fi

# ============================================================================
# RELATÓRIO FINAL
# ============================================================================
echo ""
echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║                                                               ║"
if [ $ERRORS -eq 0 ]; then
    echo "║           ✅ REMOÇÃO CONCLUÍDA COM SUCESSO! ✅                ║"
else
    echo "║           ⚠️  REMOÇÃO CONCLUÍDA COM AVISOS ⚠️                 ║"
fi
echo "║                                                               ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ 📋 RESUMO                                                   │"
echo "├─────────────────────────────────────────────────────────────┤"
echo "│ ✅ Serviço Remotely: Removido                                │"
echo "│ ✅ Arquivos de produção: Removidos                           │"
echo "│ ✅ Repositório: Removido                                      │"
echo "│ ✅ Configurações Nginx: Removidas                             │"
echo "│ ✅ Cache .NET: Limpo                                          │"
if [ -f "${BACKUP_PATH}.tar.gz" ]; then
    echo "│ 💾 Backup final: ${BACKUP_PATH}.tar.gz │"
fi
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

echo "┌─────────────────────────────────────────────────────────────┐"
echo "│ ⚙️  FERRAMENTAS PRESERVADAS                                  │"
echo "├─────────────────────────────────────────────────────────────┤"
if command -v dotnet &> /dev/null; then
    echo "│ ✅ .NET SDK: $(dotnet --version)                           "
else
    echo "│ ❌ .NET SDK: Não instalado                                 │"
fi

if command -v node &> /dev/null; then
    echo "│ ✅ Node.js: $(node --version)                              "
else
    echo "│ ❌ Node.js: Não instalado                                  │"
fi

if command -v pwsh &> /dev/null; then
    echo "│ ✅ PowerShell: Instalado                                   │"
else
    echo "│ ❌ PowerShell: Não instalado                               │"
fi

if command -v nginx &> /dev/null; then
    echo "│ ✅ Nginx: $(nginx -v 2>&1 | cut -d/ -f2)                   "
else
    echo "│ ❌ Nginx: Não instalado                                    │"
fi

if command -v certbot &> /dev/null; then
    CERT_COUNT=$(certbot certificates 2>/dev/null | grep "Certificate Name:" | wc -l)
    echo "│ ✅ Certbot: Instalado ($CERT_COUNT certificado(s))         "
else
    echo "│ ❌ Certbot: Não instalado                                  │"
fi
echo "└─────────────────────────────────────────────────────────────┘"
echo ""

if [ $ERRORS -eq 0 ]; then
    log_success "🎉 Sistema limpo! Pronto para nova instalação."
else
    log_warning "⚠️  Verifique os itens marcados com erro acima"
fi

echo ""
log_info "Para reinstalar o Remotely, execute o script de instalação novamente"
echo ""