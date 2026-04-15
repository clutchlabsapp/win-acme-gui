package ui

import (
	"context"
	"strconv"
	"time"

	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
	"github.com/clutchlabsapp/win-acme-gui/internal/service"
)

// DashboardTab manages the dashboard overview tab.
type DashboardTab struct {
	svc       service.WacsService
	setStatus StatusFunc
	appendLog LogFunc

	lblTotalCerts    *walk.Label
	lblDueSoon       *walk.Label
	lblExpired       *walk.Label
	lblLastActivity  *walk.Label
	lblWacsVersion   *walk.Label
	lblWacsPath      *walk.Label
	teRecentActivity *walk.TextEdit
}

// NewDashboardTab creates a new DashboardTab.
func NewDashboardTab(svc service.WacsService, setStatus StatusFunc, appendLog LogFunc) *DashboardTab {
	return &DashboardTab{
		svc:       svc,
		setStatus: setStatus,
		appendLog: appendLog,
	}
}

// TabPageDef returns the declarative definition for the Dashboard tab.
func (d *DashboardTab) TabPageDef() TabPage {
	return TabPage{
		Title:  "Dashboard",
		Layout: VBox{},
		Children: []Widget{
			GroupBox{
				Title:  "Certificate Overview",
				Layout: Grid{Columns: 4},
				Children: []Widget{
					Label{Text: "Total Certificates:", Font: Font{Bold: true}},
					Label{AssignTo: &d.lblTotalCerts, Text: "..."},
					Label{Text: "Due for Renewal:", Font: Font{Bold: true}},
					Label{AssignTo: &d.lblDueSoon, Text: "..."},
					Label{Text: "Expired:", Font: Font{Bold: true}},
					Label{AssignTo: &d.lblExpired, Text: "..."},
					Label{Text: "Last Activity:", Font: Font{Bold: true}},
					Label{AssignTo: &d.lblLastActivity, Text: "..."},
				},
			},
			GroupBox{
				Title:  "Win-ACME Info",
				Layout: Grid{Columns: 2},
				Children: []Widget{
					Label{Text: "Version:", Font: Font{Bold: true}},
					Label{AssignTo: &d.lblWacsVersion, Text: "..."},
					Label{Text: "Path:", Font: Font{Bold: true}},
					Label{AssignTo: &d.lblWacsPath, Text: "..."},
				},
			},
			GroupBox{
				Title:  "Quick Actions",
				Layout: HBox{},
				Children: []Widget{
					PushButton{
						Text:      "Renew All Due",
						OnClicked: d.onRenewAllClicked,
						MinSize:   Size{Width: 120},
					},
					PushButton{
						Text:      "Refresh",
						OnClicked: func() { d.Refresh() },
						MinSize:   Size{Width: 120},
					},
					HSpacer{},
				},
			},
			GroupBox{
				Title:  "Recent Output",
				Layout: VBox{},
				Children: []Widget{
					TextEdit{
						AssignTo: &d.teRecentActivity,
						ReadOnly: true,
						VScroll:  true,
						Font:     Font{Family: "Consolas", PointSize: 9},
						MinSize:  Size{Height: 150},
					},
				},
			},
		},
	}
}

// Refresh loads current renewal data and updates the dashboard.
func (d *DashboardTab) Refresh() {
	d.setStatus("Refreshing dashboard...")

	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
		defer cancel()

		// Get version info
		version, verErr := d.svc.Version(ctx)

		// Get renewal list
		renewals, output, listErr := d.svc.ListRenewals(ctx)
		if output != "" {
			d.appendLog(output)
		}

		// Update UI on the main thread
		d.syncUI(func() {
			// Version
			if verErr != nil {
				d.lblWacsVersion.SetText("Error: " + verErr.Error())
			} else {
				d.lblWacsVersion.SetText(version)
			}
			d.lblWacsPath.SetText(d.svc.WacsExePath())

			// Renewal stats
			if listErr != nil {
				d.lblTotalCerts.SetText("Error")
				d.setStatus("Error loading renewals: " + listErr.Error())
				d.teRecentActivity.SetText("Error: " + listErr.Error())
				return
			}

			dueSoon := 0
			expired := 0
			var lastActivity time.Time
			for _, r := range renewals {
				switch r.Status {
				case model.RenewalStatusDueSoon:
					dueSoon++
				case model.RenewalStatusExpired:
					expired++
				}
				if r.LastRenewal.After(lastActivity) {
					lastActivity = r.LastRenewal
				}
			}

			d.lblTotalCerts.SetText(strconv.Itoa(len(renewals)))
			d.lblDueSoon.SetText(strconv.Itoa(dueSoon))
			d.lblExpired.SetText(strconv.Itoa(expired))

			if lastActivity.IsZero() {
				d.lblLastActivity.SetText("Never")
			} else {
				d.lblLastActivity.SetText(lastActivity.Format("2006-01-02 15:04"))
			}

			if output != "" {
				d.teRecentActivity.SetText(output)
			}

			d.setStatus("Dashboard refreshed")
		})
	}()
}

func (d *DashboardTab) onRenewAllClicked() {
	if !ConfirmAction(d.lblTotalCerts.Form(), "Renew All",
		"Force renewal of all certificates?\n\nThis may take several minutes.") {
		return
	}

	d.setStatus("Renewing all certificates...")
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Minute)
		defer cancel()

		output, err := d.svc.RenewAll(ctx)
		if output != "" {
			d.appendLog(output)
		}

		d.syncUI(func() {
			if err != nil {
				ShowError(d.lblTotalCerts.Form(), "Renewal Error",
					"Renewal failed:\n"+err.Error())
				d.setStatus("Renewal failed")
			} else {
				ShowInfo(d.lblTotalCerts.Form(), "Renewal Complete",
					"All certificates have been renewed.")
				d.setStatus("Renewal complete")
			}
			d.teRecentActivity.SetText(output)
		})

		// Refresh dashboard data after renewal
		d.Refresh()
	}()
}

// syncUI runs the given function on the UI thread via Synchronize.
func (d *DashboardTab) syncUI(fn func()) {
	if d.lblTotalCerts != nil && d.lblTotalCerts.Form() != nil {
		d.lblTotalCerts.Form().Synchronize(fn)
	}
}
