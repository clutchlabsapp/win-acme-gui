package service

import (
	"encoding/json"
	"io/fs"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

// ansiRe matches SGR colour escapes. win-acme picks an ANSI Serilog theme
// whenever the OS major version is 10, which covers both Windows 10 and 11,
// and it does not disable that when output is redirected. Captured output
// therefore carries escape codes that must be removed before display or
// parsing.
var ansiRe = regexp.MustCompile(`\x1b\[[0-9;]*[A-Za-z]`)

// StripANSI removes terminal colour escapes from captured wacs.exe output.
func StripANSI(s string) string {
	return ansiRe.ReplaceAllString(s, "")
}

// listLineRe matches one entry of `wacs.exe --list`, which win-acme renders as
// " {index}: {description}" with a single leading space and a 1-based index.
var listLineRe = regexp.MustCompile(`^\s*\d+:\s+(.+)$`)

// listDescRe splits a renewal description into its name and trailing summary.
//
// The description is "{friendly name} - {N} renewal[s]" followed by optional
// ", {M} orders", ", due ..." and ", {K} error[s]" clauses. The leading group is
// greedy so that a friendly name which itself contains " - " is kept intact:
// only the final " - N renewal" separator is treated as the split point.
var listDescRe = regexp.MustCompile(`^(.*) - (\d+) renewals?(.*)$`)

var (
	ordersRe = regexp.MustCompile(`^(\d+) orders?$`)
	errorsRe = regexp.MustCompile(`^(\d+) errors?$`)
	dueRe    = regexp.MustCompile(`^due (.+)$`)
)

// dueSoonWindow is how close an upcoming renewal must be to be flagged.
const dueSoonWindow = 14 * 24 * time.Hour

// ParseRenewalList parses the output of `wacs.exe --list`.
//
// This is a fallback: the console layout is not a documented interface, and it
// deliberately omits both the hostnames and the renewal ID, so it cannot
// produce a complete model. ReadRenewalFiles is the primary source; this exists
// for when the renewal files cannot be located.
//
// Sample of the real format:
//
//	1: www.example.com - 4 renewals, due 2026/9/15
//	2: mail.example.com - 1 renewal, due now
//	3: *.contoso.com - 12 renewals, 3 orders, due 2026/9/20 ~ 2026/9/27
//	4: broken.example.net - 0 renewals, due now, 2 errors
func ParseRenewalList(output string) []model.Renewal {
	var renewals []model.Renewal

	for _, line := range strings.Split(StripANSI(output), "\n") {
		line = strings.TrimRight(line, "\r")

		entry := listLineRe.FindStringSubmatch(line)
		if entry == nil {
			continue
		}
		desc := listDescRe.FindStringSubmatch(strings.TrimSpace(entry[1]))
		if desc == nil {
			continue
		}

		r := model.Renewal{
			FriendlyName: strings.TrimSpace(desc[1]),
			Status:       model.RenewalStatusOK,
		}
		applyListClauses(&r, desc[3])
		renewals = append(renewals, r)
	}

	return renewals
}

// applyListClauses interprets the trailing ", ..." clauses of a --list entry.
func applyListClauses(r *model.Renewal, tail string) {
	dueNow := false

	for _, clause := range strings.Split(tail, ",") {
		clause = strings.TrimSpace(clause)
		switch {
		case clause == "":
		case ordersRe.MatchString(clause):
			// Order count carries no state the UI surfaces.
		case errorsRe.MatchString(clause):
			if n, _ := strconv.Atoi(errorsRe.FindStringSubmatch(clause)[1]); n > 0 {
				r.ErrorMessages = make([]string, n)
				r.Status = model.RenewalStatusError
			}
		case dueRe.MatchString(clause):
			value := dueRe.FindStringSubmatch(clause)[1]
			if value == "now" {
				dueNow = true
				continue
			}
			// A due window renders as "start ~ end"; the start is what matters.
			if start, _, found := strings.Cut(value, "~"); found {
				value = strings.TrimSpace(start)
			}
			if t, ok := parseDueDate(value); ok {
				r.NextDue = t
			}
		}
	}

	// An explicit error state outranks scheduling.
	if r.Status == model.RenewalStatusError {
		return
	}
	switch {
	case dueNow:
		r.Status = model.RenewalStatusDueSoon
	case r.NextDue.IsZero():
	case r.NextDue.Before(time.Now()):
		r.Status = model.RenewalStatusExpired
	case time.Until(r.NextDue) < dueSoonWindow:
		r.Status = model.RenewalStatusDueSoon
	}
}

// dueDateLayouts covers win-acme's default UI.DateFormat of "yyyy/M/d", whose
// month and day are not zero-padded, plus the padded and ISO forms a user may
// configure instead.
var dueDateLayouts = []string{"2006/1/2", "2006/01/02", "2006-01-02"}

func parseDueDate(s string) (time.Time, bool) {
	s = strings.TrimSpace(s)
	for _, layout := range dueDateLayouts {
		if t, err := time.Parse(layout, s); err == nil {
			return t, true
		}
	}
	return time.Time{}, false
}

// DiscoverConfigDir locates the directory holding win-acme's renewal files.
//
// win-acme keeps them under %ProgramData%\win-acme\<server-specific folder>,
// where the leaf is derived from the ACME server URI (so the staging and
// production endpoints do not share state). Rather than reconstructing that
// name, walk the tree and take the directory that actually contains renewal
// files. Returns "" when none are found.
func DiscoverConfigDir() string {
	programData := os.Getenv("ProgramData")
	if programData == "" {
		return ""
	}
	root := filepath.Join(programData, "win-acme")

	var found string
	// The tree is shallow (one directory per ACME server), so a full walk is cheap.
	_ = filepath.WalkDir(root, func(path string, d fs.DirEntry, err error) error {
		if err != nil || d.IsDir() {
			return nil //nolint:nilerr // unreadable subtrees are skipped, not fatal
		}
		if strings.HasSuffix(d.Name(), ".renewal.json") {
			found = filepath.Dir(path)
			return filepath.SkipAll
		}
		return nil
	})
	return found
}

// renewalJSON is the subset of a *.renewal.json file that the UI displays.
type renewalJSON struct {
	ID           string `json:"Id"`
	FriendlyName string `json:"LastFriendlyName"`
	History      []struct {
		Date          string   `json:"Date"`
		Success       *bool    `json:"Success"`
		Thumbprints   []string `json:"Thumbprints"`
		ErrorMessages []string `json:"ErrorMessages"`
	} `json:"History"`
	TargetPluginOptions struct {
		CommonName string   `json:"CommonName"`
		Host       string   `json:"Host"`
		AltNames   []string `json:"AltNames"`
	} `json:"TargetPluginOptions"`
	ValidationPluginOptions struct {
		Plugin string `json:"Plugin"`
		Name   string `json:"Name"`
	} `json:"ValidationPluginOptions"`
	StorePluginOptions []struct {
		Plugin string `json:"Plugin"`
		Name   string `json:"Name"`
	} `json:"StorePluginOptions"`
}

// ReadRenewalFiles reads all *.renewal.json files from the given config directory.
//
// These files are win-acme's own on-disk state and are far more reliable to
// consume than the human-readable `--list` output, whose layout is not a
// documented interface and which omits both hostnames and renewal IDs. Entries
// that fail to parse are skipped rather than failing the whole read, so one
// malformed file cannot hide the others.
func ReadRenewalFiles(configDir string) ([]model.Renewal, error) {
	if configDir == "" {
		return nil, nil
	}
	files, err := filepath.Glob(filepath.Join(configDir, "*.renewal.json"))
	if err != nil {
		return nil, err
	}

	var renewals []model.Renewal
	for _, path := range files {
		data, err := os.ReadFile(path)
		if err != nil {
			continue
		}
		var rj renewalJSON
		if err := json.Unmarshal(data, &rj); err != nil {
			continue
		}
		renewals = append(renewals, rj.toModel())
	}
	return renewals, nil
}

func (rj renewalJSON) toModel() model.Renewal {
	r := model.Renewal{
		ID:             rj.ID,
		FriendlyName:   rj.FriendlyName,
		Status:         model.RenewalStatusOK,
		ValidationType: rj.ValidationPluginOptions.Name,
		Hosts:          rj.hosts(),
	}
	if r.ValidationType == "" {
		r.ValidationType = rj.ValidationPluginOptions.Plugin
	}
	if len(rj.StorePluginOptions) > 0 {
		r.StoreType = rj.StorePluginOptions[0].Name
		if r.StoreType == "" {
			r.StoreType = rj.StorePluginOptions[0].Plugin
		}
	}

	// History is append-ordered, so the final entry is the most recent run.
	if n := len(rj.History); n > 0 {
		last := rj.History[n-1]
		if t, err := time.Parse(time.RFC3339, last.Date); err == nil {
			r.LastRenewal = t
		}
		if len(last.Thumbprints) > 0 {
			r.LastThumbprint = last.Thumbprints[len(last.Thumbprints)-1]
		}
		if last.Success != nil && !*last.Success {
			r.Status = model.RenewalStatusError
			r.ErrorMessages = last.ErrorMessages
		}
	}
	return r
}

// hosts collects the certificate's names, preferring the common name and
// falling back to the host field, then adding any subject alternative names.
func (rj renewalJSON) hosts() []string {
	var out []string
	seen := map[string]bool{}
	add := func(h string) {
		h = strings.TrimSpace(h)
		if h != "" && !seen[h] {
			seen[h] = true
			out = append(out, h)
		}
	}

	add(rj.TargetPluginOptions.CommonName)
	for _, h := range strings.Split(rj.TargetPluginOptions.Host, ",") {
		add(h)
	}
	for _, h := range rj.TargetPluginOptions.AltNames {
		add(h)
	}
	return out
}
