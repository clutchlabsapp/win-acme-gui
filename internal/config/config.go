package config

import (
	"encoding/json"
	"os"
	"path/filepath"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

const configFileName = "settings.json"

// ConfigDir returns the application configuration directory.
func ConfigDir() (string, error) {
	dir, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "win-acme-gui"), nil
}

// Load reads settings from the config file. Returns defaults if the file
// does not exist or cannot be parsed.
func Load() *model.Settings {
	dir, err := ConfigDir()
	if err != nil {
		return DefaultSettings()
	}
	data, err := os.ReadFile(filepath.Join(dir, configFileName))
	if err != nil {
		return DefaultSettings()
	}
	var s model.Settings
	if err := json.Unmarshal(data, &s); err != nil {
		return DefaultSettings()
	}
	return &s
}

// Save persists settings to the config file.
func Save(s *model.Settings) error {
	dir, err := ConfigDir()
	if err != nil {
		return err
	}
	if err := os.MkdirAll(dir, 0755); err != nil {
		return err
	}
	data, err := json.MarshalIndent(s, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, configFileName), data, 0644)
}

// DefaultSettings returns sensible defaults for the application.
func DefaultSettings() *model.Settings {
	return &model.Settings{
		ACMEBaseURI: "https://acme-v02.api.letsencrypt.org/",
		ACMETestURI: "https://acme-staging-v02.api.letsencrypt.org/",
		RenewalDays: 55,
		SMTPPort:    587,
		UseTestMode: true,
	}
}
