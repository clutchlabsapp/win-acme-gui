# win-acme-gui

A native Windows desktop front-end for [win-acme](https://www.win-acme.com/),
the ACME client for Let's Encrypt and other certificate authorities.

win-acme is excellent but drives entirely from a console menu or command-line
flags. This wraps it in a normal Windows GUI, and **bundles win-acme itself**,
so deploying it means copying one `.exe` onto the server. Nothing to install,
no .NET prerequisite to chase, no `PATH` to configure.

## Features

| Tab | What it does |
|-----|--------------|
| **Dashboard** | Certificate counts, how many are due or expired, win-acme version, one-click "renew everything due" |
| **New Certificate** | Guided form: domains, validation, storage, installation, with fields that enable and disable to match the choices above them |
| **Renewals** | Sortable table of every managed renewal, colour-coded by status, with renew / cancel / revoke |
| **Settings** | ACME provider and contact address, renewal window, SMTP notifications, defaults |
| **Logs** | Timestamped, copyable view of everything win-acme printed |

Long-running operations execute on background goroutines and marshal results
back to the UI thread, so the window stays responsive while a certificate is
being issued.

## Building

Requires Go 1.24+. `curl`, `unzip` and `zip` are used to assemble the bundle.

```sh
make deps    # download win-acme + DNS plugins, repack as one archive
make build   # produce win-acme-gui.exe
```

`make all` runs both plus resource generation. `make build386` produces a
32-bit binary.

Windows binaries **cross-compile from Linux or macOS with no extra toolchain**.
Walk talks to Win32 through `lxn/win`, which is pure syscall bindings rather
than cgo, so `CGO_ENABLED=0 GOOS=windows go build` is all that is needed. No
mingw-w64.

### Application icon and manifest

```sh
make resources
```

This runs [`go-winres`](https://github.com/tc-hib/go-winres) over
`winres/winres.json` to emit a `.syso` that the Go linker picks up
automatically. It carries the version info and the application manifest.

The manifest matters: Walk requires **Common Controls v6**
(`"use-common-controls-v6": true`). Without it the app renders with
Windows-95-era controls or fails outright. `make resources` is optional for a
working build but strongly recommended for a shippable one.

Drop a `winres/icon.ico` in and add an `RT_GROUP_ICON` entry to
`winres.json` to give the executable an icon.

### What gets bundled

`make deps` downloads the `pluggable` win-acme build plus the DNS validation
plugins, unpacks them into one directory and rezips it to `embed/dist/wacs.zip`,
which `//go:embed` compiles into the binary.

The Cloudflare, Route53 and Azure providers are separate downloads. They are
*not* part of the base archive, and they only load in the `pluggable` build.
The list lives in `WACS_PLUGINS` in the Makefile and must stay in step with
`model.DNSValidationMethods()`, or the GUI will offer a provider whose plugin is
not present. (`acme-dns` is compiled into win-acme itself and needs no plugin.)

`embed/dist/wacs.zip` is not committed, being tens of MB of third-party
binaries. The build still compiles without it: the missing archive is
reported at startup with an actionable message rather than breaking
compilation, so `go build`, `go vet` and `go test` all work on a fresh clone.

The bundled version is pinned in two places, and `make check-version` fails the
build if they drift:

- `WACS_VERSION` in the `Makefile`
- `extractor.Version` in `internal/extractor/extractor.go`

Bumping `extractor.Version` is what invalidates an already-extracted copy on a
user's machine and triggers a re-extract at next launch.

## How it works

```
cmd/win-acme-gui   entry point: extract, wire up, run
internal/app       orchestrator; owns the window and cross-tab callbacks
internal/ui        one file per tab, plus the TableView model and dialogs
internal/service   WacsService: everything that shells out to wacs.exe
internal/model     Renewal, CertificateRequest, Settings
internal/extractor unpacks the embedded archive to %APPDATA%
internal/config    settings persistence
embed              go:embed of the win-acme archive
```

On first launch the embedded archive is extracted to
`%APPDATA%\win-acme-gui\wacs\`, guarded by a `.version` marker so it only
happens once per bundled version. Logs are written to
`%APPDATA%\win-acme-gui\win-acme-gui.log`, not the working directory, which a
GUI app cannot rely on being writable.

The renewals view reads win-acme's own `*.renewal.json` files rather than
scraping `--list` output. Those files are structured, stable, and the only
place the hostnames and renewal IDs appear at all; the text output is a
fallback for when they cannot be located.

## Testing

```sh
make test   # unit tests
make vet    # static analysis against the Windows target
```

`make test` covers argument construction, output parsing, the renewal-file
schema, and the extractor, including that a malicious archive cannot write
outside the destination directory ("zip slip"). It deliberately skips
`internal/ui` and `internal/app`: those import Walk, which has no non-Windows
build. `make vet` cross-checks them with `GOOS=windows`.

## Notes on driving wacs.exe

wacs.exe is an interactive console application, and several of its behaviours
are hostile to being driven from a GUI. These are handled, but they explain
some otherwise surprising code.

**The `--list` pager will hang you.** Output is paginated every
`UI.PageSize` entries (default **50**) with a blocking `Console.ReadKey` waiting
for the spacebar. With no console to type into, a user with 51 renewals would
hang forever. The extractor raises `UI.PageSize` in win-acme's `settings.json`
after unpacking, editing it through a generic map so the other defaults survive.

**Output contains ANSI escape codes.** win-acme selects an ANSI Serilog theme
whenever the OS major version is 10, which is both Windows 10 *and* 11, and
does not disable it when redirected. All captured output goes through
`StripANSI`.

**stdin is left empty** on every invocation, so a prompt fails fast rather than
blocking forever.

**`--version` is not a cheap probe.** It has no dedicated output; the version
comes from the startup banner, which is printed for every command and performs
a network connectivity check that can take up to 30 seconds (twice). Budget
timeouts accordingly.

**Never mix `--renew` with plugin arguments** (`--source`, `--store`,
`--validation`, `--installation`). win-acme rejects that combination outright.
`--id`, `--friendlyname` and `--force` are fine.

**Renewal IDs are 22-character ShortGuids**, not GUIDs: a base64url-encoded,
truncated GUID such as `hFhBaGbBLkiIY0RpVfPtDA`. Do not validate them as GUIDs.

## Status

Compiles and cross-compiles cleanly; `make test` passes. The CLI flag mapping,
the `--list` format and the renewal-file schema were verified against the
win-acme source at tag `v2.2.9.1701`.

What has **not** happened is an end-to-end run against a real `wacs.exe` on
Windows. The parser is covered by tests built from the verified output format,
but the exact spacing of a live run is worth confirming once. That risk is
contained: renewal data comes from the structured `*.renewal.json` files, and
the text parser only carries the fallback path.

Run a certificate creation against the Let's Encrypt **staging** environment
first ("Use Staging (Test Mode)" is on by default). Staging has far more
forgiving rate limits than production, which locks you out for a week after a
handful of failures.
