package extractor

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"os"
	"path/filepath"
	"strings"

	embedpkg "github.com/clutchlabsapp/win-acme-gui/embed"
)

// Version identifies the embedded win-acme release. Changing it invalidates any
// previously extracted copy, so bumping this is what triggers a re-extract on
// the next launch. Kept in sync with the Makefile by `make check-version`.
const Version = "2.2.9.1701"

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

	// Fetch the embedded archive before touching the existing extraction, so a
	// binary built without win-acme leaves any previous install intact.
	archive, err := embedpkg.WacsZip()
	if err != nil {
		return "", err
	}

	// Remove old extraction and start fresh
	if err := os.RemoveAll(targetDir); err != nil {
		return "", fmt.Errorf("removing old extraction: %w", err)
	}
	if err := os.MkdirAll(targetDir, 0755); err != nil {
		return "", fmt.Errorf("creating extraction directory: %w", err)
	}

	zipReader, err := zip.NewReader(bytes.NewReader(archive), int64(len(archive)))
	if err != nil {
		return "", fmt.Errorf("reading embedded zip: %w", err)
	}

	for _, f := range zipReader.File {
		if err := extractZipFile(f, targetDir); err != nil {
			return "", fmt.Errorf("extracting %s: %w", f.Name, err)
		}
	}

	// Verify wacs.exe exists before recording the version, so a partial
	// extraction is retried on the next launch rather than trusted.
	if _, err := os.Stat(wacsExe); err != nil {
		return "", fmt.Errorf("wacs.exe not found after extraction: %w", err)
	}

	if err := disableListPager(targetDir); err != nil {
		// Not fatal: this only matters once a user has many renewals.
		log.Printf("could not disable the wacs list pager: %v", err)
	}

	if err := os.WriteFile(versionFile, []byte(Version), 0644); err != nil {
		return "", fmt.Errorf("writing version file: %w", err)
	}

	return wacsExe, nil
}

// listPageSize is large enough that `wacs.exe --list` will not paginate in
// practice. Anything under the real renewal count triggers the pager.
const listPageSize = 1000000

// disableListPager raises UI.PageSize in win-acme's settings.
//
// `wacs.exe --list` paginates every UI.PageSize entries (default 50) and waits
// on a blocking Console.ReadKey for the spacebar between pages. Driven from a
// GUI there is nobody to press it, so a user with enough renewals would see the
// command hang forever. Raising the page size is the supported way to avoid
// that; the alternative, feeding synthetic keystrokes, is far more fragile.
//
// The existing settings are edited in place through a generic map so that every
// other default win-acme ships is preserved untouched.
func disableListPager(dir string) error {
	path := filepath.Join(dir, "settings.json")
	data, err := os.ReadFile(path)
	if err != nil {
		// Fresh installs ship settings_default.json and create settings.json on
		// first run; seed from the default when that has not happened yet.
		if data, err = os.ReadFile(filepath.Join(dir, "settings_default.json")); err != nil {
			return err
		}
	}

	var settings map[string]any
	if err := json.Unmarshal(data, &settings); err != nil {
		return fmt.Errorf("parsing settings: %w", err)
	}

	ui, ok := settings["UI"].(map[string]any)
	if !ok {
		ui = map[string]any{}
		settings["UI"] = ui
	}
	ui["PageSize"] = listPageSize

	updated, err := json.MarshalIndent(settings, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, updated, 0644)
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
