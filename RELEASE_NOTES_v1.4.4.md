# Network Scanner Tool v1.4.4

## Highlights

This release improves multi-network scanning by grouping records that share the same MAC address. When the same physical device is discovered through multiple Ethernet networks, the device list shows one primary row with a `+` indicator. Clicking the indicator expands the group and reveals the other IP addresses and network records; clicking it again collapses the group.

The grouping logic keeps the existing quick search, device type filtering, list/cards view, compact/normal density, automatic column sizing, column visibility controls, status-based row colors, and collapsible details panel.

Target IP addresses are still deduplicated before scanning, while MAC-based grouping preserves separate network records when one physical device is reachable through more than one network.

## Verification

The Release configuration completed successfully as a standalone executable. Assembly and file version: `1.4.4.0`.

SHA-256: `BUILD_SHA256`

## Installation

Download `NetworkScannerTool.exe` and run it on a supported Windows system. Existing v1.4.3 users can use the built-in update flow to receive this release.

## Compatibility

The application targets .NET Framework 4.8 and is intended for supported Windows systems.
