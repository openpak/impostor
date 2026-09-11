# OpenPak build of Impostor

Unmodified fork of [Impostor](https://github.com/Impostor/Impostor) (GPL-3.0), the open-source
Among Us server. OpenPak runs it as its own container (`ghcr.io/openpak/impostor`, built on
`openpak-v*` tags from upstream's Dockerfile) behind the redirect and TLS front in
[`servers/among-us`](https://github.com/openpak/among-us). No source changes; the GPL work stays
its own process, aggregated with the AGPL stack and nothing more.

---

# Impostor

[![Discord](https://img.shields.io/badge/Discord-chat-blue?style=flat-square)](https://discord.gg/Mk3w6Tb)
[![CI](https://github.com/Impostor/Impostor/workflows/CI/badge.svg)](https://github.com/Impostor/Impostor/actions)

Impostor is the first working **Among Us** private server, written in C#.

As Among Us is actively updated, you may want to use the latest build from the master branch if you want to play on the latest version of Among Us that supported by Impostor.

If you want to play on an older version of Among Us, you can download old releases from [here](https://github.com/Impostor/Impostor/releases/), however these versions are less supported.

## Features

- All Among Us features are implemented. It is a full replacement for the official server.
- Plugin support.
- Server-sided anticheat.

## Installation

### Client

If you just want to play on a server hosted by someone else, you need to configure your client to connect to an Impostor server. Please go to [this website](https://impostor.github.io/Impostor) to set up your client by following the instructions for your OS there.

### Server

See the [docs](docs/Running-the-server.md) for instructions on how to set it up.

## Troubleshooting

See [TROUBLESHOOTING](docs/TROUBLESHOOTING.md) to solve issues with the Impostor client or the server.

## Contributing

See [CONTRIBUTING](CONTRIBUTING.md).

## License

This software is distributed under the **GNU GPLv3** License.

## Credits

- [willardf/Hazel-Networking](https://github.com/willardf/Hazel-Networking)
