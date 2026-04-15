package service

import (
	"encoding/json"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

// renewalListLineRe matches lines from `wacs.exe --list` output.
// Typical format: " 1: FriendlyName - host1.com, host2.com - due date"
// or: "  - Id: <guid>"
var renewalListLineRe = regexp.MustCompile(`^\s*\d+:\s+(.+)$`)

// ParseRenewalList parses the text output of `wacs.exe --list`.
func ParseRenewalList(output string) []model.Renewal {
	var renewals []model.Renewal
	lines := strings.Split(output, "\n")

	for _, line := range lines {
		line = strings.TrimRight(line, "\r")
		match := renewalListLineRe.FindStringSubmatch(line)
		if match == nil {
			continue
		}

		parts := strings.SplitN(match[1], " - ", 3)
		r := model.Renewal{
			FriendlyName: strings.TrimSpace(parts[0]),
			Status:       model.RenewalStatusOK,
		}

		if len(parts) >= 2 {
			hosts := strings.Split(parts[1], ",")
			for _, h := range hosts {
				h = strings.TrimSpace(h)
				if h != "" {
					r.Hosts = append(r.Hosts, h)
				}
			}
		}

		if len(parts) >= 3 {
			duePart := strings.TrimSpace(parts[2])
			if t, err := time.Parse("2006/01/02", duePart); err == nil {
				r.NextDue = t
				if time.Until(t) < 14*24*time.Hour {
					r.Status = model.RenewalStatusDueSoon
				}
				if t.Before(time.Now()) {
					r.Status = model.RenewalStatusExpired
				}
			}
		}

		renewals = append(renewals, r)
	}

	return renewals
}

// renewalJSON represents the structure of a *.renewal.json file on disk.
type renewalJSON struct {
	ID   string `json:"Id"`
	Last struct {
		Date       string `json:"Date"`
		Thumbprint string `json:"Thumbprint"`
	} `json:"History"`
	FriendlyName string `json:"FriendlyNamePart"`
	TargetPlugin struct {
		Host string `json:"Host"`
	} `json:"TargetPluginOptions"`
	ValidationPlugin struct {
		Name string `json:"Plugin"`
	} `json:"ValidationPluginOptions"`
	StorePlugin struct {
		Name string `json:"Plugin"`
	} `json:"StorePluginOptions"`
}

// ReadRenewalFiles reads all *.renewal.json files from the given config directory.
// This provides richer data than the --list output.
func ReadRenewalFiles(configDir string) ([]model.Renewal, error) {
	pattern := filepath.Join(configDir, "*.renewal.json")
	files, err := filepath.Glob(pattern)
	if err != nil {
		return nil, err
	}

	var renewals []model.Renewal
	for _, f := range files {
		data, err := os.ReadFile(f)
		if err != nil {
			continue
		}

		var rj renewalJSON
		if err := json.Unmarshal(data, &rj); err != nil {
			continue
		}

		r := model.Renewal{
			ID:           rj.ID,
			FriendlyName: rj.FriendlyName,
			Status:       model.RenewalStatusOK,
		}

		if rj.TargetPlugin.Host != "" {
			hosts := strings.Split(rj.TargetPlugin.Host, ",")
			for _, h := range hosts {
				h = strings.TrimSpace(h)
				if h != "" {
					r.Hosts = append(r.Hosts, h)
				}
			}
		}

		if rj.ValidationPlugin.Name != "" {
			r.ValidationType = rj.ValidationPlugin.Name
		}
		if rj.StorePlugin.Name != "" {
			r.StoreType = rj.StorePlugin.Name
		}

		r.LastThumbprint = rj.Last.Thumbprint
		if rj.Last.Date != "" {
			if t, err := time.Parse(time.RFC3339, rj.Last.Date); err == nil {
				r.LastRenewal = t
			}
		}

		renewals = append(renewals, r)
	}

	return renewals, nil
}
