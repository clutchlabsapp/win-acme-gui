# Version of win-acme to bundle. Keep in sync with internal/extractor.Version
# (the build verifies this with `make check-version`).
WACS_VERSION := 2.2.9.1701
APP_VERSION := 1.0.0

RELEASE_URL := https://github.com/win-acme/win-acme/releases/download/v$(WACS_VERSION)

# The "pluggable" build is used rather than "trimmed" because the DNS
# validation providers the GUI offers ship as separate plugin archives that
# have to be loaded at runtime.
WACS_BASE := win-acme.v$(WACS_VERSION).x64.pluggable.zip

# DNS validation plugins. These are NOT part of the base download; without them
# the dns-01 options in the New Certificate tab would fail at runtime. Keep this
# list in sync with model.DNSValidationMethods.
WACS_PLUGINS := \
	plugin.validation.dns.cloudflare.v$(WACS_VERSION).zip \
	plugin.validation.dns.route53.v$(WACS_VERSION).zip \
	plugin.validation.dns.azure.v$(WACS_VERSION).zip

ARCHIVE := embed/dist/wacs.zip
STAGE := build/wacs
LDFLAGS := -H windowsgui -s -w -X main.version=$(APP_VERSION)

# Walk uses pure syscall-based Win32 bindings (lxn/win), not cgo, so Windows
# binaries cross-compile from any host with CGO disabled. No mingw required.
GOBUILD := CGO_ENABLED=0 GOOS=windows go build -ldflags="$(LDFLAGS)"

.PHONY: all build build386 deps resources test vet check-version clean

all: deps resources build

## deps: download win-acme plus its DNS plugins and repack them for embedding
## The plugins have to be unpacked over the base install so wacs.exe finds them,
## so the pieces are staged into one directory and rezipped as a single archive.
deps: $(ARCHIVE)

$(ARCHIVE):
	@mkdir -p $(STAGE) embed/dist
	@echo "==> downloading win-acme $(WACS_VERSION)"
	@curl -fL --retry 3 -o build/$(WACS_BASE) "$(RELEASE_URL)/$(WACS_BASE)"
	@cd $(STAGE) && unzip -oq ../$(WACS_BASE)
	@for p in $(WACS_PLUGINS); do \
		echo "==> downloading $$p"; \
		curl -fL --retry 3 -o build/$$p "$(RELEASE_URL)/$$p" || exit 1; \
		(cd $(STAGE) && unzip -oq ../$$p) || exit 1; \
	done
	@test -f $(STAGE)/wacs.exe || { echo "ERROR: wacs.exe missing from staged archive"; exit 1; }
	@cd $(STAGE) && zip -qr ../../$(ARCHIVE) .
	@echo "==> packed $(ARCHIVE) ($$(du -h $(ARCHIVE) | cut -f1))"

## resources: generate the Windows manifest/icon .syso
## Required for correct visual styles; Walk needs Common Controls v6.
resources:
	go install github.com/tc-hib/go-winres@latest
	go-winres make --in winres/winres.json --out cmd/win-acme-gui/rsrc

## build: produce the 64-bit Windows executable
build: check-version
	$(GOBUILD) -o win-acme-gui.exe ./cmd/win-acme-gui

## build386: produce the 32-bit Windows executable
build386: check-version
	GOARCH=386 $(GOBUILD) -o win-acme-gui-x86.exe ./cmd/win-acme-gui

## check-version: fail if the Makefile and extractor disagree on the win-acme version
check-version:
	@grep -q 'Version = "$(WACS_VERSION)"' internal/extractor/extractor.go \
		|| { echo "ERROR: WACS_VERSION ($(WACS_VERSION)) does not match internal/extractor.Version"; exit 1; }

## test: run the unit tests
## Scoped to the non-UI packages so this runs on any host: internal/ui imports
## Walk, which has no non-Windows build. `make vet` covers the UI packages.
test:
	CGO_ENABLED=0 go test ./internal/service/... ./internal/model/... \
		./internal/config/... ./internal/extractor/... ./embed/...

## vet: static analysis against the Windows target
vet:
	CGO_ENABLED=0 GOOS=windows go vet ./...

clean:
	rm -f win-acme-gui.exe win-acme-gui-x86.exe
	rm -f cmd/win-acme-gui/rsrc_windows_*.syso
	rm -rf build
	rm -f $(ARCHIVE)
