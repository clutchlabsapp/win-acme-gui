package ui

import (
	"context"
	"strings"
	"time"

	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
	"github.com/clutchlabsapp/win-acme-gui/internal/service"
)

// NewCertTab manages the certificate creation tab.
type NewCertTab struct {
	svc       service.WacsService
	setStatus StatusFunc
	appendLog LogFunc
	onCreated RefreshFunc

	// Source widgets
	cbSource       *walk.ComboBox
	leHosts        *walk.LineEdit
	leFriendlyName *walk.LineEdit
	leIISSiteID    *walk.LineEdit

	// Validation widgets
	cbValMode   *walk.ComboBox
	cbValMethod *walk.ComboBox

	// DNS credential widgets
	grpDNSCreds      *walk.GroupBox
	leCloudflareToken *walk.LineEdit
	leAWSAccessKey   *walk.LineEdit
	leAWSSecretKey   *walk.LineEdit
	leAcmeDNSServer  *walk.LineEdit

	// Store widgets
	cbStore   *walk.ComboBox
	lePemPath *walk.LineEdit
	lePfxPath *walk.LineEdit
	lePfxPass *walk.LineEdit

	// Installation widgets
	cbInstall       *walk.ComboBox
	leInstallSiteID *walk.LineEdit
	leScriptPath    *walk.LineEdit

	// Options
	chkTest    *walk.CheckBox
	chkVerbose *walk.CheckBox

	// Action
	btnCreate *walk.PushButton
	pbCreate  *walk.ProgressBar
}

// NewNewCertTab creates a new NewCertTab.
func NewNewCertTab(svc service.WacsService, setStatus StatusFunc, appendLog LogFunc, onCreated RefreshFunc) *NewCertTab {
	return &NewCertTab{
		svc:       svc,
		setStatus: setStatus,
		appendLog: appendLog,
		onCreated: onCreated,
	}
}

// TabPageDef returns the declarative definition for the New Certificate tab.
func (n *NewCertTab) TabPageDef() TabPage {
	return TabPage{
		Title:  "New Certificate",
		Layout: VBox{},
		Children: []Widget{
			ScrollView{
				Layout: VBox{},
				Children: []Widget{
					// Step 1: Domain Source
					GroupBox{
						Title:  "Step 1: Domain Source",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "Source Type:"},
							ComboBox{
								AssignTo:              &n.cbSource,
								Model:                 []string{"manual", "iis"},
								CurrentIndex:          0,
								OnCurrentIndexChanged: n.onSourceChanged,
							},
							Label{Text: "Hostnames (comma-separated):"},
							LineEdit{
								AssignTo:    &n.leHosts,
								CueBanner:   "example.com, www.example.com",
								ToolTipText: "Enter one or more domain names separated by commas",
							},
							Label{Text: "Friendly Name (optional):"},
							LineEdit{
								AssignTo:    &n.leFriendlyName,
								CueBanner:   "My Certificate",
								ToolTipText: "A descriptive name for this certificate",
							},
							Label{Text: "IIS Site ID (IIS source only):"},
							LineEdit{
								AssignTo:    &n.leIISSiteID,
								CueBanner:   "1",
								ToolTipText: "The IIS website ID to get bindings from",
								Enabled:     false,
							},
						},
					},

					// Step 2: Validation
					GroupBox{
						Title:  "Step 2: Validation",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "Validation Mode:"},
							ComboBox{
								AssignTo:              &n.cbValMode,
								Model:                 []string{"http-01", "dns-01", "tls-alpn-01"},
								CurrentIndex:          0,
								OnCurrentIndexChanged: n.onValModeChanged,
							},
							Label{Text: "Validation Method:"},
							ComboBox{
								AssignTo:     &n.cbValMethod,
								Model:        model.HTTPValidationMethods(),
								CurrentIndex: 0,
								OnCurrentIndexChanged: n.onValMethodChanged,
							},
						},
					},

					// DNS Credentials (shown only for DNS validation methods)
					GroupBox{
						AssignTo: &n.grpDNSCreds,
						Title:    "DNS Plugin Credentials",
						Layout:   Grid{Columns: 2},
						Visible:  false,
						Children: []Widget{
							Label{Text: "Cloudflare API Token:"},
							LineEdit{
								AssignTo:     &n.leCloudflareToken,
								PasswordMode: true,
								CueBanner:    "API token for Cloudflare DNS",
							},
							Label{Text: "AWS Access Key ID:"},
							LineEdit{
								AssignTo:  &n.leAWSAccessKey,
								CueBanner: "Route53 access key",
							},
							Label{Text: "AWS Secret Access Key:"},
							LineEdit{
								AssignTo:     &n.leAWSSecretKey,
								PasswordMode: true,
								CueBanner:    "Route53 secret key",
							},
							Label{Text: "acme-dns Server:"},
							LineEdit{
								AssignTo:  &n.leAcmeDNSServer,
								CueBanner: "https://auth.acme-dns.io",
							},
						},
					},

					// Step 3: Certificate Store
					GroupBox{
						Title:  "Step 3: Certificate Store",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "Store Type:"},
							ComboBox{
								AssignTo:              &n.cbStore,
								Model:                 []string{"certificatestore", "pemfiles", "pfxfile", "centralssl"},
								CurrentIndex:          0,
								OnCurrentIndexChanged: n.onStoreChanged,
							},
							Label{Text: "PEM Output Path:"},
							LineEdit{
								AssignTo:  &n.lePemPath,
								CueBanner: "C:\\certs\\",
								Enabled:   false,
							},
							Label{Text: "PFX Output Path:"},
							LineEdit{
								AssignTo:  &n.lePfxPath,
								CueBanner: "C:\\certs\\cert.pfx",
								Enabled:   false,
							},
							Label{Text: "PFX Password:"},
							LineEdit{
								AssignTo:     &n.lePfxPass,
								PasswordMode: true,
								Enabled:      false,
							},
						},
					},

					// Step 4: Installation
					GroupBox{
						Title:  "Step 4: Installation",
						Layout: Grid{Columns: 2},
						Children: []Widget{
							Label{Text: "Installation Target:"},
							ComboBox{
								AssignTo:              &n.cbInstall,
								Model:                 []string{"iis", "script", "none"},
								CurrentIndex:          0,
								OnCurrentIndexChanged: n.onInstallChanged,
							},
							Label{Text: "IIS Site ID:"},
							LineEdit{
								AssignTo:  &n.leInstallSiteID,
								CueBanner: "1",
							},
							Label{Text: "Script Path:"},
							LineEdit{
								AssignTo:  &n.leScriptPath,
								CueBanner: "C:\\scripts\\install-cert.ps1",
								Enabled:   false,
							},
						},
					},

					// Options
					GroupBox{
						Title:  "Options",
						Layout: HBox{},
						Children: []Widget{
							CheckBox{
								AssignTo: &n.chkTest,
								Text:     "Use Staging (Test Mode)",
								Checked:  true,
							},
							CheckBox{
								AssignTo: &n.chkVerbose,
								Text:     "Verbose Output",
							},
						},
					},
				},
			},

			// Bottom bar
			Composite{
				Layout:  HBox{},
				Children: []Widget{
					ProgressBar{
						AssignTo:    &n.pbCreate,
						MarqueeMode: true,
						Visible:     false,
						MinSize:     Size{Width: 200},
					},
					HSpacer{},
					PushButton{
						AssignTo:  &n.btnCreate,
						Text:      "Create Certificate",
						OnClicked: n.onCreateClicked,
						MinSize:   Size{Width: 160},
					},
				},
			},
		},
	}
}

func (n *NewCertTab) onSourceChanged() {
	isIIS := n.cbSource.CurrentIndex() == 1
	n.leIISSiteID.SetEnabled(isIIS)
	n.leHosts.SetEnabled(!isIIS)
}

func (n *NewCertTab) onValModeChanged() {
	var methods []string
	switch n.cbValMode.CurrentIndex() {
	case 0: // http-01
		methods = model.HTTPValidationMethods()
		n.grpDNSCreds.SetVisible(false)
	case 1: // dns-01
		methods = model.DNSValidationMethods()
		n.grpDNSCreds.SetVisible(true)
	case 2: // tls-alpn-01
		methods = model.TLSALPNValidationMethods()
		n.grpDNSCreds.SetVisible(false)
	}
	n.cbValMethod.SetModel(methods)
	if len(methods) > 0 {
		n.cbValMethod.SetCurrentIndex(0)
	}
}

func (n *NewCertTab) onValMethodChanged() {
	// Could enable/disable specific credential fields based on selected DNS method
}

func (n *NewCertTab) onStoreChanged() {
	idx := n.cbStore.CurrentIndex()
	n.lePemPath.SetEnabled(idx == 1) // pemfiles
	n.lePfxPath.SetEnabled(idx == 2) // pfxfile
	n.lePfxPass.SetEnabled(idx == 2)
}

func (n *NewCertTab) onInstallChanged() {
	idx := n.cbInstall.CurrentIndex()
	n.leInstallSiteID.SetEnabled(idx == 0) // iis
	n.leScriptPath.SetEnabled(idx == 1)    // script
}

func (n *NewCertTab) onCreateClicked() {
	// Validate required fields
	source := n.cbSource.Text()
	hosts := strings.TrimSpace(n.leHosts.Text())

	if source == "manual" && hosts == "" {
		ShowError(n.btnCreate.Form(), "Validation Error",
			"Please enter at least one hostname.")
		return
	}

	req := n.buildRequest()

	// Disable button, show progress
	n.btnCreate.SetEnabled(false)
	n.pbCreate.SetVisible(true)
	n.setStatus("Creating certificate...")

	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
		defer cancel()

		output, err := n.svc.CreateCertificate(ctx, req)
		if output != "" {
			n.appendLog(output)
		}

		n.syncUI(func() {
			n.btnCreate.SetEnabled(true)
			n.pbCreate.SetVisible(false)

			if err != nil {
				ShowError(n.btnCreate.Form(), "Certificate Creation Error",
					"Failed to create certificate:\n"+err.Error())
				n.setStatus("Certificate creation failed")
				return
			}

			ShowInfo(n.btnCreate.Form(), "Success",
				"Certificate created successfully!")
			n.setStatus("Certificate created")

			if n.onCreated != nil {
				n.onCreated()
			}
		})
	}()
}

func (n *NewCertTab) buildRequest() model.CertificateRequest {
	req := model.CertificateRequest{
		Source:       model.SourceType(n.cbSource.Text()),
		FriendlyName: n.leFriendlyName.Text(),
		Test:         n.chkTest.Checked(),
		Verbose:      n.chkVerbose.Checked(),
	}

	// Hosts
	if req.Source == model.SourceManual {
		hostsStr := n.leHosts.Text()
		for _, h := range strings.Split(hostsStr, ",") {
			h = strings.TrimSpace(h)
			if h != "" {
				req.Hosts = append(req.Hosts, h)
			}
		}
	}

	// IIS source site ID
	if req.Source == model.SourceIIS {
		if id, ok := parseIntField(n.leIISSiteID.Text()); ok {
			req.IISWebsiteID = id
		}
	}

	// Validation
	valModes := []model.ValidationMode{model.ValidationModeHTTP01, model.ValidationModeDNS01, model.ValidationModeTLSALPN}
	if idx := n.cbValMode.CurrentIndex(); idx >= 0 && idx < len(valModes) {
		req.ValidationMode = valModes[idx]
	}
	req.ValidationMethod = model.ValidationMethod(n.cbValMethod.Text())

	// DNS credentials
	req.CloudflareToken = n.leCloudflareToken.Text()
	req.AWSAccessKey = n.leAWSAccessKey.Text()
	req.AWSSecretKey = n.leAWSSecretKey.Text()
	req.AcmeDNSServer = n.leAcmeDNSServer.Text()

	// Store
	storeTypes := []model.StoreType{model.StoreCertificateStore, model.StorePemFiles, model.StorePfxFile, model.StoreCentralSSL}
	if idx := n.cbStore.CurrentIndex(); idx >= 0 && idx < len(storeTypes) {
		req.Store = storeTypes[idx]
	}
	req.PemPath = n.lePemPath.Text()
	req.PfxPath = n.lePfxPath.Text()
	req.PfxPassword = n.lePfxPass.Text()

	// Installation
	installTypes := []model.InstallationType{model.InstallationIIS, model.InstallationScript, model.InstallationNone}
	if idx := n.cbInstall.CurrentIndex(); idx >= 0 && idx < len(installTypes) {
		req.Installation = installTypes[idx]
	}
	if id, ok := parseIntField(n.leInstallSiteID.Text()); ok {
		req.IISInstallSiteID = id
	}
	req.ScriptPath = n.leScriptPath.Text()

	return req
}

func parseIntField(s string) (int, bool) {
	s = strings.TrimSpace(s)
	if s == "" {
		return 0, false
	}
	var n int
	for _, c := range s {
		if c < '0' || c > '9' {
			return 0, false
		}
		n = n*10 + int(c-'0')
	}
	return n, true
}

func (n *NewCertTab) syncUI(fn func()) {
	if n.btnCreate != nil && n.btnCreate.Form() != nil {
		n.btnCreate.Form().Synchronize(fn)
	}
}
