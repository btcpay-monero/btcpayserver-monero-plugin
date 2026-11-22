# Pre-mined blockchain cache

The integration tests need a funded cash cow wallet, so `MiningFixture` mines the local
testnet to height 71 before the first test runs. At `--fixed-difficulty=150` that is real
proof of work and costs several minutes on every CI run, but the result is the same every time.

Instead, CI mines the chain once with `generate-cache.sh`, keeps the gzipped monerod LMDB in
the GitHub Actions cache, and `restore-cache.sh` seeds it into `node_1` and `node_2` when they
start. monerod then boots at the cached height, the cash cow wallet (restored from its fixed
seed) already has unlocked coinbase outputs, and `MiningFixture` skips the initial mining.
The tests still mine the blocks they need for confirmations.

- `generate-cache.sh`: boots a throwaway offline monerod from the image in
  `docker-compose.yml`, mines to `TARGET_HEIGHT` (default 80) to the cash cow address,
  stops it cleanly and writes `testnet-data.mdb.gz`.
- `restore-cache.sh`: entrypoint wrapper for `node_1`/`node_2`. Restores the snapshot into
  `/data/testnet/lmdb/data.mdb` if the cache file exists and the data dir has no chain yet,
  then runs the image's entrypoint. Without a cache file it changes nothing.

The CI cache key contains the Monero image tag and the hash of `generate-cache.sh`, so the
cache is regenerated automatically when the Monero image is bumped (the LMDB format is tied
to the monerod build) or when the generation script changes.

To use the cache locally, run `./generate-cache.sh` once before `docker compose up`.
