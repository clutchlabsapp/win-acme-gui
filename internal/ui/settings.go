package ui

import (
	"strconv"

	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"

	"github.com/clutchlabsapp/win-acme-gui/internal/config"
	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

// SettingsTab manages the settings tab.
type SettingsTab struct {
	setStatus StatusFunc
	wacsPath  string

	// ACME Provider
	leBaseURI *walk.LineEdit
	leEmail   *walk.LineEdit

	// Paths
	leConfigPath *walk.LineEdit
	lblWacsPath  *walk.Label

	// Renewal
	leRenewalDays *walk.LineEdit

	// SMTP
	leSMTPServer *walk.LineEdit
	leSMTPPort   *walk.LineEdit
	leSMTPUser   *walk.LineEdit
	leSMTPPass   *walk.LineEdit
	leEmailFrom  *walk.LineEdit
	leEmailTo    *walk.LineEdit

	// Defaults
	chkTestMode *walk.CheckBox
	chkVerbose  *walk.CheckBox
}

// NewSettingsTab creates a new SettingsTab.
func NewSettingsTab(setStatus StatusFunc, wacsPath string) *SettingsTab {
	return &SettingsTab{
		setStatus: setStatus,
		wacsPath:  wacsPath,
	}
}

// TabPageDef returns the declarative definition for the Settings tab.
func (s *SettingsTab) TabPageDef() TabPage {
	settings := config.Load()

	return TabPage{
		Title:  "Settings",
		Layout: VBox{},
		Children: []Widget{
			ScrollView{
				Layout: VBox{},
				Children: []Widget{
					GroupBox{
						Title:  "ACME Provider",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "ACME Server URI:"},
							LineEdit{
								AssignTo:  &s.leBaseURI,
								Text:      settings.ACMEBaseURI,
								CueBanner: "https://acme-v02.api.letsencrypt.org/",
							},
							Label{Text: "Account Email:"},
							LineEdit{
								AssignTo:  &s.leEmail,
								Text:      settings.AccountEmail,
								CueBanner: "admin@example.com",
							},
						},
					},
					GroupBox{
						Title:  "Paths",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "Win-ACME Config Path:"},
							LineEdit{
								AssignTo:  &s.leConfigPath,
								Text:      settings.ConfigPath,
								CueBanner: "%ProgramData%\\win-acme",
							},
							Label{Text: "Win-ACME Executable:"},
							Label{
								AssignTo: &s.lblWacsPath,
								Text:     s.wacsPath,
							},
						},
					},
					GroupBox{
						Title:  "Renewal",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "Renewal Days Before Expiry:"},
							LineEdit{
								AssignTo:  &s.leRenewalDays,
								Text:      strconv.Itoa(settings.RenewalDays),
								MaxLength: 3,
							},
						},
					},
					GroupBox{
						Title:  "Email Notifications (SMTP)",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "SMTP Server:"},
							LineEdit{
								AssignTo: &s.leSMTPServer,
								Text:     settings.SMTPServer,
							},
							Label{Text: "SMTP Port:"},
							LineEdit{
								AssignTo:  &s.leSMTPPort,
								Text:      strconv.Itoa(settings.SMTPPort),
								MaxLength: 5,
							},
							Label{Text: "Username:"},
							LineEdit{
								AssignTo: &s.leSMTPUser,
								Text:     settings.SMTPUser,
							},
							Label{Text: "Password:"},
							LineEdit{
								AssignTo:     &s.leSMTPPass,
								PasswordMode: true,
								Text:         settings.SMTPPassword,
							},
							Label{Text: "From Address:"},
							LineEdit{
								AssignTo:  &s.leEmailFrom,
								Text:      settings.EmailFrom,
								CueBanner: "certs@example.com",
							},
							Label{Text: "To Address:"},
							LineEdit{
								AssignTo:  &s.leEmailTo,
								Text:      settings.EmailTo,
								CueBanner: "admin@example.com",
							},
						},
					},
					GroupBox{
						Title:  "Defaults",
						Layout: HBox{},
						Children: []Widget{
							CheckBox{
								AssignTo: &s.chkTestMode,
								Text:     "Use Staging by Default",
								Checked:  settings.UseTestMode,
							},
							CheckBox{
								AssignTo: &s.chkVerbose,
								Text:     "Verbose Output by Default",
								Checked:  settings.VerboseMode,
							},
						},
					},
				},
			},
			Composite{
				Layout: HBox{},
				Children: []Widget{
					HSpacer{},
					PushButton{
						Text:      "Save Settings",
						OnClicked: s.onSaveClicked,
						MinSize:   Size{Width: 130},
					},
				},
			},
		},
	}
}

func (s *SettingsTab) onSaveClicked() {
	settings := s.readForm()

	if err := config.Save(settings); err != nil {
		ShowError(s.leBaseURI.Form(), "Save Error",
			"Failed to save settings:\n"+err.Error())
		s.setStatus("Error saving settings")
		return
	}

	ShowInfo(s.leBaseURI.Form(), "Settings Saved",
		"Settings have been saved successfully.")
	s.setStatus("Settings saved")
}

func (s *SettingsTab) readForm() *model.Settings {
	renewalDays, _ := strconv.Atoi(s.leRenewalDays.Text())
	if renewalDays <= 0 {
		renewalDays = 55
	}

	smtpPort, _ := strconv.Atoi(s.leSMTPPort.Text())
	if smtpPort <= 0 {
		smtpPort = 587
	}

	return &model.Settings{
		ACMEBaseURI:  s.leBaseURI.Text(),
		AccountEmail: s.leEmail.Text(),
		ConfigPath:   s.leConfigPath.Text(),
		RenewalDays:  renewalDays,
		SMTPServer:   s.leSMTPServer.Text(),
		SMTPPort:     smtpPort,
		SMTPUser:     s.leSMTPUser.Text(),
		SMTPPassword: s.leSMTPPass.Text(),
		EmailFrom:    s.leEmailFrom.Text(),
		EmailTo:      s.leEmailTo.Text(),
		UseTestMode:  s.chkTestMode.Checked(),
		VerboseMode:  s.chkVerbose.Checked(),
	}
}
