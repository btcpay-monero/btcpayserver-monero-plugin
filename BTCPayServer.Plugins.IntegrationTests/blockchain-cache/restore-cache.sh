#!/bin/sh
# Entrypoint wrapper for the monerod services: seeds the testnet LMDB from the cached,
# pre-mined chain (see README.md) before handing over to the image's own entrypoint.
# Does nothing if there is no cache file or the data dir already has a chain.
set -e

CACHE_FILE=/blockchain-cache/testnet-data.mdb.gz
LMDB_DIR="${MONERO_DATA:-/data}/testnet/lmdb"

if [ -f "$CACHE_FILE" ] && [ ! -f "$LMDB_DIR/data.mdb" ]; then
    echo "Restoring cached Monero testnet blockchain from $CACHE_FILE"
    mkdir -p "$LMDB_DIR"
    gunzip -c "$CACHE_FILE" > "$LMDB_DIR/data.mdb"
fi

exec /entrypoint.sh "$@"
