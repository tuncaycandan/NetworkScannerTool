# Network Scanner Tool v1.5.3

## Release highlights

Version 1.5.3 improves large-network scanning, device identification, inventory tracking, and offline operation while preserving the lightweight standalone Windows application model.

## What's new

### Scan profiles

- Added **Fast**, **Balanced**, and **Deep** scan profiles.
- Scan concurrency is adjusted according to the selected profile, target count, and local system capacity.
- The existing adaptive worker-pool scanning model remains available for large IPv4 ranges.

### Device change tracking

- Scan summaries now report newly discovered, changed, and unreachable devices.
- Devices that were present in a previous scan but are no longer reachable are marked inactive.
- Device history records scan state changes for later review.
- Existing XML inventory storage is preserved.

### Multi-network device grouping

- Devices with the same normalized MAC address are grouped across multiple networks or adapters.
- The `+` and `-` indicators expand or collapse related IP and network records.
- Individual network records are preserved instead of being discarded as duplicates.

### Improved vendor and device detection

- Added a large IEEE OUI database in `data/oui.xml`.
- Added device signature rules in `data/device-signatures.xml`.
- Vendor detection uses the local OUI database before contacting the external vendor lookup service.
- Device type detection uses vendor, hostname, open TCP ports, required ports, and signature priorities.
- Detection works with the bundled data even when there is no Internet connection.

### GitHub data updates and offline fallback

- Device data can be updated from the repository's GitHub Raw files:
  - `data/oui.xml`
  - `data/device-signatures.xml`
- At startup, the application checks GitHub metadata before downloading XML files.
- Files are downloaded only when they are missing or a newer remote version is detected.
- Cached files are stored under `%LocalAppData%\\NetworkScannerTool`.
- If the Internet is unavailable, the application continues silently with the existing cache or bundled XML files.
- XML files are validated before replacement and are updated using temporary files and backup files to reduce the risk of data corruption.

### Accessibility and usability

- Improved accessible names, roles, and keyboard navigation for major controls.
- Improved list filtering, details, status display, and scan summary information.

## Compatibility

- Windows 10, Windows 11, and supported Windows Server versions.
- .NET Framework 4.8.
- Standalone executable distribution; no installer is required.

## Upgrade notes

- Existing inventory and settings remain compatible.
- The application may create the following files in `%LocalAppData%\\NetworkScannerTool`:
  - `oui.xml`
  - `oui.xml.meta`
  - `oui.xml.bak`
  - `device-signatures.xml`
  - `device-signatures.xml.meta`
  - `device-signatures.xml.bak`
- `.bak` files are temporary recovery copies created during a safe replacement.
- `.meta` files store remote version metadata used to avoid unnecessary downloads.

## Known limitations

- Vendor and device type detection depends on the available MAC address, hostname, and reachable TCP ports.
- Randomized or private MAC addresses may not map to a manufacturer.
- Some device actions require appropriate Windows permissions, firewall access, routing, or target credentials.
- GitHub-based data updates require Internet access; offline scanning continues with cached or bundled data.

## Download

Download the standalone executable from the [Network Scanner Tool v1.5.3 release page](https://github.com/tuncaycandan/NetworkScannerTool/releases/tag/v1.5.3).
