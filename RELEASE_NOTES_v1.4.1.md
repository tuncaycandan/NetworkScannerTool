# Network Scanner Tool v1.4.1

## Highlights

- Added CIDR notation support for easier IPv4 range input, such as `192.168.1.0/24`.
- Moved target ping and device creation logic into dedicated network services.
- Added dynamic scan concurrency based on CPU capacity and target count.
- Reduced UI update pressure during large scans.
- Hardened Turkish text rendering and removed mojibake from scan status messages.
- Preserved the standalone single-executable distribution model.
- Retained secure process execution and verified update installation flow.
- Fixed update installation for unsigned release executables by relying on the trusted GitHub SHA-256 asset digest after download verification.
- Added an Authenticode signature so legacy v1.4 clients can recognize the update package.
- Fixed the displayed version and update comparison to use the full `1.4.1` version instead of truncating it to `1.4`.
- Added a collapsible details panel so the device list can expand to the footer area and restore with the up arrow.
- Added quick search, list/cards view, compact/normal density, automatic column sizing, column visibility controls, and status-based row coloring.

## Verification

- Release build completed successfully.
- The executable launched successfully on Windows during the startup test.
- SHA-256: `4E4083542A2F6FBBFD916938646DB81DF4AC1984737D9147921312F70998EAA9`

## Installation

Download `NetworkScannerTool.exe` and run it on a supported Windows system. No additional resource folder is required.

## Known Notes

The application targets .NET Framework 4.8 and is intended for supported Windows systems.
