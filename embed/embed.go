package embed

import _ "embed"

// WacsZip contains the win-acme release archive.
// This file is downloaded by the Makefile and is not committed to git.
// Build will fail if embed/wacs.zip does not exist.
//
//go:embed wacs.zip
var WacsZip []byte
