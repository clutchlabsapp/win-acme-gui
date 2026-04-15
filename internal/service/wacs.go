package service

import (
	"bufio"
	"context"
	"fmt"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

// WacsService defines the interface for all win-acme operations.
type WacsService interface {
	// ListRenewals returns all managed renewals.
	ListRenewals(ctx context.Context) ([]model.Renewal, string, error)

	// CreateCertificate creates a new certificate from the given request.
	CreateCertificate(ctx context.Context, req model.CertificateRequest) (string, error)

	// RenewAll forces renewal of all certificates.
	RenewAll(ctx context.Context) (string, error)

	// RenewSingle forces renewal of a specific certificate by friendly name.
	RenewSingle(ctx context.Context, friendlyName string) (string, error)

	// Cancel cancels a renewal by friendly name.
	Cancel(ctx context.Context, friendlyName string) (string, error)

	// Revoke revokes the most recent certificate for a renewal.
	Revoke(ctx context.Context, friendlyName string) (string, error)

	// Version returns the wacs.exe version string.
	Version(ctx context.Context) (string, error)

	// RunStreaming executes wacs.exe and streams output line-by-line.
	RunStreaming(ctx context.Context, args []string, onLine func(string)) error

	// WacsExePath returns the path to the wacs.exe binary.
	WacsExePath() string
}

// wacsService implements WacsService by invoking wacs.exe.
type wacsService struct {
	wacsExe string
}

// NewWacsService creates a new service that wraps the given wacs.exe binary.
func NewWacsService(wacsExe string) WacsService {
	return &wacsService{wacsExe: wacsExe}
}

func (s *wacsService) WacsExePath() string {
	return s.wacsExe
}

// run executes wacs.exe with the given arguments, capturing combined output.
func (s *wacsService) run(ctx context.Context, args ...string) (string, error) {
	cmd := exec.CommandContext(ctx, s.wacsExe, args...)
	cmd.Dir = filepath.Dir(s.wacsExe)
	output, err := cmd.CombinedOutput()
	return string(output), err
}

func (s *wacsService) ListRenewals(ctx context.Context) ([]model.Renewal, string, error) {
	output, err := s.run(ctx, "--list", "--closeonfinish")
	if err != nil {
		return nil, output, fmt.Errorf("wacs --list failed: %w\nOutput: %s", err, output)
	}
	renewals := ParseRenewalList(output)
	return renewals, output, nil
}

func (s *wacsService) CreateCertificate(ctx context.Context, req model.CertificateRequest) (string, error) {
	args := BuildCreateArgs(req)
	output, err := s.run(ctx, args...)
	if err != nil {
		return output, fmt.Errorf("certificate creation failed: %w\nOutput: %s", err, output)
	}
	return output, nil
}

func (s *wacsService) RenewAll(ctx context.Context) (string, error) {
	return s.run(ctx, "--renew", "--force", "--closeonfinish", "--notaskscheduler")
}

func (s *wacsService) RenewSingle(ctx context.Context, friendlyName string) (string, error) {
	return s.run(ctx, "--renew", "--force", "--friendlyname", friendlyName,
		"--closeonfinish", "--notaskscheduler")
}

func (s *wacsService) Cancel(ctx context.Context, friendlyName string) (string, error) {
	return s.run(ctx, "--cancel", "--friendlyname", friendlyName, "--closeonfinish")
}

func (s *wacsService) Revoke(ctx context.Context, friendlyName string) (string, error) {
	return s.run(ctx, "--revoke", "--friendlyname", friendlyName, "--closeonfinish")
}

func (s *wacsService) Version(ctx context.Context) (string, error) {
	output, err := s.run(ctx, "--version", "--closeonfinish")
	if err != nil {
		return "", fmt.Errorf("wacs --version failed: %w", err)
	}
	return strings.TrimSpace(output), nil
}

func (s *wacsService) RunStreaming(ctx context.Context, args []string, onLine func(string)) error {
	cmd := exec.CommandContext(ctx, s.wacsExe, args...)
	cmd.Dir = filepath.Dir(s.wacsExe)

	stdout, err := cmd.StdoutPipe()
	if err != nil {
		return fmt.Errorf("creating stdout pipe: %w", err)
	}
	cmd.Stderr = cmd.Stdout // Merge stderr into stdout

	if err := cmd.Start(); err != nil {
		return fmt.Errorf("starting wacs: %w", err)
	}

	scanner := bufio.NewScanner(stdout)
	for scanner.Scan() {
		onLine(scanner.Text())
	}

	return cmd.Wait()
}

// BuildCreateArgs constructs CLI arguments from a CertificateRequest.
func BuildCreateArgs(req model.CertificateRequest) []string {
	args := []string{
		"--source", string(req.Source),
	}

	if req.Source == model.SourceManual && len(req.Hosts) > 0 {
		args = append(args, "--host", strings.Join(req.Hosts, ","))
	}
	if req.FriendlyName != "" {
		args = append(args, "--friendlyname", req.FriendlyName)
	}
	if req.Source == model.SourceIIS && req.IISWebsiteID > 0 {
		args = append(args, "--siteid", strconv.Itoa(req.IISWebsiteID))
	}

	// Validation
	if req.ValidationMode != "" {
		args = append(args, "--validationmode", string(req.ValidationMode))
	}
	if req.ValidationMethod != "" {
		args = append(args, "--validation", string(req.ValidationMethod))
	}

	// DNS plugin credentials
	switch req.ValidationMethod {
	case model.ValCloudflare:
		if req.CloudflareToken != "" {
			args = append(args, "--cloudflareapitoken", req.CloudflareToken)
		}
	case model.ValRoute53:
		if req.AWSAccessKey != "" {
			args = append(args, "--route53accesskeyid", req.AWSAccessKey)
		}
		if req.AWSSecretKey != "" {
			args = append(args, "--route53secretaccesskey", req.AWSSecretKey)
		}
	case model.ValAcmeDNS:
		if req.AcmeDNSServer != "" {
			args = append(args, "--acmednsserver", req.AcmeDNSServer)
		}
	}

	// Store
	if req.Store != "" {
		args = append(args, "--store", string(req.Store))
	}
	switch req.Store {
	case model.StorePemFiles:
		if req.PemPath != "" {
			args = append(args, "--pemfilespath", req.PemPath)
		}
	case model.StorePfxFile:
		if req.PfxPath != "" {
			args = append(args, "--pfxfilepath", req.PfxPath)
		}
		if req.PfxPassword != "" {
			args = append(args, "--pfxpassword", req.PfxPassword)
		}
	}

	// Installation
	if req.Installation != "" {
		args = append(args, "--installation", string(req.Installation))
	}
	if req.Installation == model.InstallationIIS && req.IISInstallSiteID > 0 {
		args = append(args, "--installationsiteid", strconv.Itoa(req.IISInstallSiteID))
	}
	if req.Installation == model.InstallationScript && req.ScriptPath != "" {
		args = append(args, "--script", req.ScriptPath)
	}

	// Flags
	if req.Test {
		args = append(args, "--test")
	}
	if req.Force {
		args = append(args, "--force")
	}
	if req.Verbose {
		args = append(args, "--verbose")
	}

	// Always add these to prevent interactive prompts
	args = append(args, "--closeonfinish", "--notaskscheduler")

	return args
}
