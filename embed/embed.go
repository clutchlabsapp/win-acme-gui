// Package embed bundles the win-acme distribution into the compiled binary.
//
// The archive itself lives in dist/wacs.zip and is downloaded at build time by
// the Makefile rather than committed to git. Embedding the whole dist directory
// (which always contains at least a README) keeps `go build` and `go vet`
// working on a fresh clone; the absence of the archive is reported at runtime
// with an actionable message instead of breaking compilation.
package embed

import (
	"embed"
	"errors"
)

//go:embed dist
var distFS embed.FS

// ErrNotBundled indicates the binary was compiled without the win-acme archive.
var ErrNotBundled = errors.New(
	"this build does not bundle win-acme: run 'make deps' to download it, then rebuild")

// WacsZip returns the embedded win-acme release archive.
// It returns ErrNotBundled if the binary was built without the archive present.
func WacsZip() ([]byte, error) {
	data, err := distFS.ReadFile("dist/wacs.zip")
	if err != nil {
		return nil, ErrNotBundled
	}
	return data, nil
}

// IsBundled reports whether the win-acme archive was compiled into this binary.
func IsBundled() bool {
	_, err := distFS.ReadFile("dist/wacs.zip")
	return err == nil
}
