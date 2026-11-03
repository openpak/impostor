# Changelog — impostor (OpenPak fork)

## openpak-v1.10.11 — 2026-11-03

- One log line per request or message, to stdout (Serilog console, picked up from journald):
  `http <method> <path> -> <status> <ms>ms` for the HTTP API (`/healthz` skipped) and
  `cmd <MessageName> -> ok|error <ms>ms` per Hazel message, plus `connect id=` and `disconnect id=`
  lines. Never the query string, headers, bodies, nicknames or message bytes. The per-packet game-data
  relay (GameData, GameDataTo, PackedGameDataTo) is not logged. Tests in `RequestLogTests`.

## openpak-v1.10.10 — 2026-11-03

- Rewards state is durable: the session clock, the PUID -> BAAS map and counted sessions not yet
  accepted by rewards (outbox) live in Postgres (`IMPOSTOR_DATABASE_URL`; tables `impostor_*`), so a
  restart loses none. Database down: runs on memory, logs at ERROR level, retries every 30 s.
  Restart test against a real Postgres (`IMPOSTOR_TEST_DATABASE_URL`).

## openpak-v1.10.9 — 2026-11-03

- Rewards sessions: `RewardsService` reports `game_sessions.among-us` to OpenPak rewards (a player
  counts once per room after 3 minutes with another player). The EOS product user id each client
  reports in its PlayerInfo is kept (`InnerPlayerInfo.ProductUserId`) and mapped to a BAAS account
  through eosd `/internal/v1/puid/<puid>`. Idle unless `REWARDS_EVENTS_URL/KEY` and
  `EOS_INTERNAL_URL/KEY` are set. See OPENPAK.md.

## openpak-v1.10.8

- Upstream v1.10.8, no changes.
