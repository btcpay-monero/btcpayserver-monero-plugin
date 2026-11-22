#!/usr/bin/env bash
# Mines a Monero testnet chain once and snapshots monerod's LMDB to testnet-data.mdb.gz,
# so the integration tests start from an already mined chain instead of mining it every run.
# Requires Docker. See README.md.
set -euo pipefail

cd "$(dirname "$0")"

IMAGE="${MONERO_IMAGE:-$(grep -m1 -oE 'btcpayserver/monero:[^[:space:]"]+' ../docker-compose.yml)}"
TARGET_HEIGHT="${TARGET_HEIGHT:-80}"
THREADS="${THREADS:-$(nproc 2>/dev/null || echo 2)}"
# Primary address of the cash cow wallet seeded in Monero/MiningFixture.cs
MINING_ADDRESS="9zUKfjbRQQyXtbmvBkvHYMZ5eZBnZP7BeUatxfyPKvQcE7L328PQM2v17e6XApTHzeFmrfypgbDSdVFGbB5xuapQEQU4ufE"
OUTPUT_FILE=testnet-data.mdb.gz
NAME="monero-chain-cache-$$"

cleanup() {
    docker rm -f "$NAME" >/dev/null 2>&1 || true
    docker volume rm -f "$NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

rpc() {
    docker exec "$NAME" curl -sf "http://127.0.0.1:28081/$1" -H 'Content-Type: application/json' -d "${2:-{\}}"
}

height() {
    rpc get_height | grep -oE '"height": *[0-9]+' | grep -oE '[0-9]+$'
}

echo "Mining $IMAGE testnet chain to height $TARGET_HEIGHT with $THREADS thread(s)"
docker volume create "$NAME" >/dev/null
docker run -d --name "$NAME" -v "$NAME:/data" "$IMAGE" \
    monerod --testnet --fixed-difficulty=150 --offline --no-igd --non-interactive \
    --rpc-bind-ip=127.0.0.1 --rpc-bind-port=28081 >/dev/null

for _ in $(seq 120); do
    height >/dev/null 2>&1 && break
    sleep 1
done
height >/dev/null || { docker logs "$NAME"; echo "monerod RPC did not come up" >&2; exit 1; }

rpc start_mining "{\"miner_address\":\"$MINING_ADDRESS\",\"threads_count\":$THREADS,\"do_background_mining\":false,\"ignore_battery\":true}" >/dev/null
while true; do
    current="$(height)"
    echo "Height: $current/$TARGET_HEIGHT"
    [ "$current" -ge "$TARGET_HEIGHT" ] && break
    sleep 2
done
rpc stop_mining >/dev/null

# Stop monerod cleanly so the LMDB is flushed before it is copied.
rpc stop_daemon >/dev/null
docker wait "$NAME" >/dev/null

docker run --rm --entrypoint sh -v "$NAME:/data" "$IMAGE" -c 'gzip -9 -c /data/testnet/lmdb/data.mdb' > "$OUTPUT_FILE"
echo "Wrote $(pwd)/$OUTPUT_FILE ($(du -h "$OUTPUT_FILE" | cut -f1))"
