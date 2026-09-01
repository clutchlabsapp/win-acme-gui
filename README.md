# win-acme GUI

A small Windows front end for [win-acme](https://www.win-acme.com/). It is for the
common case: a handful of certificates, all for domains on one name server, renewed
automatically and forgotten about.

It does not implement ACME. Everything it does is a `wacs.exe` command line, which you
can see in the window before anything runs.

## What it does

- Collects the fields that matter — host names, ACME account, DNS provider credentials —
  and builds the `wacs.exe` arguments for you.
- Shows the exact command that will run, with credentials hidden.
- Runs it and streams the output into the window.
- **Check setup** answers "will these actually renew?": win-acme present and runnable,
  process elevated, renewals registered, and the `win-acme renew` scheduled task present
  and enabled. That last one is the one people miss — without it nothing renews and
  nothing complains until a certificate expires.
- Sets up the post-renewal hooks: IIS bindings, the Remote Desktop and Exchange scripts
  that ship with win-acme, or a script of your own.

## Requirements

- Windows 10 / Server 2016 or later.
- [win-acme](https://www.win-acme.com/) unpacked somewhere. The tool looks in
  `C:\Program Files\win-acme`, `C:\ProgramData\win-acme`, `C:\win-acme`, `C:\tools\win-acme`
  and on `PATH`, and you can browse to it otherwise.
- Administrator rights. The tool asks for elevation on launch, because win-acme writes to
  the machine certificate store and the task scheduler.

## Getting it

Download the `.exe` from the latest release and run it. There is nothing to install and
no .NET runtime to add — it is published self-contained.

The executable is not code-signed, so the first run shows the SmartScreen
"Windows protected your PC" prompt. Choose **More info → Run anyway**.

## DNS validation

DNS validation (`dns-01`) is used throughout: it works for wildcards and needs no
inbound HTTP to your server. Supported providers:

| Provider | win-acme plugin | What you need |
|---|---|---|
| Cloudflare | `cloudflare` | An API token scoped to `Zone:DNS:Edit` — not the global API key |
| Amazon Route 53 | `route53` | The instance IAM role, or an access key ID and secret |
| Azure DNS | `azure` | Subscription and resource group, plus a managed identity or an app registration |

There is no built-in Namecheap plugin in win-acme. If your domains are there, either
delegate `_acme-challenge` with a CNAME to a provider above, or use a custom script.

## After renewal

| Preset | What runs |
|---|---|
| IIS | `--installation iis`, rebinding HTTPS to the new certificate |
| Remote Desktop | win-acme's `ImportRDListener.ps1`, `ImportRDGateway.ps1` or `ImportRDS.ps1` |
| Exchange | win-acme's `ImportExchange.ps1` — upstream marks this script incomplete, so test it |
| Custom script | Any `.bat`, `.ps1` or `.exe`, with the substitution tokens shown next to the field |

IIS and one script can be combined. The Remote Desktop and Exchange scripts only read
`LocalMachine\My`, so the tool sets the certificate store accordingly and warns if it
does not match.

## About your credentials

The tool never writes API tokens or passwords to its own settings file. win-acme keeps
credentials in its renewal record, subject to its own `EncryptConfig` setting, which is
the right place for them. Only the boring fields — wacs.exe path, email address, host
names, chosen provider — are remembered, in
`%AppData%\win-acme-gui\settings.json`.

One thing worth knowing: win-acme takes credentials as command line arguments, so while
`wacs.exe` is running they are briefly visible to other processes on the machine. That is
how win-acme's unattended interface works and is not something this tool introduces.

## Building it

You do not need to — releases carry a ready-made `.exe`. If you want to build anyway:

```
dotnet build WinAcmeGui.sln -c Release
dotnet test tests/WinAcmeGui.Core.Tests/WinAcmeGui.Core.Tests.csproj
```

`.NET 8 SDK` on Windows is the only requirement. The argument-building logic lives in
`WinAcmeGui.Core`, which has no Windows or UI dependencies, so it is covered by unit
tests; the WinForms project is a thin layer over it.
