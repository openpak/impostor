# Changelog — impostor (OpenPak fork)

## openpak-v1.10.9 — 2026-11-03

- Rewards sessions: `RewardsService` reports `game_sessions.among-us` to OpenPak rewards (a player
  counts once per room after 3 minutes with another player). The EOS product user id each client
  reports in its PlayerInfo is kept (`InnerPlayerInfo.ProductUserId`) and mapped to a BAAS account
  through eosd `/internal/v1/puid/<puid>`. Idle unless `REWARDS_EVENTS_URL/KEY` and
  `EOS_INTERNAL_URL/KEY` are set. See OPENPAK.md.

## openpak-v1.10.8

- Upstream v1.10.8, no changes.
