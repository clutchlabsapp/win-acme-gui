package service

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

// A representative *.renewal.json as win-acme writes it. These files are the
// primary source for the renewals view because --list omits hosts and IDs.
// Note the ID is a 22-character ShortGuid, not a GUID.
const renewalFile = `{
  "Id": "hFhBaGbBLkiIY0RpVfPtDA",
  "LastFriendlyName": "www.example.com",
  "History": [
    {
      "Date": "2026-01-15T09:00:00Z",
      "Success": true,
      "Thumbprints": ["AAAA1111"]
    },
    {
      "Date": "2026-03-20T09:00:00Z",
      "Success": true,
      "Thumbprints": ["BBBB2222"]
    }
  ],
  "TargetPluginOptions": {
    "CommonName": "example.com",
    "Host": "example.com,www.example.com",
    "AltNames": ["shop.example.com"]
  },
  "ValidationPluginOptions": {
    "Plugin": "a2c9e8f1-0000-0000-0000-000000000000",
    "Name": "SelfHosting"
  },
  "StorePluginOptions": [
    { "Plugin": "b3d0f9a2-0000-0000-0000-000000000000", "Name": "CertificateStore" }
  ]
}`

func writeRenewal(t *testing.T, dir, name, content string) {
	t.Helper()
	if err := os.WriteFile(filepath.Join(dir, name), []byte(content), 0644); err != nil {
		t.Fatalf("writing %s: %v", name, err)
	}
}

func TestReadRenewalFiles_ParsesFullRecord(t *testing.T) {
	dir := t.TempDir()
	writeRenewal(t, dir, "one.renewal.json", renewalFile)

	got, err := ReadRenewalFiles(dir)
	if err != nil {
		t.Fatalf("ReadRenewalFiles: %v", err)
	}
	if len(got) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(got))
	}
	r := got[0]

	if r.ID != "hFhBaGbBLkiIY0RpVfPtDA" {
		t.Errorf("ID = %q", r.ID)
	}
	if r.FriendlyName != "www.example.com" {
		t.Errorf("FriendlyName = %q", r.FriendlyName)
	}
	if r.ValidationType != "SelfHosting" {
		t.Errorf("ValidationType = %q", r.ValidationType)
	}
	if r.StoreType != "CertificateStore" {
		t.Errorf("StoreType = %q", r.StoreType)
	}

	// The most recent history entry wins.
	if r.LastThumbprint != "BBBB2222" {
		t.Errorf("LastThumbprint = %q, want the newest", r.LastThumbprint)
	}
	if r.LastRenewal.IsZero() || r.LastRenewal.Year() != 2026 || r.LastRenewal.Month() != 3 {
		t.Errorf("LastRenewal = %v, want the newest entry", r.LastRenewal)
	}

	// Common name, hosts and alt names are merged without duplicates.
	want := []string{"example.com", "www.example.com", "shop.example.com"}
	if len(r.Hosts) != len(want) {
		t.Fatalf("Hosts = %v, want %v", r.Hosts, want)
	}
	for i, h := range want {
		if r.Hosts[i] != h {
			t.Errorf("Hosts[%d] = %q, want %q", i, r.Hosts[i], h)
		}
	}
}

func TestReadRenewalFiles_FailedRunMarksError(t *testing.T) {
	dir := t.TempDir()
	writeRenewal(t, dir, "bad.renewal.json", `{
	  "Id": "abc", "LastFriendlyName": "broken.example.net",
	  "History": [{
	    "Date": "2026-04-01T09:00:00Z",
	    "Success": false,
	    "ErrorMessages": ["validation failed", "dns timeout"]
	  }]
	}`)

	got, err := ReadRenewalFiles(dir)
	if err != nil {
		t.Fatalf("ReadRenewalFiles: %v", err)
	}
	if len(got) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(got))
	}
	if got[0].Status != model.RenewalStatusError {
		t.Errorf("Status = %v, want Error", got[0].Status)
	}
	if len(got[0].ErrorMessages) != 2 {
		t.Errorf("ErrorMessages = %v, want 2", got[0].ErrorMessages)
	}
}

// One unreadable file must not hide the rest.
func TestReadRenewalFiles_SkipsMalformed(t *testing.T) {
	dir := t.TempDir()
	writeRenewal(t, dir, "good.renewal.json", renewalFile)
	writeRenewal(t, dir, "broken.renewal.json", "{ this is not json")

	got, err := ReadRenewalFiles(dir)
	if err != nil {
		t.Fatalf("ReadRenewalFiles: %v", err)
	}
	if len(got) != 1 {
		t.Fatalf("expected the valid renewal to survive, got %d", len(got))
	}
	if got[0].FriendlyName != "www.example.com" {
		t.Errorf("FriendlyName = %q", got[0].FriendlyName)
	}
}

func TestReadRenewalFiles_IgnoresUnrelatedFiles(t *testing.T) {
	dir := t.TempDir()
	writeRenewal(t, dir, "settings.json", `{"UI":{"PageSize":50}}`)
	writeRenewal(t, dir, "notes.txt", "hello")

	got, err := ReadRenewalFiles(dir)
	if err != nil {
		t.Fatalf("ReadRenewalFiles: %v", err)
	}
	if len(got) != 0 {
		t.Errorf("expected no renewals, got %d", len(got))
	}
}

func TestReadRenewalFiles_EmptyDirAndUnsetPath(t *testing.T) {
	if got, err := ReadRenewalFiles(t.TempDir()); err != nil || len(got) != 0 {
		t.Errorf("empty dir: got %d renewals, err %v", len(got), err)
	}
	if got, err := ReadRenewalFiles(""); err != nil || got != nil {
		t.Errorf("unset path: got %v, err %v", got, err)
	}
}
