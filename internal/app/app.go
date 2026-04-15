package app

import (
	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"

	"github.com/clutchlabsapp/win-acme-gui/internal/service"
	"github.com/clutchlabsapp/win-acme-gui/internal/ui"
)

// App is the main application orchestrator. It wires services to UI tabs
// and manages the main window lifecycle.
type App struct {
	svc        service.WacsService
	mainWindow *walk.MainWindow
	statusBar  *walk.StatusBarItem

	dashboard *ui.DashboardTab
	newCert   *ui.NewCertTab
	renewals  *ui.RenewalsTab
	settings  *ui.SettingsTab
	logs      *ui.LogsTab
}

// New creates a new App with all tabs initialized.
func New(svc service.WacsService) *App {
	a := &App{svc: svc}

	a.logs = ui.NewLogsTab()
	a.dashboard = ui.NewDashboardTab(svc, a.setStatus, a.appendLog)
	a.newCert = ui.NewNewCertTab(svc, a.setStatus, a.appendLog, a.onCertCreated)
	a.renewals = ui.NewRenewalsTab(svc, a.setStatus, a.appendLog)
	a.settings = ui.NewSettingsTab(a.setStatus, svc.WacsExePath())

	return a
}

// Run builds and runs the main window. Blocks until the window is closed.
func (a *App) Run() error {
	mw := MainWindow{
		AssignTo: &a.mainWindow,
		Title:    "Win-ACME Certificate Manager",
		MinSize:  Size{Width: 800, Height: 600},
		Size:     Size{Width: 1024, Height: 700},
		Layout:   VBox{MarginsZero: true},
		Children: []Widget{
			TabWidget{
				Pages: []TabPage{
					a.dashboard.TabPageDef(),
					a.newCert.TabPageDef(),
					a.renewals.TabPageDef(),
					a.settings.TabPageDef(),
					a.logs.TabPageDef(),
				},
			},
		},
		StatusBarItems: []StatusBarItem{
			{
				AssignTo: &a.statusBar,
				Text:     "Ready",
			},
		},
	}

	_, err := mw.Run()
	return err
}

// RefreshAll triggers a refresh of both the dashboard and renewals tabs.
// Called after the main window is fully initialized.
func (a *App) RefreshAll() {
	a.dashboard.Refresh()
	a.renewals.Refresh()
}

func (a *App) setStatus(text string) {
	if a.mainWindow != nil {
		a.mainWindow.Synchronize(func() {
			if a.statusBar != nil {
				a.statusBar.SetText(text)
			}
		})
	}
}

func (a *App) appendLog(text string) {
	if a.mainWindow != nil {
		a.mainWindow.Synchronize(func() {
			a.logs.Append(text)
		})
	}
}

func (a *App) onCertCreated() {
	a.dashboard.Refresh()
	a.renewals.Refresh()
}
