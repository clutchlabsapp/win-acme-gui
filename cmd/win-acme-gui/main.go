// Command win-acme-gui is a native Windows front-end for the win-acme ACME
// client. It bundles win-acme itself, so the only thing a user needs to deploy
// is this single executable.
package main

import (
	"log"
	"os"
	"path/filepath"

	"github.com/lxn/walk"

	"github.com/clutchlabsapp/win-acme-gui/internal/app"
	"github.com/clutchlabsapp/win-acme-gui/internal/config"
	"github.com/clutchlabsapp/win-acme-gui/internal/extractor"
	"github.com/clutchlabsapp/win-acme-gui/internal/service"
)

// version is set at build time via -ldflags.
var version = "dev"

func main() {
	closeLog := setupLogging()
	defer closeLog()

	log.Printf("win-acme-gui %s starting", version)

	// Unpack the bundled win-acme before building any UI: without it there is
	// nothing for the GUI to drive.
	wacsPath, err := extractor.EnsureExtracted()
	if err != nil {
		fatal("Startup Failed", "Could not prepare win-acme:\n\n"+err.Error())
	}
	log.Printf("win-acme ready at %s", wacsPath)

	application := app.New(service.NewWacsService(wacsPath))
	if err := application.Run(); err != nil {
		fatal("Application Error", "The application failed to start:\n\n"+err.Error())
	}
}

// setupLogging directs the log package to a file in the app config directory.
// The working directory is not a reliable place to write: a GUI app can be
// launched from anywhere, including directories the user cannot write to.
// Returns a function that closes the log file.
func setupLogging() func() {
	dir, err := config.ConfigDir()
	if err != nil {
		return func() {}
	}
	if err := os.MkdirAll(dir, 0755); err != nil {
		return func() {}
	}
	f, err := os.OpenFile(filepath.Join(dir, "win-acme-gui.log"),
		os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err != nil {
		return func() {}
	}
	log.SetOutput(f)
	return func() { f.Close() }
}

// fatal reports an unrecoverable startup error and exits.
//
// The binary is linked with -H windowsgui and so has no console: writing to
// stderr would make the failure invisible and the app would appear to do
// nothing at all. A message box is the only way the user sees the reason.
func fatal(title, message string) {
	log.Printf("FATAL: %s: %s", title, message)
	walk.MsgBox(nil, title, message, walk.MsgBoxIconError)
	os.Exit(1)
}
