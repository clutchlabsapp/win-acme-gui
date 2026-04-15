package service

import (
	"testing"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

func TestParseRenewalList_MultipleRenewals(t *testing.T) {
	output := `A]utomatic certificate management with win-acme (WACS)
 Running in mode: List

 1: example.com - example.com, www.example.com - 2026/05/15
 2: test.org - test.org - 2026/03/01
 3: wildcard - *.mysite.com - 2026/06/20

`
	renewals := ParseRenewalList(output)

	if len(renewals) != 3 {
		t.Fatalf("expected 3 renewals, got %d", len(renewals))
	}

	// First renewal
	r := renewals[0]
	if r.FriendlyName != "example.com" {
		t.Errorf("expected FriendlyName 'example.com', got %q", r.FriendlyName)
	}
	if len(r.Hosts) != 2 {
		t.Errorf("expected 2 hosts, got %d", len(r.Hosts))
	}
	if r.Hosts[0] != "example.com" || r.Hosts[1] != "www.example.com" {
		t.Errorf("unexpected hosts: %v", r.Hosts)
	}

	// Third renewal (wildcard)
	r3 := renewals[2]
	if r3.FriendlyName != "wildcard" {
		t.Errorf("expected FriendlyName 'wildcard', got %q", r3.FriendlyName)
	}
	if len(r3.Hosts) != 1 || r3.Hosts[0] != "*.mysite.com" {
		t.Errorf("unexpected hosts for wildcard: %v", r3.Hosts)
	}
}

func TestParseRenewalList_Empty(t *testing.T) {
	output := `Running in mode: List

No scheduled renewals found.
`
	renewals := ParseRenewalList(output)
	if len(renewals) != 0 {
		t.Errorf("expected 0 renewals, got %d", len(renewals))
	}
}

func TestParseRenewalList_SingleRenewal(t *testing.T) {
	output := ` 1: my-cert - mydomain.com - 2026/04/20
`
	renewals := ParseRenewalList(output)
	if len(renewals) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(renewals))
	}
	if renewals[0].FriendlyName != "my-cert" {
		t.Errorf("expected 'my-cert', got %q", renewals[0].FriendlyName)
	}
	if renewals[0].NextDue.IsZero() {
		t.Error("expected NextDue to be set")
	}
}

func TestParseRenewalList_ExpiredStatus(t *testing.T) {
	output := ` 1: expired-cert - old.com - 2020/01/01
`
	renewals := ParseRenewalList(output)
	if len(renewals) != 1 {
		t.Fatalf("expected 1 renewal, got %d", len(renewals))
	}
	if renewals[0].Status != model.RenewalStatusExpired {
		t.Errorf("expected Expired status, got %v", renewals[0].Status)
	}
}
