# Deploy: impostor

Fork of [Impostor](https://github.com/Impostor/Impostor) (GPL-3.0), the open-source
Among Us server, kept so OpenPak builds from a tag it controls. The
[`among-us`](https://github.com/openpak/among-us) image builds Impostor from this repository at
the `openpak-v*` tag named by its `IMPOSTOR_REF` build argument and runs it beside the OpenPak
front in one container. One source change (the rewards session reporter, `OPENPAK.md`); the GPL work stays its own process. To take a new
upstream release: merge it, tag `openpak-v<version>`, bump `IMPOSTOR_REF` in among-us.

## Region nodes

A federation node runs the same `among-us` image with no code change: set
`IMPOSTOR_Server__PublicIp` to the node's public IPv4 (keep `PublicPort`/`ListenPort` 22603),
leave `OPENPAK_ROUTING_URL` unset, and announce the node's `impostor` role as `ip:22603`. Home's
front sends hosts there and answers joins for the codes the node reports. Not done yet: the
node-side room reporter that posts each game's code to home's `POST /internal/v1/amongus/rooms`
(likely a small plugin on `GameCreatedEvent`/`GameDestroyedEvent`); until it exists a node's
rooms can be hosted but not joined by code.

## Rewards sessions (environment)

| Variable | Meaning |
| --- | --- |
| `REWARDS_EVENTS_URL` | rewards base URL (bridge `http://openpak-rewards:20180`; host network `http://127.0.0.1:20180`) |
| `REWARDS_EVENTS_KEY` | rewards events key |
| `EOS_INTERNAL_URL` | eosd health listener, `http://openpak-eos:22621` |
| `EOS_INTERNAL_KEY` | the key eosd was given for `/internal/v1/puid/<puid>` |
| `IMPOSTOR_DATABASE_URL` | Postgres (`postgres://impostor:...@openpak-postgres:5432/impostor?sslmode=disable`): session clock, PUID map, outbox. Pool of 2. Unset: memory only (development) |
| `REWARDS_SESSION_MIN` | optional, default 3 minutes (.NET TimeSpan format, for example `00:03:00`) |

Any of the four unset: the reporter stays idle and logs "Rewards events are off".
