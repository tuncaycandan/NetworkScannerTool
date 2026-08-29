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

## Verification

- Release build completed successfully.
- The executable launched successfully on Windows during the startup test.
- SHA-256: `BADEE85C3612A01BF71D303CBD4E6659B660C28316F65B1B9E5E84380848C1C9`

## Installation

Download `NetworkScannerTool-v1.4.1.exe` and run it on a supported Windows system. No additional resource folder is required.

## Known Notes

The application targets .NET Framework 4.8 and is intended for supported Windows systems.
