#!/usr/bin/env bash
# Levanta Relia en local: rail-proxy (4000) → backend (5080, con PostgreSQL en Docker) → frontend (4200).
# Ctrl+C detiene los tres; PostgreSQL queda corriendo (docker compose -f backend/docker-compose.yml down para pararlo).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"

# Mata a todos los hijos (dotnet, tsx, ng) al salir
trap 'trap - INT TERM EXIT; echo; echo "Deteniendo..."; kill 0 2>/dev/null' INT TERM EXIT

# Prefija cada línea del log con el nombre del servicio
run() {
  local name=$1 dir=$2; shift 2
  (cd "$dir" && "$@" 2>&1 | while IFS= read -r line; do printf '[%s] %s\n' "$name" "$line"; done) &
  LAST_PID=$!
}

# Espera a que la URL responda (cualquier status HTTP); corta si el proceso murió
wait_for() {
  local name=$1 url=$2 secs=${3:-90}
  for ((i = 0; i < secs; i++)); do
    curl -s -o /dev/null "$url" && { echo "✔ $name listo en $url"; return; }
    kill -0 "$LAST_PID" 2>/dev/null || { echo "✘ $name se detuvo (ver el log [$name] arriba)" >&2; exit 1; }
    sleep 1
  done
  echo "✘ $name no respondió en ${secs}s ($url)" >&2
  exit 1
}

need_deps() { [[ -d "$1/node_modules" ]] || (cd "$1" && npm install); }

# Requisitos antes de levantar nada
dotnet --list-sdks | grep -q '^10\.' || { echo "✘ Falta el SDK de .NET 10 (https://aka.ms/dotnet/download)." >&2; exit 1; }
docker info >/dev/null 2>&1 || { echo "✘ Docker no está corriendo: ábrelo y reintenta." >&2; exit 1; }

# 1. Rail proxy
if [[ -f "$ROOT/rail-proxy/.env" ]]; then
  need_deps "$ROOT/rail-proxy"
  run proxy "$ROOT/rail-proxy" npm run dev
  wait_for proxy http://localhost:4000/health
else
  echo "⚠ Falta rail-proxy/.env (ver rail-proxy/README.md): sigo sin proxy; los pagos responderán 503 rail_unavailable."
fi

# 2. Backend
(cd "$ROOT/backend" && docker compose up -d --wait)
run back "$ROOT/backend/API" dotnet run --launch-profile http
wait_for back http://localhost:5080/swagger 180

# 3. Frontend
need_deps "$ROOT/frontend/relia"
run front "$ROOT/frontend/relia" npm start
wait_for front http://localhost:4200

echo "Relia corriendo → http://localhost:4200 (Ctrl+C para detener)"
wait
