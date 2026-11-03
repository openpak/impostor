# Impostor for OpenPak

Fork of upstream Impostor (the Among Us private server, C#, GPL-3.0) with one OpenPak change: the
rewards session reporter (`src/Impostor.Server/Rewards/`, below).
It exists so OpenPak builds from a tag it controls: the `among-us` server image clones this
repository at the `IMPOSTOR_REF` build argument (default `openpak-v1.10.9`, currently
upstream v1.10.8) and builds Impostor at image-build time. The fork publishes no image of its
own — the earlier `ghcr.io/openpak/impostor` release workflow was removed on 2026-09-11.
Keeping GPL-3.0 at arm's length from AGPL-3.0-only OpenPak code is the point: sharing a
container is aggregation.

To take an upstream release: merge it, tag `openpak-v<version>`, bump `IMPOSTOR_REF` in
`among-us`. The server runs beside the Among Us front that logs and proxies the matchmaker
calls, on port 22601 (ports 22600–22609 claimed in the workspace `ports.md`).

## Region nodes

A federation node runs the same `among-us` image with no code change: set
`IMPOSTOR_Server__PublicIp` to the node's public IPv4 (and keep `PublicPort`/`ListenPort` 22603),
leave `OPENPAK_ROUTING_URL` unset, and announce the node's `impostor` role as `ip:22603`. Home's
front sends hosts there and answers joins for the codes the node reports. Not done yet: the
node-side room reporter that posts each game's code to home's
`POST /internal/v1/amongus/rooms` (see `among-us/README.md`), likely a small plugin on
`GameCreatedEvent`/`GameDestroyedEvent`; until it exists a node's rooms can be hosted but not
joined by code.

## Rewards sessions

`RewardsService` (a hosted service) reports `game_sessions.among-us` to the OpenPak rewards service:
a player counts once per room after 3 continuous minutes in a room with at least one other mapped
player. Only what the server observed: every 20 s it reads each room's players and the EOS product user
id (PUID) each client put in its own PlayerInfo (`InnerPlayerInfo.ProductUserId`, which upstream read
and discarded), and asks eosd (`GET /internal/v1/puid/<puid>`) which BAAS account that PUID was
issued to. A PUID eosd never issued (another platform, or eosd restarted since the login) maps to
nobody and is left out, so it never counts and is never the "other player". The Hazel handshake carries
no account, so the PlayerInfo PUID is the only tie; it is an unguessable HMAC value, not a bare
claim a player can pick. Room ids are `<code>-<first seen unix seconds>`. Idle unless all four of
`REWARDS_EVENTS_URL`, `REWARDS_EVENTS_KEY`, `EOS_INTERNAL_URL`, `EOS_INTERNAL_KEY` are set.
Not yet confirmed against a real Switch room: the game connection had stopped at DTLS in the last
Harbor test, so the live check is that the log prints "Rewards events are on".
