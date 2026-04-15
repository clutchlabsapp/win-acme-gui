package ui

import (
	"context"
	"fmt"
	"time"

	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
	"github.com/clutchlabsapp/win-acme-gui/internal/service"
)

// RenewalsTab manages the renewals list tab.
type RenewalsTab struct {
	svc       service.WacsService
	setStatus StatusFunc
	appendLog LogFunc

	tableView *walk.TableView
	tblModel  *RenewalTableModel
}

// NewRenewalsTab creates a new RenewalsTab.
func NewRenewalsTab(svc service.WacsService, setStatus StatusFunc, appendLog LogFunc) *RenewalsTab {
	return &RenewalsTab{
		svc:       svc,
		setStatus: setStatus,
		appendLog: appendLog,
		tblModel:  NewRenewalTableModel(),
	}
}

// TabPageDef returns the declarative definition for the Renewals tab.
func (r *RenewalsTab) TabPageDef() TabPage {
	return TabPage{
		Title:  "Renewals",
		Layout: VBox{},
		Children: []Widget{
			Composite{
				Layout: HBox{},
				Children: []Widget{
					PushButton{
						Text:      "Refresh",
						OnClicked: func() { r.Refresh() },
						MinSize:   Size{Width: 100},
					},
					PushButton{
						Text:      "Renew Selected",
						OnClicked: r.onRenewClicked,
						MinSize:   Size{Width: 120},
					},
					PushButton{
						Text:      "Cancel Selected",
						OnClicked: r.onCancelClicked,
						MinSize:   Size{Width: 120},
					},
					PushButton{
						Text:      "Revoke Selected",
						OnClicked: r.onRevokeClicked,
						MinSize:   Size{Width: 120},
					},
					HSpacer{},
				},
			},
			TableView{
				AssignTo:            &r.tableView,
				AlternatingRowBG:    true,
				ColumnsOrderable:    true,
				MultiSelection:      false,
				LastColumnStretched: true,
				Model:               r.tblModel,
				Columns: []TableViewColumn{
					{Title: "Friendly Name", Width: 180},
					{Title: "Hosts", Width: 220},
					{Title: "Status", Width: 80},
					{Title: "Last Renewed", Width: 130},
					{Title: "Next Due", Width: 130},
					{Title: "Validation", Width: 100},
					{Title: "Thumbprint", Width: 120},
				},
				StyleCell: r.styleCell,
			},
		},
	}
}

// Refresh loads the current renewal list from wacs.exe.
func (r *RenewalsTab) Refresh() {
	r.setStatus("Loading renewals...")

	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
		defer cancel()

		renewals, output, err := r.svc.ListRenewals(ctx)
		if output != "" {
			r.appendLog(output)
		}

		r.syncUI(func() {
			if err != nil {
				r.setStatus("Error loading renewals: " + err.Error())
				return
			}
			r.tblModel.SetItems(renewals)
			r.setStatus(fmt.Sprintf("Loaded %d renewal(s)", len(renewals)))
		})
	}()
}

func (r *RenewalsTab) selectedRenewal() *model.Renewal {
	if r.tableView == nil {
		return nil
	}
	idx := r.tableView.CurrentIndex()
	return r.tblModel.ItemAt(idx)
}

func (r *RenewalsTab) onRenewClicked() {
	renewal := r.selectedRenewal()
	if renewal == nil {
		ShowError(r.tableView.Form(), "No Selection", "Please select a renewal to renew.")
		return
	}

	if !ConfirmAction(r.tableView.Form(), "Renew Certificate",
		fmt.Sprintf("Force renewal of '%s'?", renewal.FriendlyName)) {
		return
	}

	r.setStatus(fmt.Sprintf("Renewing '%s'...", renewal.FriendlyName))
	name := renewal.FriendlyName

	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
		defer cancel()

		output, err := r.svc.RenewSingle(ctx, name)
		if output != "" {
			r.appendLog(output)
		}

		r.syncUI(func() {
			if err != nil {
				ShowError(r.tableView.Form(), "Renewal Error",
					fmt.Sprintf("Failed to renew '%s':\n%s", name, err.Error()))
				r.setStatus("Renewal failed")
			} else {
				ShowInfo(r.tableView.Form(), "Renewal Complete",
					fmt.Sprintf("'%s' has been renewed.", name))
				r.setStatus("Renewal complete")
			}
		})

		r.Refresh()
	}()
}

func (r *RenewalsTab) onCancelClicked() {
	renewal := r.selectedRenewal()
	if renewal == nil {
		ShowError(r.tableView.Form(), "No Selection", "Please select a renewal to cancel.")
		return
	}

	if !ConfirmAction(r.tableView.Form(), "Cancel Renewal",
		fmt.Sprintf("Cancel automatic renewal of '%s'?\n\nThis will stop future renewals but won't revoke the certificate.", renewal.FriendlyName)) {
		return
	}

	r.setStatus(fmt.Sprintf("Cancelling '%s'...", renewal.FriendlyName))
	name := renewal.FriendlyName

	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
		defer cancel()

		output, err := r.svc.Cancel(ctx, name)
		if output != "" {
			r.appendLog(output)
		}

		r.syncUI(func() {
			if err != nil {
				ShowError(r.tableView.Form(), "Cancel Error",
					fmt.Sprintf("Failed to cancel '%s':\n%s", name, err.Error()))
			} else {
				ShowInfo(r.tableView.Form(), "Cancelled",
					fmt.Sprintf("Renewal for '%s' has been cancelled.", name))
			}
		})

		r.Refresh()
	}()
}

func (r *RenewalsTab) onRevokeClicked() {
	renewal := r.selectedRenewal()
	if renewal == nil {
		ShowError(r.tableView.Form(), "No Selection", "Please select a renewal to revoke.")
		return
	}

	if !ConfirmAction(r.tableView.Form(), "Revoke Certificate",
		fmt.Sprintf("REVOKE the certificate for '%s'?\n\nThis will invalidate the certificate immediately. This action cannot be undone.", renewal.FriendlyName)) {
		return
	}

	r.setStatus(fmt.Sprintf("Revoking '%s'...", renewal.FriendlyName))
	name := renewal.FriendlyName

	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
		defer cancel()

		output, err := r.svc.Revoke(ctx, name)
		if output != "" {
			r.appendLog(output)
		}

		r.syncUI(func() {
			if err != nil {
				ShowError(r.tableView.Form(), "Revoke Error",
					fmt.Sprintf("Failed to revoke '%s':\n%s", name, err.Error()))
			} else {
				ShowInfo(r.tableView.Form(), "Revoked",
					fmt.Sprintf("Certificate for '%s' has been revoked.", name))
			}
		})

		r.Refresh()
	}()
}

// styleCell applies color coding to the Status column based on renewal state.
func (r *RenewalsTab) styleCell(style *walk.CellStyle) {
	if style.Col() != 2 { // Status column
		return
	}
	item := r.tblModel.ItemAt(style.Row())
	if item == nil {
		return
	}
	switch item.Status {
	case model.RenewalStatusOK:
		style.TextColor = walk.RGB(0, 128, 0) // Green
	case model.RenewalStatusDueSoon:
		style.TextColor = walk.RGB(200, 150, 0) // Orange
	case model.RenewalStatusExpired:
		style.TextColor = walk.RGB(200, 0, 0) // Red
	case model.RenewalStatusError:
		style.TextColor = walk.RGB(200, 0, 0) // Red
	}
}

func (r *RenewalsTab) syncUI(fn func()) {
	if r.tableView != nil && r.tableView.Form() != nil {
		r.tableView.Form().Synchronize(fn)
	}
}
