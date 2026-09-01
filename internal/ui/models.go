package ui

import (
	"github.com/lxn/walk"

	"github.com/clutchlabsapp/win-acme-gui/internal/model"
)

// RenewalTableModel implements walk.TableModel for displaying renewals in a TableView.
type RenewalTableModel struct {
	walk.TableModelBase
	walk.SorterBase
	items []model.Renewal
}

// NewRenewalTableModel creates a new empty table model.
func NewRenewalTableModel() *RenewalTableModel {
	return &RenewalTableModel{}
}

// RowCount returns the number of rows.
func (m *RenewalTableModel) RowCount() int {
	return len(m.items)
}

// Value returns the value for the given row and column.
func (m *RenewalTableModel) Value(row, col int) interface{} {
	if row < 0 || row >= len(m.items) {
		return nil
	}
	item := m.items[row]
	switch col {
	case 0:
		return item.FriendlyName
	case 1:
		return item.HostsDisplay()
	case 2:
		return item.StatusText()
	case 3:
		if item.LastRenewal.IsZero() {
			return "Never"
		}
		return item.LastRenewal.Format("2006-01-02 15:04")
	case 4:
		if item.NextDue.IsZero() {
			return "N/A"
		}
		return item.NextDue.Format("2006-01-02 15:04")
	case 5:
		return item.ValidationType
	case 6:
		return item.LastThumbprint
	}
	return nil
}

// SetItems replaces the data and notifies the TableView to refresh.
func (m *RenewalTableModel) SetItems(items []model.Renewal) {
	m.items = items
	m.PublishRowsReset()
}

// ItemAt returns the renewal at the given row index, or nil if out of range.
func (m *RenewalTableModel) ItemAt(index int) *model.Renewal {
	if index < 0 || index >= len(m.items) {
		return nil
	}
	return &m.items[index]
}

// Items returns all current items.
func (m *RenewalTableModel) Items() []model.Renewal {
	return m.items
}
