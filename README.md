# FallenSec

A Little Snitch-style firewall and network monitor for Windows 10/11.

See every connection your PC makes on a live world map, cut any app off the internet with one switch, and write detailed block rules by domain, IP, country or port.

> The interface is available in **English** and **Romanian** (it follows the Windows language and can be changed in Settings).

## Features

- **Live map** of all connections, with animated arcs from your location to each destination. Click a point to see details or block it.
- **Apps**: per-program download/upload speed, connections and countries, plus an **Internet ON/OFF** switch. Turning an app off also cuts its connections that are already open.
- **Connections**: process, ports, domain, IP, city, country, state and speed.
- **Rules** per app or for the whole PC: domain (subdomains included), IP / CIDR / range, country, ports, protocol and direction.
- **History**: traffic charts, plus the top apps, countries, destinations and ports, and an event log.
- **Panic button**: blocks all internet traffic while the local network keeps working.
- Notification when an app goes online for the first time.
- Runs from the system tray.

## How it works

- **Blocking** uses the **Windows Filtering Platform (WFP)** directly, through persistent filters under its own provider. It works even when Windows Firewall is disabled, so it can run next to Bitdefender, ESET and other security suites. A Windows Firewall rule backend is available as an option.
- **Traffic** is measured through **ETW** (Event Tracing for Windows).
- **Geolocation** works offline, with the free DB-IP Lite databases.
- **Data** (rules, settings, SQLite history) is stored in `C:\ProgramData\FallenSec`.

## Install

1. Download `FallenSec-vX.Y.zip` from [Releases](../../releases) and extract the folder anywhere. Keep its files together.
2. Run `FallenSec.exe` and accept the administrator (UAC) prompt, which is needed for blocking and traffic measurement.

Requirements: Windows 10/11 x64, the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) and Microsoft Edge WebView2 (included with Windows 11).

The executable is not code-signed, so SmartScreen or your antivirus may warn on the first run.

## Good to know

- **Blocks are persistent.** They stay active after FallenSec is closed and after a reboot. To undo everything, delete your rules (or switch the apps back ON) **before** you remove the app.
- **Network requests the app makes itself:**
  - It downloads the DB-IP Lite databases (~60 MB) from `download.db-ip.com` on first start and when they are refreshed.
  - It asks `api.ipify.org` for your public IP, to place "home" on the map. To stop this, turn off *Settings → Hartă → Detectează automat locația mea* and enter your location manually.
- There is no telemetry and no account. Nothing else leaves your PC.
- Use it at your own risk: this is a hobby project, not a security product.

## Build

```
dotnet publish -c Release -o publish
```

For a test build without elevation, add `-p:NoAdmin=true`. `FallenSec.exe --wfptest`, run elevated, self-tests the WFP backend and writes `%TEMP%\fallensec-wfptest.txt`.

## License

[MIT](LICENSE) © 2026 Fallen. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

*IP Geolocation by [DB-IP](https://db-ip.com).*
