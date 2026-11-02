# Deploy: impostor

Unmodified fork of [Impostor](https://github.com/Impostor/Impostor) (GPL-3.0), the open-source
Among Us server, kept so OpenPak builds from a tag it controls. The
[`among-us`](https://github.com/openpak/among-us) image builds Impostor from this repository at
the `openpak-v*` tag named by its `IMPOSTOR_REF` build argument and runs it beside the OpenPak
front in one container. No source changes; the GPL work stays its own process. To take a new
upstream release: merge it, tag `openpak-v<version>`, bump `IMPOSTOR_REF` in among-us.

## Region nodes

A federation node runs the same `among-us` image with no code change: set
`IMPOSTOR_Server__PublicIp` to the node's public IPv4 (keep `PublicPort`/`ListenPort` 22603),
leave `OPENPAK_ROUTING_URL` unset, and announce the node's `impostor` role as `ip:22603`. Home's
front sends hosts there and answers joins for the codes the node reports. Not done yet: the
node-side room reporter that posts each game's code to home's `POST /internal/v1/amongus/rooms`
(likely a small plugin on `GameCreatedEvent`/`GameDestroyedEvent`); until it exists a node's
rooms can be hosted but not joined by code.
