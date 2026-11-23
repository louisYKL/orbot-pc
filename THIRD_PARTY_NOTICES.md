# Third-party notices

This project uses the following upstream components:

- Guardian Project Orbot source and configuration semantics: BSD-3-Clause, source checkout in `E:\tvbox\_orbot_source`.
- Tor Expert Bundle for Windows x64 16.0.1, including Tor 0.4.9.16, lyrebird and conjure-client. The bundle's own documentation and notices are kept under `runtime\tor\docs`.
- .NET 8 WPF and the Windows registry/WinINet APIs supplied by Windows.

Orbot PC does not ship Android `VpnService`, Android AARs, Android `.so` libraries, or Android package-routing code. The Windows implementation uses the bundled Windows executables and Windows user proxy APIs.
