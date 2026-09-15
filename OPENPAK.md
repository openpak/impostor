# Impostor for OpenPak

Fork of upstream Impostor (the Among Us private server, C#, GPL-3.0) with **no code changes**.
It exists so OpenPak builds from a tag it controls: the `among-us` server image clones this
repository at the `IMPOSTOR_REF` build argument (default `openpak-v1.10.8`, currently
upstream v1.10.8) and builds Impostor at image-build time. The fork publishes no image of its
own — the earlier `ghcr.io/openpak/impostor` release workflow was removed on 2026-09-11.
Keeping GPL-3.0 at arm's length from AGPL-3.0-only OpenPak code is the point: sharing a
container is aggregation.

To take an upstream release: merge it, tag `openpak-v<version>`, bump `IMPOSTOR_REF` in
`among-us`. The server runs beside the Among Us front that logs and proxies the matchmaker
calls, on port 22601 (ports 22600–22609 claimed in the workspace `ports.md`).
