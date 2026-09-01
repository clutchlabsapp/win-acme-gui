package extractor

import (
	"archive/zip"
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// buildZip returns an in-memory zip containing the given name->content entries.
func buildZip(t *testing.T, entries map[string]string) *zip.Reader {
	t.Helper()
	var buf bytes.Buffer
	zw := zip.NewWriter(&buf)
	for name, content := range entries {
		w, err := zw.Create(name)
		if err != nil {
			t.Fatalf("creating zip entry %q: %v", name, err)
		}
		if _, err := w.Write([]byte(content)); err != nil {
			t.Fatalf("writing zip entry %q: %v", name, err)
		}
	}
	if err := zw.Close(); err != nil {
		t.Fatalf("closing zip: %v", err)
	}
	zr, err := zip.NewReader(bytes.NewReader(buf.Bytes()), int64(buf.Len()))
	if err != nil {
		t.Fatalf("reading back zip: %v", err)
	}
	return zr
}

func TestExtractZipFile_WritesNestedFiles(t *testing.T) {
	dest := t.TempDir()
	zr := buildZip(t, map[string]string{
		"wacs.exe":            "binary",
		"plugins/dns.dll":     "plugin",
		"settings_default.js": "config",
	})

	for _, f := range zr.File {
		if err := extractZipFile(f, dest); err != nil {
			t.Fatalf("extracting %q: %v", f.Name, err)
		}
	}

	for _, rel := range []string{"wacs.exe", filepath.Join("plugins", "dns.dll")} {
		if _, err := os.Stat(filepath.Join(dest, rel)); err != nil {
			t.Errorf("expected %q to exist after extraction: %v", rel, err)
		}
	}

	got, err := os.ReadFile(filepath.Join(dest, "plugins", "dns.dll"))
	if err != nil {
		t.Fatalf("reading extracted plugin: %v", err)
	}
	if string(got) != "plugin" {
		t.Errorf("plugin content = %q, want %q", got, "plugin")
	}
}

// A malicious archive must not be able to write outside the destination
// directory via traversal entries ("zip slip").
func TestExtractZipFile_RejectsTraversal(t *testing.T) {
	for _, name := range []string{
		"../escaped.txt",
		"../../escaped.txt",
		"plugins/../../escaped.txt",
	} {
		t.Run(name, func(t *testing.T) {
			dest := t.TempDir()
			zr := buildZip(t, map[string]string{name: "pwned"})

			err := extractZipFile(zr.File[0], dest)
			if err == nil {
				t.Fatalf("expected traversal entry %q to be rejected", name)
			}
			if !strings.Contains(err.Error(), "illegal file path") {
				t.Errorf("unexpected error for %q: %v", name, err)
			}

			// Nothing should have been written next to the temp dir.
			if _, statErr := os.Stat(filepath.Join(filepath.Dir(dest), "escaped.txt")); statErr == nil {
				t.Errorf("traversal entry %q escaped the destination directory", name)
			}
		})
	}
}

func TestExtractZipFile_CreatesDirectoryEntries(t *testing.T) {
	dest := t.TempDir()
	var buf bytes.Buffer
	zw := zip.NewWriter(&buf)
	if _, err := zw.Create("plugins/"); err != nil {
		t.Fatalf("creating dir entry: %v", err)
	}
	if err := zw.Close(); err != nil {
		t.Fatalf("closing zip: %v", err)
	}
	zr, err := zip.NewReader(bytes.NewReader(buf.Bytes()), int64(buf.Len()))
	if err != nil {
		t.Fatalf("reading zip: %v", err)
	}

	if err := extractZipFile(zr.File[0], dest); err != nil {
		t.Fatalf("extracting directory entry: %v", err)
	}

	info, err := os.Stat(filepath.Join(dest, "plugins"))
	if err != nil {
		t.Fatalf("expected plugins directory: %v", err)
	}
	if !info.IsDir() {
		t.Error("expected plugins to be a directory")
	}
}
