# Orbot Windows Port Status

This project is a Windows client for the portable Orbot/Tor control and local-proxy surface, with an optional manually started sing-box/Wintun TUN mode.

Implemented; locally verified with build, integration checks, and binary argument/version probes:

- Tor Expert Bundle startup, cookie-authenticated control, bootstrap monitoring, clean shutdown, New Identity, and current-user proxy restore.
- Direct Tor, obfs4, Snowflake, Snowflake AMP, WebTunnel/custom bridge lines, DNSTT, Smart Connect fallback, and a manual one-client Snowflake volunteer relay. Snowflake and DNSTT Windows binaries, managed-transport configuration, and CLI probes are verified locally; broker/WebRTC relay and live DNSTT service behavior were not tested.
- Moat API request/response handling through the bundled lyrebird `meek_lite` managed transport and its local SOCKS5 endpoint.
- Tor advanced listeners and preferences that the bundled Windows Tor accepts, SOCKS isolation, padding, IPv6 preferences, and outbound HTTPS/SOCKS5 proxy settings.
- v3 Onion Service mappings and validated configuration backup/restore without exporting private keys.
- v3 Onion client authorization import, protected storage, listing, export, and removal.
- Chinese, Russian, and English fallback UI localization and notification-area lifecycle.
- Optional Windows TUN configuration and lifecycle integration. sing-box config is parser-validated without starting an adapter. Tor and bridge executables route through the physical default interface, ordinary TCP routes through Tor SOCKS, DNS uses DoH over Tor, and UDP is blocked. An abnormal sing-box exit is surfaced as unconfirmed route cleanup and blocks another TUN start in the current app session. TUN itself is not live-adapter tested and remains experimental; abnormal-process-exit recovery is not verified.
- When no explicit Tor outbound proxy is configured, a reachable loopback SOCKS5 endpoint from current Windows proxy settings is detected and used; detection skips Orbot's own ports and requires a successful no-auth SOCKS5 handshake.

Windows scope and remaining upstream-only features:

- Android quick settings/foreground-service equivalents and other Android-only system integrations.

The TUN mode uses the official Wintun API alongside sing-box and does not install a persistent Windows service. It changes system routes while running, requires administrator rights, supports TCP only, and has not been tested on a live adapter. Verification includes local build/integration checks, config parsing without opening an adapter, and a real Tor bootstrap plus app/system proxy check. No Snowflake broker/WebRTC relay or DNSTT tunnel test was run.
