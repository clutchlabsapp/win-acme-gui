package model

// SourceType identifies how domains are specified for certificate creation.
type SourceType string

const (
	SourceManual SourceType = "manual"
	SourceIIS    SourceType = "iis"
)

// ValidationMode is the ACME challenge type.
type ValidationMode string

const (
	ValidationModeHTTP01  ValidationMode = "http-01"
	ValidationModeDNS01   ValidationMode = "dns-01"
	ValidationModeTLSALPN ValidationMode = "tls-alpn-01"
)

// ValidationMethod identifies the specific validation plugin.
type ValidationMethod string

const (
	ValSelfHosting ValidationMethod = "selfhosting"
	ValFileSystem  ValidationMethod = "filesystem"
	ValFTP         ValidationMethod = "ftp"
	ValWebDav      ValidationMethod = "webdav"
	ValAcmeDNS     ValidationMethod = "acme-dns"
	ValCloudflare  ValidationMethod = "cloudflare"
	ValRoute53     ValidationMethod = "route53"
	ValAzure       ValidationMethod = "azure"
	ValManual      ValidationMethod = "manual"
)

// StoreType identifies where the certificate is stored.
type StoreType string

const (
	StoreCertificateStore StoreType = "certificatestore"
	StorePemFiles         StoreType = "pemfiles"
	StorePfxFile          StoreType = "pfxfile"
	StoreCentralSSL       StoreType = "centralssl"
)

// InstallationType identifies where the certificate is installed.
type InstallationType string

const (
	InstallationIIS    InstallationType = "iis"
	InstallationScript InstallationType = "script"
	InstallationNone   InstallationType = "none"
)

// CertificateRequest holds all parameters needed to create a new certificate
// via wacs.exe CLI in unattended mode.
type CertificateRequest struct {
	// Source
	Source       SourceType
	Hosts        []string
	FriendlyName string
	IISWebsiteID int

	// Validation
	ValidationMode   ValidationMode
	ValidationMethod ValidationMethod
	// DNS plugin credentials
	CloudflareToken string
	AWSAccessKey    string
	AWSSecretKey    string
	AWSRegion       string
	AcmeDNSServer   string

	// Store
	Store         StoreType
	PemPath       string
	PfxPath       string
	PfxPassword   string
	CertStoreName string

	// Installation
	Installation     InstallationType
	IISInstallSiteID int
	ScriptPath       string

	// Flags
	Test    bool
	Force   bool
	Verbose bool
}

// HTTPValidationMethods returns the validation methods available for HTTP-01.
func HTTPValidationMethods() []string {
	return []string{"selfhosting", "filesystem", "ftp", "webdav"}
}

// DNSValidationMethods returns the validation methods available for DNS-01.
func DNSValidationMethods() []string {
	return []string{"acme-dns", "cloudflare", "route53", "azure", "manual"}
}

// TLSALPNValidationMethods returns the validation methods available for TLS-ALPN-01.
func TLSALPNValidationMethods() []string {
	return []string{"selfhosting"}
}
