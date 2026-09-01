package ui

import "github.com/lxn/walk"

// StatusFunc is a callback to update the status bar text from any tab.
type StatusFunc func(text string)

// LogFunc is a callback to append text to the log viewer from any tab.
type LogFunc func(text string)

// RefreshFunc is a callback invoked when data changes require other tabs to refresh.
type RefreshFunc func()

// ShowError displays a modal error dialog.
func ShowError(owner walk.Form, title, message string) {
	walk.MsgBox(owner, title, message, walk.MsgBoxIconError)
}

// ShowInfo displays a modal information dialog.
func ShowInfo(owner walk.Form, title, message string) {
	walk.MsgBox(owner, title, message, walk.MsgBoxIconInformation)
}

// ConfirmAction displays a Yes/No confirmation dialog.
// Returns true if the user clicks Yes.
func ConfirmAction(owner walk.Form, title, message string) bool {
	return walk.MsgBox(owner, title, message,
		walk.MsgBoxYesNo|walk.MsgBoxIconQuestion) == walk.DlgCmdYes
}
