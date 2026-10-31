# Network Scanner Tool v1.5.0

## Update reliability

This release improves the update workflow. After the updated executable is successfully placed and started, the temporary backup file created during replacement is automatically removed.

The backup is retained whenever the update or application launch fails, providing a recovery path instead of deleting the only fallback copy. Cleanup is retried several times to handle short-lived file locks safely.

## Included features

- MAC-based grouping for devices discovered through multiple Ethernet networks.
- Expand and collapse grouped records with persistent `+` and `-` indicators.
- Multi-network records preserved using the IP address and adapter/network identity.
- Quick device search, list/cards view, compact/normal density, column visibility, automatic column sizing, and status-based row colors.
- Turkish and English interface support.
- Standalone single-EXE distribution.

## Verification

Release configuration completed successfully. Assembly and file version: `1.5.0.0`.

SHA-256: `BUILD_SHA256`

## Compatibility

Existing users can use the built-in update workflow to install this release. The application targets .NET Framework 4.8 and is intended for supported Windows systems.
