# Deploy: impostor

Unmodified fork of [Impostor](https://github.com/Impostor/Impostor) (GPL-3.0), the open-source
Among Us server, kept so OpenPak builds from a tag it controls. The
[`among-us`](https://github.com/openpak/among-us) image builds Impostor from this repository at
the `openpak-v*` tag named by its `IMPOSTOR_REF` build argument and runs it beside the OpenPak
front in one container. No source changes; the GPL work stays its own process. To take a new
upstream release: merge it, tag `openpak-v<version>`, bump `IMPOSTOR_REF` in among-us.
