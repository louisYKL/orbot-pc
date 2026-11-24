# Build information

- Product: Orbot PC
- Version: 0.3.0
- DNSTT runtime: `dnstt-client 1.0` built from `tladesignz/dnstt` commit `f1b9b97a269f83bad41d2ceef291b4d2c161cd11`; SHA-256 `C89597279632C39FE8A11960D5E1FBC8262632B8A460F6080B969BBA22D0BB6B`.
- Snowflake relay runtime: `snowflake-proxy 2.15.4` built from Tor Project commit `956d8308d6cd7518162e8fc962541035cde7e8a3`; SHA-256 `BB202325C7A7F6C8334A819B798975612F9F192594F9B8FB649CD1CC2EB29B41`.
- Target: Windows x64, .NET 8 WPF
- Runtime: Tor Expert Bundle 16.0.1, Tor 0.4.9.16
- Runtime SHA-256: `9eed3c3ff1fe43df034ce360196c9ec95472560d77ff55614d0cd5120edea27b`
- Runtime source: `https://dist.torproject.org/torbrowser/16.0.1/tor-expert-bundle-windows-x86_64-16.0.1.tar.gz`
- Startup: `requireAdministrator`; clicking Connect starts Tor, and Windows HTTP/HTTPS proxy is enabled automatically after Tor bootstrap reaches 100% (and restored on stop/exit).
- Bootstrap failure handling: five minutes without progress or fifteen minutes overall stops the attempt and reports the network/bridge phase instead of showing an endless connecting state.
- Windows UI language: Chinese, Russian, or English fallback selected from the current Windows UI culture.
- Orbot parity work: Snowflake AMP bridge generation and an opt-in one-client volunteer relay, native Windows Snowflake and DNSTT managed transports, bounded Smart Connect fallback, advanced Tor listeners/SOCKS isolation/padding/outbound proxy settings, Moat over the bundled lyrebird `meek_lite` managed transport, v3 Onion client authorization import/export/management, and validated Onion Service configuration backup/restore.
- Platform boundary: this WPF build uses local SOCKS5/HTTP proxies and does not install Windows TUN/WFP drivers, transparent routing, per-process routing, DNS interception, or a kill switch. Android-only system integrations remain outside this Windows client.
- Verification: Release self-contained publish, project integration tests, and bundled transport `-help`/version probes pass. The Snowflake volunteer supervisor is locally verified through binary probes; no live Tor bootstrap, Snowflake broker/WebRTC relay, DNSTT tunnel, or device test was run for this release.
- Project-local paths: `.cache`, `.dotnet`, `.nuget`, `.tmp`, `.build`, `.data`, and `runtime`.
