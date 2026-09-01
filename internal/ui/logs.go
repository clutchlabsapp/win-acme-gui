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

// maxLogChars bounds the log buffer. wacs.exe can emit a lot of output in
// verbose mode, and an unbounded TextEdit degrades the whole UI.
const maxLogChars = 512 * 1024

// Append adds timestamped text to the log viewer, one entry per input line.
// Must be called on the UI thread; background goroutines go through
// App.appendLog, which marshals via Synchronize.
func (l *LogsTab) Append(text string) {
	if l.teLog == nil || text == "" {
		return
	}

	// Build the whole block first: appending per line would re-copy the
	// existing buffer each time, which is quadratic in the log size.
	timestamp := time.Now().Format("15:04:05")
	var b strings.Builder
	for _, line := range strings.Split(text, "\n") {
		if line = strings.TrimRight(line, "\r"); line == "" {
			continue
		}
		b.WriteString("[")
		b.WriteString(timestamp)
		b.WriteString("] ")
		b.WriteString(line)
		b.WriteString("\r\n")
	}
	if b.Len() == 0 {
		return
	}

	if l.teLog.TextLength()+b.Len() > maxLogChars {
		l.trim()
	}
	l.teLog.AppendText(b.String())
}

// trim drops the oldest half of the buffer, keeping the log bounded while
// preserving recent context. It cuts on a line boundary so entries stay intact.
func (l *LogsTab) trim() {
	text := l.teLog.Text()
	cut := len(text) / 2
	if idx := strings.IndexByte(text[cut:], '\n'); idx >= 0 {
		cut += idx + 1
	}
	l.teLog.SetText("[log truncated]\r\n" + text[cut:])
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
	if text == "" {
		return
	}
	if err := walk.Clipboard().SetText(text); err != nil {
		ShowError(l.teLog.Form(), "Copy Failed",
			"Could not write the log to the clipboard:\n"+err.Error())
	}
}
