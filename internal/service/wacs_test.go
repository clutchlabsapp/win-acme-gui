package service

import (
	"strings"
	"testing"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

func TestBuildCreateArgs_ManualHTTP(t *testing.T) {
	req := model.CertificateRequest{
		Source:           model.SourceManual,
		Hosts:            []string{"example.com", "www.example.com"},
		FriendlyName:     "My Cert",
		ValidationMode:   model.ValidationModeHTTP01,
		ValidationMethod: model.ValSelfHosting,
		Store:            model.StoreCertificateStore,
		Installation:     model.InstallationIIS,
		IISInstallSiteID: 1,
		Test:             true,
		Verbose:          true,
	}

	args := BuildCreateArgs(req)

	assertContainsArg(t, args, "--source", "manual")
	assertContainsArg(t, args, "--host", "example.com,www.example.com")
	assertContainsArg(t, args, "--friendlyname", "My Cert")
	assertContainsArg(t, args, "--validationmode", "http-01")
	assertContainsArg(t, args, "--validation", "selfhosting")
	assertContainsArg(t, args, "--store", "certificatestore")
	assertContainsArg(t, args, "--installation", "iis")
	assertContainsArg(t, args, "--installationsiteid", "1")
	assertContains(t, args, "--test")
	assertContains(t, args, "--verbose")
	assertContains(t, args, "--closeonfinish")
	assertContains(t, args, "--notaskscheduler")
}

func TestBuildCreateArgs_IISDNSCloudflare(t *testing.T) {
	req := model.CertificateRequest{
		Source:           model.SourceIIS,
		IISWebsiteID:     2,
		ValidationMode:   model.ValidationModeDNS01,
		ValidationMethod: model.ValCloudflare,
		CloudflareToken:  "my-token",
		Store:            model.StorePemFiles,
		PemPath:          "C:\\certs",
		Installation:     model.InstallationNone,
	}

	args := BuildCreateArgs(req)

	assertContainsArg(t, args, "--source", "iis")
	assertContainsArg(t, args, "--siteid", "2")
	assertContainsArg(t, args, "--validationmode", "dns-01")
	assertContainsArg(t, args, "--validation", "cloudflare")
	assertContainsArg(t, args, "--cloudflareapitoken", "my-token")
	assertContainsArg(t, args, "--store", "pemfiles")
	assertContainsArg(t, args, "--pemfilespath", "C:\\certs")
	assertContainsArg(t, args, "--installation", "none")
}

func TestBuildCreateArgs_Route53(t *testing.T) {
	req := model.CertificateRequest{
		Source:           model.SourceManual,
		Hosts:            []string{"*.example.com"},
		ValidationMode:   model.ValidationModeDNS01,
		ValidationMethod: model.ValRoute53,
		AWSAccessKey:     "AKID",
		AWSSecretKey:     "secret",
		Store:            model.StorePfxFile,
		PfxPath:          "C:\\certs\\cert.pfx",
		PfxPassword:      "pass123",
		Installation:     model.InstallationScript,
		ScriptPath:       "C:\\scripts\\deploy.ps1",
	}

	args := BuildCreateArgs(req)

	assertContainsArg(t, args, "--host", "*.example.com")
	assertContainsArg(t, args, "--route53accesskeyid", "AKID")
	assertContainsArg(t, args, "--route53secretaccesskey", "secret")
	assertContainsArg(t, args, "--store", "pfxfile")
	assertContainsArg(t, args, "--pfxfilepath", "C:\\certs\\cert.pfx")
	assertContainsArg(t, args, "--pfxpassword", "pass123")
	assertContainsArg(t, args, "--installation", "script")
	assertContainsArg(t, args, "--script", "C:\\scripts\\deploy.ps1")
}

func TestBuildCreateArgs_MinimalDefaults(t *testing.T) {
	req := model.CertificateRequest{
		Source: model.SourceManual,
		Hosts:  []string{"test.com"},
	}

	args := BuildCreateArgs(req)

	assertContainsArg(t, args, "--source", "manual")
	assertContainsArg(t, args, "--host", "test.com")
	assertContains(t, args, "--closeonfinish")
	assertContains(t, args, "--notaskscheduler")
	// Should not contain empty flags
	assertNotContains(t, args, "--friendlyname")
	assertNotContains(t, args, "--test")
	assertNotContains(t, args, "--verbose")
}

func TestBuildCreateArgs_AcmeDNS(t *testing.T) {
	req := model.CertificateRequest{
		Source:           model.SourceManual,
		Hosts:            []string{"example.com"},
		ValidationMode:   model.ValidationModeDNS01,
		ValidationMethod: model.ValAcmeDNS,
		AcmeDNSServer:    "https://auth.acme-dns.io",
		Store:            model.StoreCertificateStore,
		Installation:     model.InstallationNone,
	}

	args := BuildCreateArgs(req)
	assertContainsArg(t, args, "--acmednsserver", "https://auth.acme-dns.io")
}

// assertContainsArg checks that args contains --flag followed by value.
func assertContainsArg(t *testing.T, args []string, flag, value string) {
	t.Helper()
	for i, a := range args {
		if a == flag {
			if i+1 < len(args) && args[i+1] == value {
				return
			}
			t.Errorf("flag %s found but value was %q, expected %q", flag, args[i+1], value)
			return
		}
	}
	t.Errorf("flag %s not found in args: %s", flag, strings.Join(args, " "))
}

// assertContains checks that args contains the given value.
func assertContains(t *testing.T, args []string, value string) {
	t.Helper()
	for _, a := range args {
		if a == value {
			return
		}
	}
	t.Errorf("value %q not found in args: %s", value, strings.Join(args, " "))
}

// assertNotContains checks that args does NOT contain the given value.
func assertNotContains(t *testing.T, args []string, value string) {
	t.Helper()
	for _, a := range args {
		if a == value {
			t.Errorf("value %q unexpectedly found in args: %s", value, strings.Join(args, " "))
			return
		}
	}
}
