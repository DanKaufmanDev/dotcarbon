---
title: Updater artifacts
description: Sign release artifacts and publish manifests trusted by the Updater plugin.
---

Generate an ECDSA P-256 key pair once:

```bash
carbon signer generate --output carbon-updater.key
```

Commit or distribute the public key. Never commit the private key.

```json
{
  "bundle": {
    "updater": {
      "active": true,
      "createArtifacts": true,
      "endpoints": [
        "https://updates.example.com/{{target}}/{{version}}/{{artifact}}"
      ],
      "publicKey": "BASE64_SUBJECT_PUBLIC_KEY_INFO"
    }
  }
}
```

Provide the private PEM or its path through `CARBON_UPDATER_PRIVATE_KEY`, then bundle:

```bash
carbon bundle desktop --updater-artifacts
```

Carbon emits signatures and update JSON beside the package. Metadata includes version, target,
artifact name, URL, byte size, SHA-256, algorithm, public key, and ECDSA signature. The build fails if
the private key does not match the configured public key.

Upload the package, signature, and manifest without modifying them. The Updater plugin checks both
the digest and signature before launching an installer.
