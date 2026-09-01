package model

import (
	"strings"
	"time"
)

// RenewalStatus represents the current state of a certificate renewal.
type RenewalStatus int

const (
	RenewalStatusOK      RenewalStatus = iota // Certificate is valid and not due
	RenewalStatusDueSoon                      // Certificate will expire within renewal window
	RenewalStatusExpired                      // Certificate has expired
	RenewalStatusError                        // Last renewal attempt failed
)

// Renewal represents a managed certificate renewal as reported by wacs.exe
// and parsed from renewal.json files.
type Renewal struct {
	ID             string
	FriendlyName   string
	LastRenewal    time.Time
	NextDue        time.Time
	Hosts          []string
	Status         RenewalStatus
	ValidationType string
	StoreType      string
	LastThumbprint string
	ErrorMessages  []string
}

// StatusText returns a human-readable status string.
func (r *Renewal) StatusText() string {
	switch r.Status {
	case RenewalStatusOK:
		return "OK"
	case RenewalStatusDueSoon:
		return "Due Soon"
	case RenewalStatusExpired:
		return "Expired"
	case RenewalStatusError:
		return "Error"
	default:
		return "Unknown"
	}
}

// HostsDisplay returns the hosts as a single comma-separated string.
func (r *Renewal) HostsDisplay() string {
	return strings.Join(r.Hosts, ", ")
}
