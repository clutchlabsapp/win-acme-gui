WACS_VERSION := 2.2.9.1544
WACS_URL := https://github.com/win-acme/win-acme/releases/download/v$(WACS_VERSION)/win-acme.v$(WACS_VERSION).x64.trimmed.zip
APP_VERSION := 1.0.0

.PHONY: all clean deps resources build build-native test vet

all: deps resources build

# Download win-acme release if not present
embed/wacs.zip:
	mkdir -p embed
	curl -L -o embed/wacs.zip "$(WACS_URL)"

deps: embed/wacs.zip
	go mod tidy

# Generate Windows resources (manifest + icon -> .syso)
resources:
	go install github.com/tc-hib/go-winres@latest
	go-winres make --in winres/winres.json --out cmd/win-acme-gui/rsrc

# Cross-compile for Windows from Linux (requires mingw-w64)
build: embed/wacs.zip
	CGO_ENABLED=1 \
	CC=x86_64-w64-mingw32-gcc \
	GOOS=windows \
	GOARCH=amd64 \
	go build -ldflags="-H windowsgui -s -w -X main.version=$(APP_VERSION)" \
		-o win-acme-gui.exe \
		./cmd/win-acme-gui

# Build on Windows natively
build-native: embed/wacs.zip
	go build -ldflags="-H windowsgui -s -w -X main.version=$(APP_VERSION)" \
		-o win-acme-gui.exe \
		./cmd/win-acme-gui

# Run tests (platform-independent logic)
test:
	go test ./internal/service/... ./internal/model/... ./internal/config/...

# Vet the code
vet:
	go vet ./...

clean:
	rm -f win-acme-gui.exe
	rm -f cmd/win-acme-gui/rsrc_windows_*.syso
	rm -f embed/wacs.zip
