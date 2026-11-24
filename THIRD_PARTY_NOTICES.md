# Third-party notices

This project uses the following upstream components:

- Guardian Project Orbot source and configuration semantics: BSD-3-Clause, source checkout in `E:\tvbox\_orbot_source`.
- Tor Expert Bundle for Windows x64 16.0.1, including Tor 0.4.9.16, lyrebird and conjure-client. The bundle's own documentation and notices are kept under `runtime\tor\docs`.
- Tor Snowflake volunteer relay built from upstream Snowflake source at commit `956d8308d6cd7518162e8fc962541035cde7e8a3`, distributed under BSD-3-Clause; see `LICENSES\SNOWFLAKE-LICENSE.txt`.
- Snowflake and DNSTT Go transitive dependency license texts and notices are included under `LICENSES\snowflake-dependencies` and `LICENSES\dnstt-dependencies`.
- Tor DNSTT client built from upstream `tladesignz/dnstt` at commit `f1b9b97a269f83bad41d2ceef291b4d2c161cd11`, distributed under CC0; see `LICENSES\DNSTT-COPYING.txt` and bundled module notices under `LICENSES\dnstt-dependencies`.
- .NET 8 WPF and the Windows registry/WinINet APIs supplied by Windows.
- sing-box 1.14.4 Windows amd64 runtime, distributed under GPL-3.0-or-later with additional upstream branding conditions. The exact v1.14.4 source archive is included at `LICENSES\sing-box-v1.14.4-source.zip`; license text is `LICENSES\SING-BOX-LICENSE.txt`. Runtime executable SHA-256: `8F30682EE198855440D2A7FE070D5D3653793B2D01BB8C85EEA88F93BBE7C8B9`.
- Wintun 0.14.1 amd64 prebuilt runtime from wintun.net, used through its public API alongside sing-box; see `LICENSES\WINTUN-PREBUILT-BINARIES-LICENSE.txt`. DLL SHA-256: `E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE`.

Orbot PC does not ship Android `VpnService`, Android AARs, Android `.so` libraries, or Android package-routing code. The Windows implementation uses the bundled Windows executables and Windows user proxy APIs.
