#!/usr/bin/env bash
set -euo pipefail

# Development only. The application authenticates against odca.users.password_hash.

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BOOTSTRAP="$ROOT/src/Odca.Bootstrap/Odca.Bootstrap.csproj"
RUNTIME="$ROOT/src/Odca.Api/development-runtime.json"
ENV_FILE="$ROOT/database/development/local-access.env"

ADMIN_EMAIL="${ODCA_DEV_ADMIN_EMAIL:-admin@odca.local}"
ADMIN_PASSWORD="${ODCA_DEV_ADMIN_PASSWORD:-V7!qM2#rL9@xT4\$p}"
OPERATOR_EMAIL="${ODCA_DEV_OPERATOR_EMAIL:-operador@odca.local}"
OPERATOR_PASSWORD="${ODCA_DEV_OPERATOR_PASSWORD:-OdcaOperador#2026Local}"
CLIENT_EMAIL="${ODCA_DEV_CLIENT_EMAIL:-cliente.teste@odca.local}"
CLIENT_PASSWORD="${ODCA_DEV_CLIENT_PASSWORD:-K8@wR3!nF6#zP2\$m}"
APPLY_MIGRATIONS=0
REQUIRE_CHANGE=0

if [[ -f "$ENV_FILE" ]]; then
  # shellcheck disable=SC1090
  set -a
  source "$ENV_FILE"
  set +a
  ADMIN_EMAIL="${ODCA_DEV_ADMIN_EMAIL:-$ADMIN_EMAIL}"
  ADMIN_PASSWORD="${ODCA_DEV_ADMIN_PASSWORD:-$ADMIN_PASSWORD}"
  OPERATOR_EMAIL="${ODCA_DEV_OPERATOR_EMAIL:-$OPERATOR_EMAIL}"
  OPERATOR_PASSWORD="${ODCA_DEV_OPERATOR_PASSWORD:-$OPERATOR_PASSWORD}"
  CLIENT_EMAIL="${ODCA_DEV_CLIENT_EMAIL:-$CLIENT_EMAIL}"
  CLIENT_PASSWORD="${ODCA_DEV_CLIENT_PASSWORD:-$CLIENT_PASSWORD}"
fi

for arg in "$@"; do
  case "$arg" in
    --apply-migrations) APPLY_MIGRATIONS=1 ;;
    --require-initial-password-change) REQUIRE_CHANGE=1 ;;
  esac
done

if [[ "$ADMIN_EMAIL" != "admin@odca.local" ]]; then
  echo "A identidade reservada do superadministrador local e admin@odca.local." >&2
  exit 1
fi
if [[ "$OPERATOR_EMAIL" != "operador@odca.local" ]]; then
  echo "A identidade reservada do operador local e operador@odca.local." >&2
  exit 1
fi
if [[ "$CLIENT_EMAIL" != "cliente.teste@odca.local" ]]; then
  echo "A identidade reservada do cliente local e cliente.teste@odca.local." >&2
  exit 1
fi
if [[ ! -f "$BOOTSTRAP" || ! -f "$RUNTIME" ]]; then
  echo "Repositorio ou development-runtime.json ausente." >&2
  exit 1
fi

cd "$ROOT"
if [[ "$APPLY_MIGRATIONS" -eq 1 ]]; then
  dotnet run --project "$BOOTSTRAP" -- migrate
fi

ARGS=(
  run --project "$BOOTSTRAP" -- provision-test-access
  --environment Development
  --allow-postgres-development
  --administrator-password "$ADMIN_PASSWORD"
  --operator-password "$OPERATOR_PASSWORD"
  --client-password "$CLIENT_PASSWORD"
)
if [[ "$REQUIRE_CHANGE" -eq 0 ]]; then
  ARGS+=(--allow-immediate-login)
fi

dotnet "${ARGS[@]}"
dotnet run --project "$BOOTSTRAP" -- show-login

cat <<EOF

Credenciais de Development confirmadas no banco (nao estao no codigo da API):
  Superadministrador  $ADMIN_EMAIL
  Senha               $ADMIN_PASSWORD
  Operador            $OPERATOR_EMAIL
  Senha               $OPERATOR_PASSWORD
  Cliente demo        $CLIENT_EMAIL
  Senha               $CLIENT_PASSWORD
Web: https://localhost:7144/entrar
EOF
