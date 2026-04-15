package main

import (
	"log"
	"os"

	"github.com/clutchlabsapp/win-acme-gui/internal/app"
	"github.com/clutchlabsapp/win-acme-gui/internal/extractor"
	"github.com/clutchlabsapp/win-acme-gui/internal/service"
)

// version is set at build time via -ldflags.
var version = "dev"

func main() {
	// Set up logging to a file alongside the executable for debugging
	logFile, err := os.OpenFile("win-acme-gui.log", os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err == nil {
		log.SetOutput(logFile)
		defer logFile.Close()
	}

	log.Printf("win-acme-gui %s starting", version)

	// Extract embedded win-acme binary
	wacsPath, err := extractor.EnsureExtracted()
	if err != nil {
		log.Fatalf("Failed to extract win-acme: %v", err)
	}
	log.Printf("win-acme extracted to: %s", wacsPath)

	// Create service layer
	svc := service.NewWacsService(wacsPath)

	// Create and run the application
	application := app.New(svc)

	// Trigger initial data load after a short delay to let the window render
	go application.RefreshAll()

	if err := application.Run(); err != nil {
		log.Fatalf("Application error: %v", err)
	}
}
