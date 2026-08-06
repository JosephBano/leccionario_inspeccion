#!/usr/bin/env bash
# =============================================================================
# Activa los hooks versionados del repositorio.
# Correr UNA VEZ por cada clon:   ./scripts/setup-hooks.sh
#
# Usa core.hooksPath en vez de copiar a .git/hooks: así los hooks quedan bajo
# control de versiones y se actualizan con un git pull.
# =============================================================================
set -euo pipefail

GRN='\033[0;32m'; YEL='\033[1;33m'; NC='\033[0m'
raiz=$(git rev-parse --show-toplevel)

cd "$raiz"
chmod +x .githooks/*
git config core.hooksPath .githooks

echo -e "${GRN}✔ Hooks activados${NC} (core.hooksPath = .githooks)"
echo ""
echo "  pre-commit  → bloquea commits en main/develop, secretos, migraciones sin rollback"
echo "  commit-msg  → exige Conventional Commits"
echo "  pre-push    → bloquea push a main/develop y push con tests en rojo"
echo ""
echo -e "${YEL}Recordatorio:${NC} son una red de seguridad local; se saltan con --no-verify."
echo "La protección real se configura en el servidor. Ver docs/08-git-flow.md § Nivel 2."
