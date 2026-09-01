# Embedded win-acme distribution

The build places the win-acme release archive here as `wacs.zip`. It is
downloaded by the Makefile (`make deps`) and is **not** committed to git,
because it is ~50 MB of third-party binaries.

This README exists so that the `//go:embed dist` directive in `../embed.go`
always has at least one file to match. Without it, a fresh clone would fail
to build until `make deps` had been run.

A binary built without `wacs.zip` present still compiles and runs, but reports
a clear error at startup instead of silently misbehaving. Run `make deps`
before `make build` to produce a fully self-contained executable.
