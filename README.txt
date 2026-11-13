FallenSec v1.2 - Firewall and network monitor for Windows
(c) 2026 Fallen - https://github.com/Fallemo/FallenSec
=========================================================

INSTALL
-------
1. Extract the FallenSec folder anywhere (e.g. C:\Programs\FallenSec).
   Keep the files together (FallenSec.exe + dlls + wwwroot).
2. Run FallenSec.exe. Windows asks for administrator rights (UAC);
   they are needed for blocking and for measuring speeds.
3. On first start the DB-IP geolocation database (~60 MB) is
   downloaded, then the map fills in.

Requirements: Windows 10/11 x64, .NET 8 Desktop Runtime, Microsoft Edge
WebView2 (both are already installed on most Windows 11 PCs).

The interface is in English or Romanian: it follows the Windows language
and can be changed in Settings > General > Language.

WHAT IT DOES
------------
- Map: every live connection, with animated arcs from you to each
  destination. Click a point for details and blocking.
- Apps: every program with download/upload speeds, connections,
  countries and an Internet ON/OFF switch. OFF cuts the app off
  instantly (connections that are already open are cut too).
- Connections: process, port, domain, IP, city, country, state, speed.
- Rules: detailed rules per app or for the whole PC, by domain
  (with subdomains), IP/CIDR, country, ports, protocol, direction.
- History: traffic charts, top apps, countries, destinations, ports,
  event log.
- Panic button: cuts all internet traffic, the LAN keeps working.
- Notification when an app goes online for the first time.

GOOD TO KNOW
------------
- Blocking goes directly through the Windows Filtering Platform (WFP)
  with persistent filters: they stay active after you close FallenSec
  and after a reboot. It works even when Windows Firewall is off, so it
  also runs alongside Bitdefender, ESET, Kaspersky and other firewalls.
- To remove all blocks: delete the rules on the Rules page (or switch
  the apps back ON) BEFORE you delete the app.
- The app is not code-signed, so SmartScreen or your antivirus may warn
  on first run: "More info" > "Run anyway".
- Network requests made by the app itself: the DB-IP database download
  and api.ipify.org (your public IP, for the map). Turn off
  "Detect my location automatically" in Settings to avoid the latter.
- Data (rules, settings, history) is stored in C:\ProgramData\FallenSec.

CREDITS
-------
IP Geolocation by DB-IP (https://db-ip.com), CC BY 4.0.
Map: Natural Earth / world-atlas. Charts: d3.js.
