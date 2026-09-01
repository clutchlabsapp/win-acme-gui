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
	a.settings = ui.NewSettingsTab(a.setStatus, svc.WacsExePath(), a.onSettingsSaved)

	return a
}

// Run builds and shows the main window, then blocks until it is closed.
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

	// Create() builds the window and populates every AssignTo target; Run()
	// then starts the message loop. Splitting them lets us queue the initial
	// load against widgets that already exist. Doing this from a goroutine
	// before the window was built would race against widget construction.
	if err := mw.Create(); err != nil {
		return err
	}

	// Synchronize appends to a queue the message loop drains, so work queued
	// here runs on the UI thread as soon as Run() starts.
	a.mainWindow.Synchronize(a.RefreshAll)

	a.mainWindow.Run()
	return nil
}

// RefreshAll populates every tab with current data. Must be called on the UI
// thread; the Refresh methods hand off to background goroutines themselves.
func (a *App) RefreshAll() {
	a.newCert.ReloadSettings()
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

// onSettingsSaved propagates saved settings to the tabs that display them.
func (a *App) onSettingsSaved() {
	a.newCert.ReloadSettings()
}
