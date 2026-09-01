# win-acme GUI — implementation plan

## Context

`clutchlabsapp/win-acme-gui` is being restarted from an empty orphan branch
(`claude/simplegui`). The goal is a small Windows desktop tool that makes it easy to
set up and confirm win-acme renewals for a handful of SSL certificates whose domains
all live on the same name server.

win-acme's own interactive console is fine but hard to get right unattended, and there
is no quick way to see "is this actually going to renew?". This tool is a thin,
opinionated front end: it collects the handful of fields that matter, builds the
`wacs.exe` argument list, runs it, and shows the result.

The tool does **not** re-implement ACME. Everything it does is expressed as `wacs.exe`
arguments, taken verbatim from https://www.win-acme.com/reference/cli.

Decisions already made with the user:
- The GUI **runs `wacs.exe` directly** and streams its output into a log pane.
- Phase 1 DNS validation plugins: **Cloudflare, Route53, Azure DNS**. Namecheap is
  dropped (win-acme has no built-in Namecheap plugin).
- Verification is via a **GitHub Actions `windows-latest` build + test**, since .NET
  cannot be built in this Linux container.

## Stack

- **.NET 8**, WinForms. Lightest native option, no extra runtime, single-file publish.
- Three projects so the logic is testable off-Windows:

| Project | TFM | Purpose |
|---|---|---|
| `src/WinAcmeGui.Core` | `net8.0` | Models, plugin catalog, argument builder, `--list` parsing, process runner abstraction. No Windows/UI dependencies. |
| `src/WinAcmeGui` | `net8.0-windows` | WinForms UI. Thin — reads/writes the Core models. |
| `tests/WinAcmeGui.Core.Tests` | `net8.0` | xUnit tests over the argument builder and parsers. |

`WinAcmeGui.sln` at the root ties them together.

## Phase 1 — certificates and validation

### `WinAcmeGui.Core` model

- `CertificateRequest` — friendly name, common name, host names (list), notes.
- `AcmeAccount` — email address, accept-ToS flag, optional account name, test-mode flag.
- `ValidationSettings` — chosen plugin id + a `Dictionary<string,string>` of its
  parameter values.
- `PluginCatalog` — static, declarative description of the supported validation
  plugins so the UI can render fields generically and the builder can emit args:

  | Plugin id | `--validation` | Fields (CLI flags) |
  |---|---|---|
  | `cloudflare` | `cloudflare` | `--cloudflareapitoken` (secret) |
  | `route53` | `route53` | `--route53iamrole` OR `--route53accesskeyid` + `--route53secretaccesskey` (secret) |
  | `azure` | `azure` | `--azuresubscriptionid`, `--azureresourcegroupname`, `--azurehostedzone` (optional), `--azuretenantid`, `--azureclientid`, `--azuresecret` (secret), `--azureusemsi` (bool) |

  Each field carries: flag, label, required-ness, secret-ness, and an optional
  validator. Adding a plugin later = one entry in this table, no UI code.

- `WacsArgumentBuilder` — the heart of the tool. Turns the models into an ordered
  `IReadOnlyList<string>` of arguments. Rules:
  - Always `--source manual --host <comma-list>`, plus `--commonname` when set.
  - `--friendlyname`, `--accepttos`, `--emailaddress`.
  - `--validationmode dns-01 --validation <id>` plus the plugin's fields.
  - `--store certificatestore` by default (phase 1 exposes `--certificatestore` name
    and nothing else).
  - `--test` when test mode is on; `--closeonfinish` and `--verbose` always, so the
    process exits and the log is useful.
  - Never emits a flag whose value is empty.
  - Returns args as a **list**, never a joined string — `Process.Start` gets
    `ArgumentList` so quoting is the runtime's problem, not ours.
  - A separate `ToDisplayString(redactSecrets: true)` renders the command for the
    preview pane with secret values shown as `********`.

- `WacsRunner` — wraps `Process` with async stdout/stderr streaming, cancellation, and
  exit-code capture. Behind an `IWacsRunner` interface so tests use a fake.

- `RenewalListParser` — parses `wacs.exe --list` output into
  `RenewalSummary { Id, FriendlyName, Hosts, ExpiryDate, DueDate, IsDue, LastError }`.
  Defensive: unknown lines are skipped, not thrown on.

- `WacsLocator` — finds `wacs.exe`: last-used path from settings, then
  `%ProgramFiles%\win-acme\wacs.exe`, `%ProgramData%\win-acme`, `C:\win-acme`, then
  `PATH`. Falls back to a Browse dialog.

### UI (`MainForm`, single window)

Stacked group boxes, top to bottom, plus a log pane. No wizard, no tabs-within-tabs.

1. **win-acme** — detected `wacs.exe` path + Browse, and the detected version.
2. **Account** — email, "I accept the ACME terms of service" checkbox, "Use Let's
   Encrypt staging (test)" checkbox.
3. **Certificate** — friendly name, common name, host names (multiline, one per line;
   also accepts commas). Live validation: at least one host, each a syntactically
   plausible DNS name, common name must be one of the hosts.
4. **Validation** — plugin dropdown; below it a panel rebuilt from `PluginCatalog`
   for the selected plugin. Secret fields use `UseSystemPasswordChar`.
5. **Command preview** — read-only textbox showing the redacted command line, updated
   as fields change. Copy button copies the redacted form; a "Copy with secrets"
   menu item copies the real one.
6. **Actions** — `Create renewal`, `Check setup`, `Renew now`, `Cancel`.
7. **Log** — read-only monospace textbox, auto-scrolling, bounded to ~5000 lines.

### "Check setup" — the point of the tool

One button that answers "will this actually renew?". It runs, in order, and reports
each as pass / warn / fail in the log:

1. `wacs.exe --version` — win-acme is present and runnable.
2. Process is elevated (`WindowsPrincipal.IsInRole(Administrator)`) — win-acme needs it.
3. `wacs.exe --list` — parsed into a grid: renewal id, hosts, expiry, due date, last error.
4. The **scheduled task** exists and is enabled — read via `TaskService` /
   `schtasks /query`, looking for the `win-acme renew (*)` task. Without it nothing
   renews, and this is the most common silent failure.
5. Optional dry run: `wacs.exe --test --renew --force --id <id>` against staging.

Steps 2 and 4 are Windows-only and live in the WinForms project behind small
interfaces; Core stays portable.

### Secrets

API tokens are **never written to the GUI's own settings file**. win-acme stores them
in its renewal record (subject to its own `EncryptConfig`), which is the right place.
The GUI keeps them in memory only for the run. Non-secret fields (wacs path, email,
last-used plugin, host names) are persisted to
`%AppData%\win-acme-gui\settings.json`.

Known caveat, to be stated in the README: win-acme takes credentials as command-line
arguments, so they are briefly visible to other processes on the machine while
`wacs.exe` runs. This is inherent to win-acme's unattended interface, not something
the GUI introduces.

## Phase 2 — post-renewal hooks

Adds an **Installation** group box to the same window. Model: `InstallationStep` list,
appended to `--installation` as a comma-separated list, with the flags for each.

Presets:

| Preset | Emitted arguments |
|---|---|
| None | `--installation none` |
| IIS | `--installation iis`, plus optional `--installationsiteid`, `--sslport`, `--sslipaddress` |
| RDP / RD Listener | `--installation script --script "<wacsDir>\Scripts\ImportRDListener.ps1" --scriptparameters "{CertThumbprint}"` and forces `--certificatestore My` (the script looks in `LocalMachine\My`) |
| RD Gateway | same, `ImportRDGateway.ps1` |
| RDS (both) | same, `ImportRDS.ps1` |
| Exchange | same, `ImportExchange.ps1` |
| Custom script | free-text `--script` (Browse) and `--scriptparameters`, with a token cheat-sheet next to the field |

The bundled-script presets resolve their path relative to the located `wacs.exe`, and
the UI shows a warning if the `.ps1` is not actually there. The token cheat-sheet lists
the substitutions from the win-acme docs: `{CertCommonName}`, `{CertThumbprint}`,
`{CertFriendlyName}`, `{CacheFile}`, `{CachePassword}`, `{CacheFolder}`, `{StorePath}`,
`{StoreType}`, `{RenewalId}`, and the `{OldCert*}` variants.

Multiple steps are allowed (e.g. IIS + RDP) and are emitted in list order.

## Files to create

```
WinAcmeGui.sln
PLAN.md                                  # this plan, committed to the repo as asked
README.md
.gitignore                               # standard VisualStudio .gitignore
.github/workflows/build.yml              # windows-latest: dotnet build + dotnet test
src/WinAcmeGui.Core/WinAcmeGui.Core.csproj
src/WinAcmeGui.Core/Models/*.cs          # CertificateRequest, AcmeAccount, ValidationSettings, InstallationStep, RenewalSummary
src/WinAcmeGui.Core/Plugins/PluginCatalog.cs
src/WinAcmeGui.Core/WacsArgumentBuilder.cs
src/WinAcmeGui.Core/RenewalListParser.cs
src/WinAcmeGui.Core/WacsRunner.cs        # + IWacsRunner
src/WinAcmeGui.Core/WacsLocator.cs
src/WinAcmeGui/WinAcmeGui.csproj
src/WinAcmeGui/Program.cs
src/WinAcmeGui/MainForm.cs               # code-only layout, no .Designer.cs — easier to review in a diff
src/WinAcmeGui/PluginFieldPanel.cs       # renders PluginCatalog fields
src/WinAcmeGui/SetupChecker.cs           # elevation + scheduled task checks
src/WinAcmeGui/AppSettings.cs
tests/WinAcmeGui.Core.Tests/WinAcmeGui.Core.Tests.csproj
tests/WinAcmeGui.Core.Tests/WacsArgumentBuilderTests.cs
tests/WinAcmeGui.Core.Tests/RenewalListParserTests.cs
```

## Build order

1. Repo scaffolding: `.gitignore`, solution, three projects, CI workflow. Push and
   confirm CI is green on an empty-but-compiling tree before writing features.
2. `WinAcmeGui.Core` models + `PluginCatalog` + `WacsArgumentBuilder`, with tests.
3. `RenewalListParser`, `WacsLocator`, `WacsRunner`, with tests for the parser.
4. `MainForm` and `PluginFieldPanel` — phase 1 UI wired to the builder and runner.
5. `SetupChecker` and the Check setup button.
6. Phase 2: installation presets in Core + the Installation group box in the UI.
7. `README.md` with screenshots-free usage notes and the secrets caveat.

Each step is its own commit on `claude/simplegui`.

## Verification

- **CI**: `.github/workflows/build.yml` runs `dotnet build -c Release` and
  `dotnet test` on `windows-latest` for every push. This is the compile proof — I read
  the run logs and fix failures until green. Cost: the repo is private on a personal
  account, so ~8 quota minutes per push (Windows bills at 2x) against a 2,000-minute
  monthly allowance — roughly 250 runs/month, and the default $0 spending limit means
  it stops rather than bills if ever exhausted.
- **Release**: a tag-triggered job runs `dotnet publish -r win-x64 --self-contained
  -p:PublishSingleFile=true` and attaches the resulting `.exe` to a GitHub release, so
  you never need a .NET SDK on your Windows machine. The exe is unsigned, so first run
  shows a SmartScreen prompt once — noted in the README.
- **Unit tests** cover the parts that are easy to get wrong:
  - argument builder emits exactly the expected sequence for each of the three DNS
    plugins, for the phase 2 installation presets, and for test mode;
  - empty/whitespace fields never produce a dangling flag;
  - host list accepts newline-, comma- and space-separated input and de-duplicates;
  - common name not in the host list is rejected;
  - `ToDisplayString` redacts every field marked secret;
  - `RenewalListParser` handles real `--list` output, a zero-renewal machine, and
    garbage lines.
- **Manual smoke test on your Windows box** (the parts CI cannot reach): point the
  tool at a real `wacs.exe`, tick test mode, create a renewal against Let's Encrypt
  staging for one domain, then press Check setup and confirm the renewal and the
  scheduled task both show up.
