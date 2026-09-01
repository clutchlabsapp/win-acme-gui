package service

import (
	"testing"
	"time"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

// The sample below matches win-acme's real --list rendering: a numbered list of
// "{name} - {N} renewals" with optional order, due and error clauses. It
// notably carries no hostnames and no renewal IDs.
const listSample = ` A simple Windows ACMEv2 client (WACS)
 Software version 2.2.9.1701 (release, trimmed, standalone, 64-bit)

 1: www.example.com - 4 renewals, due 2035/9/15
 2: mail.example.com - 1 renewal, due now
 3: *.contoso.com - 12 renewals, 3 orders, due 2035/9/20 ~ 2035/9/27
 4: broken.example.net - 0 renewals, due now, 2 errors
`

func TestParseRenewalList_RealFormat(t *testing.T) {
	got := ParseRenewalList(listSample)

	if len(got) != 4 {
		t.Fatalf("expected 4 renewals, got %d: %+v", len(got), got)
	}

	names := []string{"www.example.com", "mail.example.com", "*.contoso.com", "broken.example.net"}
	for i, want := range names {
		if got[i].FriendlyName != want {
			t.Errorf("renewal %d name = %q, want %q", i, got[i].FriendlyName, want)
		}
	}

	// A far-future due date is neither expired nor imminent.
	if got[0].Status != model.RenewalStatusOK {
		t.Errorf("future due date should be OK, got %v", got[0].Status)
	}
	if got[0].NextDue.IsZero() {
		t.Error("expected a parsed due date for entry 1")
	}

	// "due now" means it needs attention.
	if got[1].Status != model.RenewalStatusDueSoon {
		t.Errorf("'due now' should be DueSoon, got %v", got[1].Status)
	}

	// A due window "start ~ end" should yield the start.
	if got[2].NextDue.IsZero() {
		t.Error("expected the start of the due range to parse")
	}

	// A trailing error count outranks the schedule.
	if got[3].Status != model.RenewalStatusError {
		t.Errorf("entry with errors should be Error, got %v", got[3].Status)
	}
	if len(got[3].ErrorMessages) != 2 {
		t.Errorf("expected 2 errors recorded, got %d", len(got[3].ErrorMessages))
	}
}

// --list reports neither hostnames nor IDs, which is why the renewal files are
// the primary source. Asserting the gap keeps that rationale honest.
func TestParseRenewalList_CarriesNoHostsOrIDs(t *testing.T) {
	for _, r := range ParseRenewalList(listSample) {
		if len(r.Hosts) != 0 {
			t.Errorf("%q: --list has no hosts field, got %v", r.FriendlyName, r.Hosts)
		}
		if r.ID != "" {
			t.Errorf("%q: --list has no ID field, got %q", r.FriendlyName, r.ID)
		}
	}
}

// A friendly name may itself contain " - "; only the final " - N renewal"
// separator delimits the summary.
func TestParseRenewalList_NameContainingSeparator(t *testing.T) {
	got := ParseRenewalList(" 1: prod - web - frontend - 2 renewals, due now\n")

	if len(got) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(got))
	}
	if want := "prod - web - frontend"; got[0].FriendlyName != want {
		t.Errorf("name = %q, want %q", got[0].FriendlyName, want)
	}
}

func TestParseRenewalList_ExpiredDueDate(t *testing.T) {
	got := ParseRenewalList(" 1: old.example.com - 1 renewal, due 2020/1/5\n")

	if len(got) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(got))
	}
	if got[0].Status != model.RenewalStatusExpired {
		t.Errorf("past due date should be Expired, got %v", got[0].Status)
	}
}

func TestParseRenewalList_DueSoonWithinWindow(t *testing.T) {
	soon := time.Now().Add(3 * 24 * time.Hour)
	line := " 1: soon.example.com - 1 renewal, due " + soon.Format("2006/1/2") + "\n"

	got := ParseRenewalList(line)
	if len(got) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(got))
	}
	if got[0].Status != model.RenewalStatusDueSoon {
		t.Errorf("date inside the window should be DueSoon, got %v", got[0].Status)
	}
}

// win-acme's default UI.DateFormat is "yyyy/M/d", which is not zero-padded,
// but the user can change it, so the padded forms must parse too.
func TestParseRenewalList_DateFormats(t *testing.T) {
	for _, date := range []string{"2035/9/5", "2035/09/05", "2035-09-05"} {
		got := ParseRenewalList(" 1: host - 1 renewal, due " + date + "\n")
		if len(got) != 1 {
			t.Fatalf("%s: expected 1 renewal", date)
		}
		if got[0].NextDue.IsZero() {
			t.Errorf("%s: expected the date to parse", date)
		}
	}
}

func TestParseRenewalList_EmptyAndNoise(t *testing.T) {
	for name, input := range map[string]string{
		"empty marker": " [empty] \n\n",
		"banner only":  " A simple Windows ACMEv2 client (WACS)\n",
		"blank":        "",
	} {
		if got := ParseRenewalList(input); len(got) != 0 {
			t.Errorf("%s: expected no renewals, got %d", name, len(got))
		}
	}
}

// win-acme emits ANSI colour codes on Windows 10/11 even when redirected.
func TestParseRenewalList_StripsANSI(t *testing.T) {
	got := ParseRenewalList("\x1b[32m 1: colored.example.com - 1 renewal, due now\x1b[0m\n")

	if len(got) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(got))
	}
	if want := "colored.example.com"; got[0].FriendlyName != want {
		t.Errorf("name = %q, want %q", got[0].FriendlyName, want)
	}
}

func TestStripANSI(t *testing.T) {
	cases := map[string]string{
		"\x1b[32mgreen\x1b[0m":       "green",
		"\x1b[40m bg \x1b[0m":        " bg ",
		" [INFO] plain":              " [INFO] plain",
		"\x1b[1;31mbold red\x1b[22m": "bold red",
	}
	for in, want := range cases {
		if got := StripANSI(in); got != want {
			t.Errorf("StripANSI(%q) = %q, want %q", in, got, want)
		}
	}
}
