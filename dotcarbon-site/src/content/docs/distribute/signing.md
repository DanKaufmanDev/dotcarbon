---
title: Signing & notarization
description: Configure platform signing without storing secrets in carbon.json.
---

`carbon.json` contains identifiers and non-secret signing references. Passwords, private keys, and
credential files belong in environment variables or the CI secret store.

## macOS

Set `bundle.macOS.signingIdentity` or `APPLE_SIGNING_IDENTITY`. Carbon signs the application with the
hardened runtime and signs the generated DMG.

Store notarization credentials in the keychain:

```bash
xcrun notarytool store-credentials carbon-notary \
  --apple-id "$APPLE_ID" \
  --team-id "$APPLE_TEAM_ID" \
  --password "$APPLE_APP_PASSWORD"
```

Set `bundle.macOS.notarizationProfile` or `APPLE_NOTARIZATION_PROFILE`. Carbon submits the DMG and
staples the accepted ticket. A configured signing, notarization, or staple failure fails the build.

## Windows

Set `bundle.windows.certificateThumbprint` or `WINDOWS_CERTIFICATE_THUMBPRINT`. The certificate must
exist in the runner certificate store. Carbon signs the application executable and MSI with signtool
and the configured timestamp URL.

## Android and iOS

Android keystore and key passwords come from `CARBON_ANDROID_KEYSTORE_PASSWORD` and
`CARBON_ANDROID_KEY_PASSWORD`. iOS uses identities and provisioning profiles installed in the macOS
keychain and provisioning directories.

Run the platform readiness checks before bundling:

```bash
carbon doctor signing
```
