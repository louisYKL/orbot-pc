# Build information

- Product: Orbot PC
- Version: 0.2.0
- Target: Windows x64, .NET 8 WPF
- Runtime: Tor Expert Bundle 16.0.1, Tor 0.4.9.16
- Runtime SHA-256: `9eed3c3ff1fe43df034ce360196c9ec95472560d77ff55614d0cd5120edea27b`
- Runtime source: `https://dist.torproject.org/torbrowser/16.0.1/tor-expert-bundle-windows-x86_64-16.0.1.tar.gz`
- Startup: `requireAdministrator`; clicking Connect starts Tor, and Windows HTTP/HTTPS proxy is enabled automatically after Tor bootstrap reaches 100% (and restored on stop/exit).
- Bootstrap failure handling: five minutes without progress or fifteen minutes overall stops the attempt and reports the network/bridge phase instead of showing an endless connecting state.
- Windows UI language: Chinese, Russian, or English fallback selected from the current Windows UI culture.
- Project-local paths: `.cache`, `.dotnet`, `.nuget`, `.tmp`, `.build`, `.data`, and `runtime`.
