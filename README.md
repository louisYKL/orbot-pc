# Orbot PC

**Windows desktop client for Tor** based on the Tor Project's official Windows Expert Bundle. It provides local SOCKS5 and HTTP/HTTPS proxy endpoints and can apply them to the current Windows user's proxy settings after Tor connects.

> This is a Windows-specific desktop client, not an Android app port with Android APIs. It does not provide a system-wide VPN, transparent routing, per-process rules, or a kill switch.

## 中文

Orbot PC 是面向 Windows 的 Tor 桌面客户端，使用 Tor Project 官方 Windows Expert Bundle。用户点击“连接 Tor”后，程序启动 Tor 并等待网络引导成功；成功后启用本机 SOCKS5、HTTP/HTTPS 代理，并自动设置当前 Windows 用户的系统代理。连接与代理均可在应用中停止或恢复。

### 功能

- 本机回环地址 SOCKS5，以及支持 HTTP 和 HTTPS `CONNECT` 的本机代理。
- Tor 状态与引导进度监控、New Identity、Cookie 控制端口认证。
- 直连、内置 obfs4/Snowflake、WebTunnel/自定义桥接行和可选出口国家。
- Tor v3 Onion Service 本地端口映射。
- Windows 当前用户 HTTP/HTTPS 代理自动启用；停止 Tor 或退出程序时恢复此前保存的代理设置。
- 关闭窗口后驻留通知区域；右键托盘图标可重新打开窗口或退出。
- 端口冲突时自动选择可用的本机端口。
- 界面按 Windows 当前 UI 语言自动选择简体中文、俄语或英语（其它语言使用英语）。
- 引导无进展五分钟或总耗时十五分钟会自动结束连接尝试并显示当前阶段，避免无限停留在“正在连接”。

### 使用

1. 启动程序并在 Windows UAC 提示中允许管理员权限。管理员权限用于本程序的 Windows 适配与代理设置。
2. 在“桥接与网络”选择直连、obfs4、Snowflake 或配置桥接行。网络环境无法直连 Tor 时，可按需选择桥接。
3. 点击“连接 Tor”。待引导成功后，程序自动启用本机代理和当前 Windows 用户的 HTTP/HTTPS 代理。
4. 点击“断开连接”会停止 Tor、本机代理并恢复原有 Windows 代理设置。关闭窗口只会将程序隐藏到通知区域；要彻底退出，请右键托盘图标并选择“退出”。

本机端点默认使用 `127.0.0.1`。SOCKS5 可供支持 SOCKS5 的应用使用；Windows 系统代理采用 HTTP/HTTPS 端点，只有遵循 Windows 用户代理设置的应用会使用它。若 Tor 无法引导，请查看“运行日志”，检查当前网络、桥接模式和桥接行。

Windows 桥接传输会直接启动项目目录中的官方 pluggable transport；桥接程序路径和配置均由应用生成，连接能否完成仍取决于当前网络能否访问所选入口。

### Windows 能力边界

系统代理不是全局 VPN，也不会接管忽略 Windows 用户代理设置的应用、游戏、服务或 DNS 请求。程序目前不包含 Windows TUN/WFP 驱动、透明代理、按进程分流、DNS 防泄漏或 kill switch。Tor 和桥接可用性依赖当前网络；连接状态不代表匿名性或安全性保证。

### 构建

项目、运行时、SDK/NuGet 缓存、临时文件和输出目录均位于 `E:\tvbox\orbot-pc`。在 PowerShell 中：

```powershell
$root = 'E:\tvbox\orbot-pc'
$env:TEMP = "$root\.tmp"
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = "$root\.dotnet"
$env:NUGET_PACKAGES = "$root\.nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = "$root\.nuget\http-cache"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet publish "$root\orbot-pc.csproj" --configuration Release --runtime win-x64 --self-contained true --output "$root\.build\publish\win-x64"
```

### 运行时与许可证

随附的 Tor Expert Bundle 来自 [Tor Project](https://dist.torproject.org/torbrowser/16.0.1/tor-expert-bundle-windows-x86_64-16.0.1.tar.gz)。SHA-256：`9eed3c3ff1fe43df034ce360196c9ec95472560d77ff55614d0cd5120edea27b`。Tor 与其他运行时组件保留各自许可证；请一并保留 `LICENSES` 和运行时文档。

## English

Orbot PC is a Tor desktop client for Windows using the official Tor Project Windows Expert Bundle. When you select **Connect Tor**, the app starts Tor and waits for bootstrap to complete. It then enables the local SOCKS5 and HTTP/HTTPS proxies and applies the HTTP/HTTPS proxy to the current Windows user. You can stop Tor and restore the previous Windows proxy settings from the app.

### Features

- Loopback-only SOCKS5 and HTTP proxy endpoints; the HTTP proxy supports HTTPS `CONNECT` tunnels.
- Tor bootstrap/status monitoring, cookie-authenticated control port, and New Identity.
- Direct connections, bundled obfs4 and Snowflake bridges, WebTunnel/custom bridge lines, and an optional exit-country preference.
- Local port forwarding through Tor v3 Onion Services.
- Automatic current-user Windows HTTP/HTTPS proxy configuration after Tor bootstrap, with restoration of the saved settings when Tor stops or the app exits.
- Minimize-to-tray behavior; right-click the tray icon to reopen the window or exit.
- Automatic selection of available local ports when configured ports are occupied.
- UI language is selected from the Windows UI culture: Simplified Chinese, Russian, or English fallback.
- A five-minute no-progress timeout and fifteen-minute overall bootstrap timeout report the active phase instead of leaving the UI in an endless connecting state.

### Getting started

1. Start the app and approve the Windows UAC prompt. The application manifest requests administrator privileges for its Windows integration.
2. Choose Direct, obfs4, Snowflake, or configure bridge lines under **Bridges & Network**. Use a bridge when your network cannot reach Tor directly.
3. Select **Connect Tor**. The local proxies and current-user Windows HTTP/HTTPS proxy are enabled after bootstrap succeeds.
4. Select **Disconnect** to stop Tor and restore the previous Windows proxy settings. Closing the window hides the app in the notification area. To exit completely, right-click its tray icon and choose **Exit**.

Endpoints bind to `127.0.0.1`. Use SOCKS5 in applications that support it. The Windows system proxy uses the HTTP/HTTPS endpoint and affects only applications that honor the current user's Windows proxy settings. If bootstrap fails, inspect **Logs** and check the network and bridge configuration.

Bridge transports are launched from the bundled Tor Expert Bundle using Windows-compatible executable paths. Whether a bridge reaches the Tor network still depends on network access to the selected bridge.

### Windows limitations

The Windows system proxy is not a system-wide VPN. It does not capture applications, games, services, or DNS requests that ignore Windows user proxy settings. This project does not currently include a Windows TUN/WFP driver, transparent proxying, per-process routing, DNS leak protection, or a kill switch. Tor and bridge reachability depend on the network; a connected status is not a guarantee of anonymity or security.

### Build

The project, Tor runtime, SDK/NuGet caches, temporary files, and build output are kept under `E:\tvbox\orbot-pc`. See the PowerShell publish command in the Chinese section above, or run `build-local.ps1` from the project directory.

### Runtime and licenses

The bundled Tor Expert Bundle is from the [Tor Project](https://dist.torproject.org/torbrowser/16.0.1/tor-expert-bundle-windows-x86_64-16.0.1.tar.gz). SHA-256: `9eed3c3ff1fe43df034ce360196c9ec95472560d77ff55614d0cd5120edea27b`. Tor and other runtime components retain their respective licenses. Keep `LICENSES` and the runtime documentation with distributed builds.

## Русский

Orbot PC — настольный клиент Tor для Windows, использующий официальный Windows Expert Bundle проекта Tor. После нажатия **«Подключить Tor»** приложение запускает Tor и ожидает завершения подключения к сети. Затем оно включает локальные прокси SOCKS5 и HTTP/HTTPS и настраивает HTTP/HTTPS-прокси для текущего пользователя Windows. Остановить Tor и восстановить прежние параметры прокси можно в приложении.

### Возможности

- Локальные прокси SOCKS5 и HTTP, привязанные только к `127.0.0.1`; HTTP-прокси поддерживает туннели HTTPS `CONNECT`.
- Мониторинг состояния и подключения Tor, аутентификация управляющего порта через cookie, смена личности (New Identity).
- Прямое подключение, встроенные мосты obfs4 и Snowflake, WebTunnel/пользовательские строки мостов и необязательное предпочтение страны выхода.
- Перенаправление локальных портов через Onion Services v3.
- Автоматическая настройка HTTP/HTTPS-прокси текущего пользователя Windows после подключения Tor и восстановление сохранённых параметров при остановке Tor или выходе из приложения.
- Сворачивание в область уведомлений. Щёлкните правой кнопкой по значку, чтобы открыть окно или завершить работу.
- Автоматический выбор свободных локальных портов, если заданные порты заняты.
- Язык интерфейса выбирается по языку Windows: упрощённый китайский, русский или английский по умолчанию.
- Если подключение не продвигается пять минут или длится больше пятнадцати минут, попытка завершается с указанием текущего этапа вместо бесконечного ожидания.

### Начало работы

1. Запустите приложение и подтвердите запрос UAC Windows. Для интеграции с Windows манифест приложения запрашивает права администратора.
2. В разделе **«Мосты и сеть»** выберите прямое подключение, obfs4, Snowflake или задайте строки мостов. Используйте мост, если сеть не позволяет подключиться к Tor напрямую.
3. Нажмите **«Подключить Tor»**. После успешного подключения включатся локальные прокси и HTTP/HTTPS-прокси текущего пользователя Windows.
4. Нажмите **«Отключить»**, чтобы остановить Tor и восстановить прежние параметры прокси. Закрытие окна только скрывает приложение в области уведомлений. Для полного выхода нажмите правой кнопкой на значке и выберите **«Выход»**.

Прокси доступны только по адресу `127.0.0.1`. SOCKS5 можно указать в приложениях с поддержкой SOCKS5. Системный прокси Windows использует HTTP/HTTPS и действует только для программ, которые учитывают пользовательские настройки прокси Windows. Если подключение не удаётся, откройте раздел **«Журнал»** и проверьте сеть и параметры мостов.

Мосты запускаются из поставляемого Tor Expert Bundle с корректными для Windows путями. Возможность подключения через выбранный мост зависит от доступа к нему из текущей сети.

### Ограничения Windows

Системный прокси Windows не является VPN для всей системы. Он не перехватывает приложения, игры, службы и DNS-запросы, которые игнорируют пользовательские настройки прокси Windows. В проект пока не входят драйвер Windows TUN/WFP, прозрачная маршрутизация, правила для отдельных процессов, защита от утечки DNS или аварийный сетевой блокиратор (kill switch). Доступность Tor и мостов зависит от сети; статус подключения не является гарантией анонимности или безопасности.

### Сборка

Исходный код, Tor runtime, кэши SDK/NuGet, временные файлы и результаты сборки находятся в `E:\tvbox\orbot-pc`. Команду публикации PowerShell см. в китайском разделе выше; также можно запустить `build-local.ps1` из каталога проекта.

### Runtime и лицензии

Tor Expert Bundle поставляется проектом [Tor Project](https://dist.torproject.org/torbrowser/16.0.1/tor-expert-bundle-windows-x86_64-16.0.1.tar.gz). SHA-256: `9eed3c3ff1fe43df034ce360196c9ec95472560d77ff55614d0cd5120edea27b`. Для Tor и остальных runtime-компонентов действуют собственные лицензии. При распространении сборок сохраняйте каталог `LICENSES` и документацию runtime.
