package extractor

import (
	"archive/zip"
	"bytes"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	embedpkg "github.com/clutchlabsapp/win-acme-gui/embed"
)

// Version must match the embedded win-acme release.
const Version = "2.2.9.1544"

// EnsureExtracted checks if win-acme is already extracted at the correct version.
// If not, it extracts the embedded zip to %APPDATA%/win-acme-gui/wacs/.
// Returns the absolute path to wacs.exe.
func EnsureExtracted() (string, error) {
	targetDir, err := wacsDir()
	if err != nil {
		return "", fmt.Errorf("determining extraction directory: %w", err)
	}

	versionFile := filepath.Join(targetDir, ".version")
	wacsExe := filepath.Join(targetDir, "wacs.exe")

	// Fast path: already extracted and version matches
	if data, err := os.ReadFile(versionFile); err == nil {
		if strings.TrimSpace(string(data)) == Version {
			if _, err := os.Stat(wacsExe); err == nil {
				return wacsExe, nil
			}
		}
	}

	// Remove old extraction and start fresh
	if err := os.RemoveAll(targetDir); err != nil {
		return "", fmt.Errorf("removing old extraction: %w", err)
	}
	if err := os.MkdirAll(targetDir, 0755); err != nil {
		return "", fmt.Errorf("creating extraction directory: %w", err)
	}

	// Extract embedded zip
	zipReader, err := zip.NewReader(
		bytes.NewReader(embedpkg.WacsZip),
		int64(len(embedpkg.WacsZip)),
	)
	if err != nil {
		return "", fmt.Errorf("reading embedded zip: %w", err)
	}

	for _, f := range zipReader.File {
		if err := extractZipFile(f, targetDir); err != nil {
			return "", fmt.Errorf("extracting %s: %w", f.Name, err)
		}
	}

	// Write version marker
	if err := os.WriteFile(versionFile, []byte(Version), 0644); err != nil {
		return "", fmt.Errorf("writing version file: %w", err)
	}

	// Verify wacs.exe exists after extraction
	if _, err := os.Stat(wacsExe); err != nil {
		return "", fmt.Errorf("wacs.exe not found after extraction: %w", err)
	}

	return wacsExe, nil
}

// wacsDir returns the directory where win-acme should be extracted.
func wacsDir() (string, error) {
	configDir, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(configDir, "win-acme-gui", "wacs"), nil
}

// extractZipFile extracts a single file from the zip archive.
func extractZipFile(f *zip.File, destDir string) error {
	// Sanitize the path to prevent zip slip attacks
	destPath := filepath.Join(destDir, f.Name)
	if !strings.HasPrefix(filepath.Clean(destPath), filepath.Clean(destDir)+string(os.PathSeparator)) {
		return fmt.Errorf("illegal file path: %s", f.Name)
	}

	if f.FileInfo().IsDir() {
		return os.MkdirAll(destPath, 0755)
	}

	// Ensure parent directory exists
	if err := os.MkdirAll(filepath.Dir(destPath), 0755); err != nil {
		return err
	}

	rc, err := f.Open()
	if err != nil {
		return err
	}
	defer rc.Close()

	outFile, err := os.OpenFile(destPath, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, f.Mode())
	if err != nil {
		return err
	}
	defer outFile.Close()

	_, err = io.Copy(outFile, rc)
	return err
}
