# Development signing

Private certificates must never be committed.

Create a local test certificate once per machine:

```powershell
New-SelfSignedCertificate `
  -Type Custom `
  -Subject "CN=BuildnBits-Dev" `
  -KeyUsage DigitalSignature `
  -FriendlyName "BuildnBits Usage Dev" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

Export the public `.cer` for sideload trust if needed, and keep the `.pfx` outside the repository.

Set `AppxPackageSigningEnabled` to `true` locally and point `PackageCertificateKeyFile` at your untracked PFX, or sign with `signtool` after packing.

The packaged identity publisher is `CN=BuildnBits-Dev` so it matches this development certificate subject.
