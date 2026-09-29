#!/bin/bash
# Instala Tailscale no Linux e conecta à rede (acesso ao SQL Server remoto).
#
# Opção A — variável de ambiente:
#   sudo TAILSCALE_AUTH_KEY='tskey-auth-...' bash deploy/install-tailscale.sh
#
# Opção B — arquivo local (não versionado):
#   echo 'tskey-auth-...' > deploy/tailscale.auth
#   sudo bash deploy/install-tailscale.sh

set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Execute como root: sudo bash $0"
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AUTH_FILE="${SCRIPT_DIR}/tailscale.auth"

if [ -z "${TAILSCALE_AUTH_KEY:-}" ] && [ -f "${AUTH_FILE}" ]; then
  TAILSCALE_AUTH_KEY="$(tr -d '\r\n' < "${AUTH_FILE}")"
fi

if [ -z "${TAILSCALE_AUTH_KEY:-}" ]; then
  echo "Defina TAILSCALE_AUTH_KEY ou crie ${AUTH_FILE}"
  echo "Chaves em: https://login.tailscale.com/admin/settings/keys"
  exit 1
fi

echo "Instalando Tailscale..."
curl -fsSL https://tailscale.com/install.sh | sh

echo "Conectando à rede Tailscale..."
tailscale up --auth-key="${TAILSCALE_AUTH_KEY}"

echo ""
echo "Tailscale conectado."
echo "IP deste servidor na rede Tailscale:"
tailscale ip -4
echo ""
echo "Status:"
tailscale status
echo ""
echo "Próximo passo: em conexao.conf use o IP Tailscale do SQL Server em SERVIDOR=..."
echo "Teste porta SQL:"
echo "  nc -zv IP_TAILSCALE_DO_SQL 1433"