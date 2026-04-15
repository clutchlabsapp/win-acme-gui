package model

// Settings represents configurable options for both win-acme and the GUI.
type Settings struct {
	// ACME Provider
	ACMEBaseURI  string `json:"acmeBaseUri"`
	ACMETestURI  string `json:"acmeTestUri"`
	AccountEmail string `json:"accountEmail"`

	// Paths
	ConfigPath string `json:"configPath"`

	// Renewal
	RenewalDays int `json:"renewalDays"`

	// SMTP Notifications
	SMTPServer   string `json:"smtpServer"`
	SMTPPort     int    `json:"smtpPort"`
	SMTPUser     string `json:"smtpUser"`
	SMTPPassword string `json:"smtpPassword"`
	EmailFrom    string `json:"emailFrom"`
	EmailTo      string `json:"emailTo"`

	// GUI Preferences
	UseTestMode bool `json:"useTestMode"`
	VerboseMode bool `json:"verboseMode"`
}
