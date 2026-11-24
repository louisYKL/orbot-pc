# Orbot Windows Port Status

This project is a Windows client for the portable Orbot/Tor control and local-proxy surface. Windows system routing is intentionally outside this port's scope.

Implemented; locally verified with build, integration checks, and binary argument/version probes:

- Tor Expert Bundle startup, cookie-authenticated control, bootstrap monitoring, clean shutdown, New Identity, and current-user proxy restore.
- Direct Tor, obfs4, Snowflake, Snowflake AMP, WebTunnel/custom bridge lines, DNSTT, Smart Connect fallback, and a manual one-client Snowflake volunteer relay. Snowflake and DNSTT Windows binaries, managed-transport configuration, and CLI probes are verified locally; broker/WebRTC relay and live DNSTT service behavior were not tested.
- Moat API request/response handling through the bundled lyrebird `meek_lite` managed transport and its local SOCKS5 endpoint.
- Tor advanced listeners and preferences that the bundled Windows Tor accepts, SOCKS isolation, padding, IPv6 preferences, and outbound HTTPS/SOCKS5 proxy settings.
- v3 Onion Service mappings and validated configuration backup/restore without exporting private keys.
- v3 Onion client authorization import, protected storage, listing, export, and removal.
- Chinese, Russian, and English fallback UI localization and notification-area lifecycle.

Windows scope and remaining upstream-only features:

- Android quick settings/foreground-service equivalents and other Android-only system integrations.

The Windows client deliberately does not install a kernel driver or service. It provides Tor local proxies and Windows user proxy integration; applications must use the SOCKS5 endpoint or honor the Windows user HTTP/HTTPS proxy. The 0.3.0 verification covers local build/integration tests and runtime binary probes; no real Tor bootstrap, Snowflake broker/WebRTC relay, or physical-device test was run.
