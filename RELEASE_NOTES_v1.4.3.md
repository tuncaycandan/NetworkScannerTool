# Network Scanner Tool v1.4.3

## Bug Fixes

This patch release fixes duplicate device rows when **Scan all networks** is enabled across multiple Ethernet adapters. Target IP addresses are now deduplicated across adapter ranges before scanning, and a second IP-based guard prevents duplicate rows from reaching the device list.

The release also preserves the v1.4.2 interface improvements, including quick search, list/cards view, compact/normal density, automatic column sizing, column visibility controls, status-based row coloring, aligned top controls, and the collapsible details panel.

## Verification

The Release configuration completed successfully as a standalone executable. Assembly and file version: `1.4.3.0`.

SHA-256: `51BF6F80F44D3C393FC2D55886300053964D6E70B50A60E8FB36C91B61740FB1`

## Installation

Download `NetworkScannerTool.exe` and run it on a supported Windows system. Existing v1.4.2 users can use the built-in update flow to receive this patch.

## Compatibility

The application targets .NET Framework 4.8 and is intended for supported Windows systems.
