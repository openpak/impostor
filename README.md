# Impostor

OpenPak's Impostor.

| | |
| --- | --- |
| Network | Among Us |
| Role | Among Us private server (C#), unmodified upstream Impostor |
| Used by | [`among-us`](https://github.com/openpak/among-us), which runs it beside the OpenPak front |
| Based on | [Impostor](https://github.com/Impostor/Impostor) (GPL-3.0) |
| Licence | GPL-3.0 |

## What it serves

| Service | What |
| --- | --- |
| HTTP (TCP 22023) | Matchmaking; points Among Us at the UDP service |
| UDP (22023) | Game traffic |

## Run

Set `PublicIp` in `config.json` to the address players connect to, then run:

```sh
./Impostor.Server
```

## Docs

- [Protocol](docs/protocol.md)
- [Evidence](docs/evidence.md)
- [Deploy](docs/deploy.md)
- [Notes](docs/notes.md)
- [Upstream README](docs/upstream.md)
- [Documentation index](docs/README.md)
- [Running the server](docs/Running-the-server.md)
- [Server configuration](docs/Server-configuration.md)
- [Building from source](docs/Building-from-source.md)
- [Writing a plugin](docs/Writing-a-plugin.md)
- [HTTP server](docs/Http-server.md)
- [FAQ](docs/FAQ.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Upgrading](docs/Upgrading.md)

## Licence

GPL-3.0, the upstream Impostor licence, kept for this fork. See [LICENSE](LICENSE).
