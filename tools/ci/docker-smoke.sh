#!/usr/bin/env bash
# Collaudo dell'immagine Docker (wave 3): installazione da zero, registrazione vera da un HLS simulato,
# ricreazione del container sugli stessi volumi (come un aggiornamento), backup, controllo di salute, utente.
#   bash tools/ci/docker-smoke.sh <immagine>
set -euo pipefail
IMAGE="${1:?immagine}"
W="$(mktemp -d)"
API="http://127.0.0.1:8765/api/v1"
GEN=""; WEB=""

cleanup() {
  status=$?
  if [ $status -ne 0 ]; then
    echo "::group::log del container"; docker logs dlf 2>&1 | tail -100 || true; echo "::endgroup::"
    echo "::group::log delle registrazioni"; sudo find "$W/data/logs" -type f -exec sh -c 'echo "--- $1"; cat "$1"' _ {} \; 2>/dev/null | tail -100 || true; echo "::endgroup::"
  fi
  docker rm -f dlf >/dev/null 2>&1 || true
  [ -n "$GEN" ] && kill "$GEN" 2>/dev/null || true
  [ -n "$WEB" ] && kill "$WEB" 2>/dev/null || true
  exit $status
}
trap cleanup EXIT

step() { echo; echo "== $*"; }
fail() { echo "ERRORE: $*"; exit 1; }

step "HLS live simulato sulla macchina"
mkdir -p "$W/hls" "$W/data" "$W/rec"
chmod 777 "$W/data" "$W/rec"          # il container gira come utente app (uid 1654)
(cd "$W/hls" && ffmpeg -hide_banner -nostdin -loglevel error -re \
  -f lavfi -i testsrc2=size=320x180:rate=25 -f lavfi -i sine=frequency=440:sample_rate=48000 \
  -c:v libx264 -preset ultrafast -tune zerolatency -g 25 -b:v 600k -c:a aac -b:a 96k \
  -f hls -hls_time 1 -hls_list_size 6 -hls_flags delete_segments+omit_endlist+independent_segments live.m3u8) &
GEN=$!
python3 -m http.server 8799 --bind 127.0.0.1 --directory "$W/hls" >/dev/null 2>&1 &
WEB=$!
for _ in $(seq 1 30); do
  [ -f "$W/hls/live.m3u8" ] && [ "$(grep -c '\.ts' "$W/hls/live.m3u8")" -ge 3 ] && break
  sleep 1
done
cat > "$W/data/prova.m3u" <<M3U
#EXTM3U
#EXTINF:-1 tvg-id="Prova.it" group-title="Test",Canale di prova
http://127.0.0.1:8799/live.m3u8
M3U

run_container() {
  docker run -d --name dlf --network host -v "$W/data:/data" -v "$W/rec:/recordings" "$IMAGE" >/dev/null
  for _ in $(seq 1 60); do
    curl -fsS "$API/health" >/dev/null 2>&1 && return 0
    sleep 1
  done
  fail "il server non risponde"
}

step "Installazione da zero"
run_container
curl -fsS "$API/health" | tee /dev/stderr | jq -e '.status == "ok" and .api == "v1"' >/dev/null
[ "$(docker exec dlf id -u)" = "1654" ] || fail "il container non gira come utente app"
docker exec dlf ffmpeg -version | head -1

step "Playlist e canali"
curl -fsS -X POST "$API/sources" -H 'Content-Type: application/json' \
  -d '{"name":"Prova","url":"/data/prova.m3u"}' | tee /dev/stderr | jq -e '.channels == 1' >/dev/null
CH=$(curl -fsS "$API/channels" | jq '.[0].id')

step "Registrazione di 15 secondi"
START=$(date -u +%Y-%m-%dT%H:%M:%SZ)
STOP=$(date -u -d '+15 seconds' +%Y-%m-%dT%H:%M:%SZ)
ID=$(curl -fsS -X POST "$API/recordings" -H 'Content-Type: application/json' \
  -d "{\"channelId\":$CH,\"title\":\"Prova Docker\",\"start\":\"$START\",\"stop\":\"$STOP\",\"beforeMin\":0,\"afterMin\":0,\"format\":\"mp4\"}" | jq '.id')
STATE=""
for _ in $(seq 1 120); do
  STATE=$(curl -fsS "$API/recordings/$ID" | jq -r '.recording.state')
  case "$STATE" in scheduled|recording|merging) sleep 1 ;; *) break ;; esac
done
curl -fsS "$API/recordings/$ID" | jq '.recording | {state, file, lostMinutes}'
[ "$STATE" = "completed" ] || fail "stato $STATE"
FILE=$(curl -fsS "$API/recordings/$ID" | jq -r '.recording.file')
HOSTFILE="$W/rec/${FILE#/recordings/}"
[ -f "$HOSTFILE" ] || fail "file $HOSTFILE mancante"
DUR=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$HOSTFILE")
echo "durata $DUR s"
awk -v d="$DUR" 'BEGIN { exit !(d >= 8 && d <= 40) }' || fail "durata $DUR fuori misura"

step "Container ricreato sugli stessi volumi (come un aggiornamento)"
docker rm -f dlf >/dev/null
run_container
[ "$(curl -fsS "$API/sources" | jq length)" = "1" ] || fail "playlist persa"
[ "$(curl -fsS "$API/recordings/$ID" | jq -r '.recording.state')" = "completed" ] || fail "registrazione persa"

step "Backup"
curl -fsS -X POST "$API/backups" | tee /dev/stderr | jq -e '.bytes > 0' >/dev/null
ls -l "$W/data/backup"

step "Controllo di salute di Docker"
for _ in $(seq 1 90); do
  H=$(docker inspect -f '{{.State.Health.Status}}' dlf)
  [ "$H" = "healthy" ] && break
  sleep 1
done
[ "$H" = "healthy" ] || fail "salute: $H"

echo; echo "Collaudo del container riuscito"
