# Build information

- Product: Orbot PC
- Version: 0.3.1
- DNSTT runtime: `dnstt-client 1.0` built from `tladesignz/dnstt` commit `f1b9b97a269f83bad41d2ceef291b4d2c161cd11`; SHA-256 `C89597279632C39FE8A11960D5E1FBC8262632B8A460F6080B969BBA22D0BB6B`.
- Snowflake relay runtime: `snowflake-proxy 2.15.4` built from Tor Project commit `956d8308d6cd7518162e8fc962541035cde7e8a3`; SHA-256 `BB202325C7A7F6C8334A819B798975612F9F192594F9B8FB649CD1CC2EB29B41`.
- Target: Windows x64, .NET 8 WPF
- Runtime: Tor Expert Bundle 16.0.1, Tor 0.4.9.16
- Runtime SHA-256: `9eed3c3ff1fe43df034ce360196c9ec95472560d77ff55614d0cd5120edea27b`
- Runtime source: `https://dist.torproject.org/torbrowser/16.0.1/tor-expert-bundle-windows-x86_64-16.0.1.tar.gz`
- Optional Windows TUN: sing-box 1.14.4 amd64, SHA-256 `8F30682EE198855440D2A7FE070D5D3653793B2D01BB8C85EEA88F93BBE7C8B9`, with upstream source archive at `LICENSES/sing-box-v1.14.4-source.zip` and GPL-3.0-or-later license.
- Optional TUN adapter dependency: official Wintun 0.14.1 amd64 binary, SHA-256 `E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE`; prebuilt binary terms are in `LICENSES/WINTUN-PREBUILT-BINARIES-LICENSE.txt`.
- Startup: `requireAdministrator`; clicking Connect starts Tor, and Windows HTTP/HTTPS proxy is enabled automatically after Tor bootstrap reaches 100% (and restored on stop/exit).
- Bootstrap failure handling: five minutes without progress or fifteen minutes overall stops the attempt and reports the network/bridge phase instead of showing an endless connecting state.
- Windows UI language: Chinese, Russian, or English fallback selected from the current Windows UI culture.
- Orbot parity work: Snowflake AMP bridge generation and an opt-in one-client volunteer relay, native Windows Snowflake and DNSTT managed transports, bounded Smart Connect fallback, advanced Tor listeners/SOCKS isolation/padding/outbound proxy settings, Moat over the bundled lyrebird `meek_lite` managed transport, v3 Onion client authorization import/export/management, and validated Onion Service configuration backup/restore.
- Platform boundary: the Windows client offers local proxies and an optional manually started TUN mode. The TUN mode routes TCP via Tor, blocks UDP, and routes Tor/bridge processes through the physical default interface to prevent recursion. It requires administrator rights and has not been tested with a live adapter; abnormal-process-exit route recovery is unverified and it must not be described as production-verified. When no Tor upstream is configured, the app probes the enabled Windows proxy for a reachable loopback SOCKS5 endpoint and uses it only when its no-auth SOCKS handshake succeeds. Android-only system integrations remain outside this Windows client.
- Verification: Release self-contained publish, project integration tests, sing-box TUN configuration parser checks (without starting TUN), and bundled transport probes pass. A real Tor test reached 100%, the app-local and Windows system proxy both returned `IsTor=true`, and the original Windows proxy settings were restored byte-for-byte at the registry value level. No TUN adapter or route was activated.
- Project-local paths: `.cache`, `.dotnet`, `.nuget`, `.tmp`, `.build`, `.data`, and `runtime`.
