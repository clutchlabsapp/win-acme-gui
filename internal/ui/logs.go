package ui

import (
	"strings"
	"time"

	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"
)

// LogsTab manages the log viewer tab.
type LogsTab struct {
	teLog *walk.TextEdit
}

// NewLogsTab creates a new LogsTab instance.
func NewLogsTab() *LogsTab {
	return &LogsTab{}
}

// TabPageDef returns the declarative definition for the Logs tab.
func (l *LogsTab) TabPageDef() TabPage {
	return TabPage{
		Title:  "Logs",
		Layout: VBox{},
		Children: []Widget{
			Composite{
				Layout: HBox{},
				Children: []Widget{
					PushButton{
						Text:      "Clear",
						OnClicked: l.onClearClicked,
					},
					PushButton{
						Text:      "Copy to Clipboard",
						OnClicked: l.onCopyClicked,
					},
					HSpacer{},
				},
			},
			TextEdit{
				AssignTo: &l.teLog,
				ReadOnly: true,
				VScroll:  true,
				HScroll:  true,
				Font:     Font{Family: "Consolas", PointSize: 9},
			},
		},
	}
}

// Append adds timestamped text to the log viewer.
// Safe to call from the UI thread (callers must use Synchronize from goroutines).
func (l *LogsTab) Append(text string) {
	if l.teLog == nil || text == "" {
		return
	}
	timestamp := time.Now().Format("15:04:05")
	for _, line := range strings.Split(text, "\n") {
		line = strings.TrimRight(line, "\r")
		if line == "" {
			continue
		}
		current := l.teLog.Text()
		entry := "[" + timestamp + "] " + line + "\r\n"
		newText := current + entry
		l.teLog.SetText(newText)
	}
	// Scroll to bottom
	textLen := len(l.teLog.Text())
	l.teLog.SetTextSelection(textLen, textLen)
}

func (l *LogsTab) onClearClicked() {
	if l.teLog != nil {
		l.teLog.SetText("")
	}
}

func (l *LogsTab) onCopyClicked() {
	if l.teLog == nil {
		return
	}
	text := l.teLog.Text()
	if text != "" {
		walk.Clipboard().SetText(text)
	}
}
