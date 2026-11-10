#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  School Admission System — run everything WITHOUT Docker
#
#  Starts PostgreSQL (must already be installed), the .NET API and the Vite dev
#  server on this machine, then prints both URLs. Ctrl+C stops the API and UI.
#
#  Usage:
#     ./run-local.sh              start everything
#     ./run-local.sh --stop       stop what this script started
#     ./run-local.sh --skip-install   do not run dotnet restore / npm install
#
#  Everything can be overridden with environment variables:
#     DB_HOST=127.0.0.1 DB_PORT=5432 DB_NAME=school_admission \
#     DB_USER=school DB_PASSWORD=school \
#     BACKEND_PORT=8080 FRONTEND_PORT=5173 HOLD_DURATION_SECONDS=600 \
#     ./run-local.sh
#
#  Docker is NOT used here. The docker-compose.yml in this repo is what the
#  assignment asks for; this script is only a developer convenience.
# ---------------------------------------------------------------------------
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_DIR="$ROOT_DIR/.run"
mkdir -p "$RUN_DIR"

DB_HOST="${DB_HOST:-127.0.0.1}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-school_admission}"
DB_USER="${DB_USER:-school}"
DB_PASSWORD="${DB_PASSWORD:-school}"
BACKEND_PORT="${BACKEND_PORT:-8080}"
FRONTEND_PORT="${FRONTEND_PORT:-5173}"
HOLD_DURATION_SECONDS="${HOLD_DURATION_SECONDS:-600}"
SKIP_INSTALL=0

for arg in "$@"; do
  case "$arg" in
    --stop)          STOP_ONLY=1 ;;
    --skip-install)  SKIP_INSTALL=1 ;;
    -h|--help)       sed -n '2,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $arg (try --help)"; exit 1 ;;
  esac
done

# ------------------------------- pretty output ------------------------------
if [ -t 1 ]; then
  B=$'\033[1m'; G=$'\033[32m'; Y=$'\033[33m'; R=$'\033[31m'; C=$'\033[36m'; N=$'\033[0m'
else
  B=""; G=""; Y=""; R=""; C=""; N=""
fi
info()  { printf '%s==>%s %s\n' "$C" "$N" "$*"; }
ok()    { printf '%s ok %s %s\n' "$G" "$N" "$*"; }
warn()  { printf '%swarn%s %s\n' "$Y" "$N" "$*"; }
fail()  { printf '%sfail%s %s\n' "$R" "$N" "$*" >&2; exit 1; }

# ------------------------------- stop / cleanup -----------------------------
stop_previous() {
  for name in backend frontend; do
    local pidfile="$RUN_DIR/$name.pid"
    if [ -f "$pidfile" ]; then
      local pid; pid="$(cat "$pidfile" 2>/dev/null || true)"
      if [ -n "${pid:-}" ] && kill -0 "$pid" 2>/dev/null; then
        info "Stopping previous $name (pid $pid)"
        kill "$pid" 2>/dev/null || true
        wait "$pid" 2>/dev/null || true
      fi
      rm -f "$pidfile"
    fi
  done
}

if [ "${STOP_ONLY:-0}" = "1" ]; then
  stop_previous
  ok "Stopped."
  exit 0
fi

cleanup() {
  echo
  info "Shutting down…"
  stop_previous
  ok "Bye."
}
trap cleanup EXIT INT TERM

# ------------------------------- tool checks --------------------------------
find_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return; fi
  for candidate in "$HOME/.dotnet/dotnet" "$HOME/.local/dotnet/dotnet" /usr/local/share/dotnet/dotnet /usr/lib/dotnet/dotnet /opt/homebrew/bin/dotnet; do
    [ -x "$candidate" ] && { echo "$candidate"; return; }
  done
  echo ""
}

DOTNET="$(find_dotnet)"
[ -n "$DOTNET" ] || fail "The .NET 8 SDK was not found. Install it: https://dotnet.microsoft.com/download/dotnet/8.0"
NODE_OK=1; command -v npm >/dev/null 2>&1 || NODE_OK=0
[ "$NODE_OK" = "1" ] || fail "Node.js 18+ / npm was not found. Install it: https://nodejs.org"
command -v curl >/dev/null 2>&1 || fail "curl is required for the health checks."
command -v psql >/dev/null 2>&1 || fail "PostgreSQL client (psql) was not found.
      Install PostgreSQL 16+ and make sure 'psql' is on your PATH, e.g.
        Debian/Ubuntu : sudo apt install postgresql postgresql-contrib
        macOS (brew)  : brew install postgresql@16 && brew services start postgresql@16
        Windows       : https://www.postgresql.org/download/windows/  (add bin/ to PATH)
      Or use Docker instead:  docker compose up --build"

ok "$($DOTNET --version | tr -d '\n') SDK at $DOTNET"
ok "node $(node --version) / npm $(npm --version)"

# ------------------------------- PostgreSQL ---------------------------------
export PGPASSWORD="$DB_PASSWORD"

pg_ready() { pg_isready -h "$DB_HOST" -p "$DB_PORT" >/dev/null 2>&1; }

if pg_ready; then
  ok "PostgreSQL is running on $DB_HOST:$DB_PORT"
else
  warn "PostgreSQL is not answering on $DB_HOST:$DB_PORT — trying to start it…"
  if command -v systemctl >/dev/null 2>&1; then
    sudo systemctl start postgresql 2>/dev/null || true
  elif command -v service >/dev/null 2>&1; then
    sudo service postgresql start 2>/dev/null || true
  elif command -v brew >/dev/null 2>&1; then
    brew services start postgresql@16 >/dev/null 2>&1 || brew services start postgresql >/dev/null 2>&1 || true
  fi
  for _ in $(seq 1 20); do pg_ready && break; sleep 1; done
  pg_ready || fail "Could not reach PostgreSQL on $DB_HOST:$DB_PORT. Start it and try again (or set DB_HOST/DB_PORT)."
  ok "PostgreSQL started"
fi

can_connect() { psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "${1:-postgres}" -tAc "select 1" >/dev/null 2>&1; }

if can_connect postgres || can_connect "$DB_NAME"; then
  ok "Connected to PostgreSQL as '$DB_USER'"
else
  warn "Role '$DB_USER' cannot log in yet — creating role and database (needs local admin rights)…"
  if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null && command -v psql >/dev/null 2>&1; then
    sudo -u postgres psql -v ON_ERROR_STOP=1 -c \
      "DO \$\$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='$DB_USER') THEN
         CREATE ROLE \"$DB_USER\" LOGIN PASSWORD '$DB_PASSWORD' CREATEDB; END IF; END \$\$;" >/dev/null
  else
    psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -tAc "select 1" >/dev/null 2>&1 || fail \
      "Cannot log in as '$DB_USER'. Create the role once, then re-run:
         sudo -u postgres psql -c \"CREATE ROLE $DB_USER LOGIN PASSWORD '$DB_PASSWORD' CREATEDB;\"
       (or set DB_USER/DB_PASSWORD to an existing role)"
    psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -c \
      "DO \$\$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='$DB_USER') THEN
         CREATE ROLE \"$DB_USER\" LOGIN PASSWORD '$DB_PASSWORD' CREATEDB; END IF; END \$\$;" >/dev/null
  fi
  can_connect postgres || fail "Created role '$DB_USER' but still cannot log in. Check pg_hba.conf / the password."
  ok "Role '$DB_USER' ready"
fi

if can_connect "$DB_NAME"; then
  ok "Database '$DB_NAME' exists"
else
  info "Creating database '$DB_NAME'…"
  psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres -v ON_ERROR_STOP=1 \
    -c "CREATE DATABASE \"$DB_NAME\";" >/dev/null 2>&1 \
    || psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -c "CREATE DATABASE \"$DB_NAME\" OWNER \"$DB_USER\";" >/dev/null \
    || fail "Could not create database '$DB_NAME'. Create it manually and re-run."
  ok "Database '$DB_NAME' created"
fi

CONNECTION_STRING="Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD"

# ------------------------------- install ------------------------------------
if [ "$SKIP_INSTALL" = "0" ]; then
  info "Restoring .NET packages…"
  ( cd "$ROOT_DIR/backend" && "$DOTNET" restore src/SchoolAdmission.Api/SchoolAdmission.Api.csproj >/dev/null )
  ok "Backend packages restored"

  if [ ! -d "$ROOT_DIR/frontend/node_modules" ]; then
    info "Installing frontend dependencies (first run only, ~20s)…"
    ( cd "$ROOT_DIR/frontend" && npm install --no-audit --no-fund >/dev/null )
  fi
  ok "Frontend dependencies present"
else
  warn "Skipping restore/install (--skip-install)"
fi

# ------------------------------- already running? ---------------------------
if curl -fsS "http://localhost:$BACKEND_PORT/health" >/dev/null 2>&1; then
  fail "Port $BACKEND_PORT already answers /health — something is running there.
      Stop it (./run-local.sh --stop) or use BACKEND_PORT=8090 ./run-local.sh"
fi

# ------------------------------- backend ------------------------------------
info "Starting the API on http://localhost:$BACKEND_PORT …"
(
  cd "$ROOT_DIR/backend"
  ConnectionStrings__Default="$CONNECTION_STRING" \
  HOLD_DURATION_SECONDS="$HOLD_DURATION_SECONDS" \
  ASPNETCORE_URLS="http://0.0.0.0:$BACKEND_PORT" \
  ASPNETCORE_ENVIRONMENT=Development \
  "$DOTNET" run --no-launch-profile --project src/SchoolAdmission.Api/SchoolAdmission.Api.csproj \
    >"$RUN_DIR/backend.log" 2>&1 &
  echo $! >"$RUN_DIR/backend.pid"
)

for i in $(seq 1 90); do
  if curl -fsS "http://localhost:$BACKEND_PORT/health" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$(cat "$RUN_DIR/backend.pid")" 2>/dev/null; then
    tail -30 "$RUN_DIR/backend.log" >&2
    fail "The API stopped while starting up (log above)."
  fi
  sleep 1
done
curl -fsS "http://localhost:$BACKEND_PORT/health" >/dev/null 2>&1 || {
  tail -30 "$RUN_DIR/backend.log" >&2
  fail "The API did not become healthy in 90s (log above)."
}
ok "API healthy — hold window ${HOLD_DURATION_SECONDS}s"

# ------------------------------- frontend -----------------------------------
info "Starting the UI on http://localhost:$FRONTEND_PORT …"
(
  cd "$ROOT_DIR/frontend"
  VITE_PROXY_TARGET="http://localhost:$BACKEND_PORT" \
  npm run dev -- --host 0.0.0.0 --port "$FRONTEND_PORT" --strictPort \
    >"$RUN_DIR/frontend.log" 2>&1 &
  echo $! >"$RUN_DIR/frontend.pid"
)

for i in $(seq 1 60); do
  if curl -fsS "http://localhost:$FRONTEND_PORT/" >/dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS "http://localhost:$FRONTEND_PORT/" >/dev/null 2>&1 || {
  tail -30 "$RUN_DIR/frontend.log" >&2
  fail "The UI did not start (log above)."
}
ok "UI is up"

cat <<EOF

${B}School Admission System is running (no Docker)${N}

  ${B}UI${N}        http://localhost:$FRONTEND_PORT
  ${B}API${N}       http://localhost:$BACKEND_PORT     (health: /health)
  ${B}Database${N}  postgres://$DB_USER@$DB_HOST:$DB_PORT/$DB_NAME

  Logs      $RUN_DIR/backend.log  ·  $RUN_DIR/frontend.log
  Stop      Ctrl+C   (or ./run-local.sh --stop)

  Try it: create a class in the "School admin" panel, type any 17 digits as the
  birth registration number, press "Apply for a seat", then pay or cancel.

EOF

# Keep the script alive and mirror both logs until Ctrl+C.
tail -n 5 -f "$RUN_DIR/backend.log" "$RUN_DIR/frontend.log"
