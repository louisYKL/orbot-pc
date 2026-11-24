# Orbot PC

**Windows desktop client for Tor** based on the Tor Project's official Windows Expert Bundle. It provides local SOCKS5 and HTTP/HTTPS proxy endpoints and can apply them to the current Windows user's proxy settings after Tor connects.

## 0.3.1 release

### 中文更新记录

- 新增 Tor 出站代理自动探测：仅使用已启用的 Windows 代理中可连通的本机 SOCKS5 端点，避开应用自身监听端口；检测不到则维持原设置。
- 新增默认关闭、手动开启的 Windows TUN 虚拟网卡模式，连接 Tor 后可启动；Tor/桥接进程走物理网卡防递归，DNS 通过 Tor 内 DoH，UDP 阻断。
- TUN 使用 sing-box 1.14.4 与官方 Wintun 0.14.1，随包包含许可和 sing-box 对应源码。配置校验已通过，但没有启动虚拟网卡，真实路由和 DNS 故障恢复仍需实机验证。
- 如果 TUN 进程意外退出，应用会保留清理未确认标记并阻止再次启动，直到 Windows 重启；这只是防止重复接管路由的保护，不代表已验证或自动修复残留路由。
- 增加真实联网验证：Tor 引导到 100%，应用本地代理及 Windows 当前用户系统代理均通过 Tor 检查，退出后原代理设置逐项恢复。

### English change log

- Added Tor upstream auto-detection for a reachable loopback SOCKS5 endpoint in the enabled Windows proxy settings; app listener ports are excluded, and the existing behavior remains when none is found.
- Added a manual, off-by-default Windows TUN mode. Tor and bridge processes bypass the tunnel to prevent routing loops, DNS uses DoH over Tor, and UDP is blocked.
- Bundles sing-box 1.14.4 and official Wintun 0.14.1 with license texts and corresponding sing-box source. Config validation passes; the virtual adapter was not started, so live routing and DNS recovery still need device testing.
- If the TUN process exits unexpectedly, the app persists an unconfirmed-cleanup warning and blocks another TUN start until Windows restarts. This prevents a second route takeover; it does not verify or repair stale routes.
- Live network verification reached 100% Tor bootstrap. Both the app-local proxy and Windows current-user proxy passed the Tor check, and the original proxy settings were restored after exit.

### Русский список изменений

- Добавлено автоматическое обнаружение доступного локального SOCKS5 в активных настройках прокси Windows; порты самого приложения исключаются. Если такой прокси не найден, прежнее поведение сохраняется.
- Добавлен ручной режим Windows TUN, выключенный по умолчанию. Tor и мосты обходят туннель для предотвращения цикла, DNS использует DoH через Tor, UDP блокируется.
- В комплект входят sing-box 1.14.4 и официальный Wintun 0.14.1 с лицензиями и соответствующим исходным кодом sing-box. Конфигурация проверена, но адаптер не запускался; маршруты и восстановление DNS нужно проверить на устройстве.
- При аварийном завершении TUN приложение сохраняет предупреждение о неподтверждённой очистке и блокирует повторный запуск до перезагрузки Windows. Это предотвращает повторное изменение маршрутов, но не подтверждает и не исправляет оставшиеся маршруты.
- Реальная проверка сети достигла 100% загрузки Tor. Локальный прокси приложения и системный прокси пользователя Windows прошли проверку Tor, исходные настройки восстановлены после выхода.

### Previous release: 0.3.0

- Moat 桥接请求改为使用随包 `lyrebird` 的 `meek_lite` 临时 SOCKS5 隧道。
- 增加原生 Windows DNSTT managed transport、内置桥接，并在 Moat 的 meek 请求不可用时回退到 Guardian Project DNSTT 端点。
- 增加 v3 Onion 客户端授权 `.auth_private` 导出，并在导出前校验域名和 X25519 密钥格式。
- 补齐开机启动、桥接回退、Snowflake AMP、Onion Service 备份、托盘生命周期和中英俄界面文案。
- 新增默认关闭的 Snowflake volunteer relay 手动启停，每次最多服务 1 个客户端。

### 0.3.0 English change log

- Moat bridge requests now use the bundled `lyrebird` `meek_lite` temporary SOCKS5 tunnel.
- Added the native Windows DNSTT managed transport and bundled bridges, plus a Guardian Project Moat fallback when the meek request is unavailable.
- Added validated export of v3 Onion client `.auth_private` files, including domain and X25519 key checks.
- Completed the Windows startup, bridge fallback, Snowflake AMP, Onion Service backup, tray lifecycle, and Chinese/Russian/English UI paths covered by this release.
- Added an opt-in Snowflake volunteer relay with manual start/stop and a one-client capacity limit.

### 0.3.0 Русский список изменений

- Запросы мостов Moat теперь выполняются через поставляемый временный SOCKS5-туннель `lyrebird` `meek_lite`.
- Добавлены собственный транспорт DNSTT для Windows, встроенные мосты и резервный запрос Moat через Guardian Project, если `meek_lite` недоступен.
- Добавлен проверяемый экспорт файлов клиентской авторизации v3 Onion `.auth_private` с проверкой домена и ключа X25519.
- Завершены поддержанные этой версией функции автозапуска Windows, отката мостов, Snowflake AMP, резервного копирования Onion Service, области уведомлений и интерфейса на китайском, русском и английском языках.
- Добавлен добровольный ретранслятор Snowflake с ручным запуском/остановкой и ограничением в один клиент.

> Windows Orbot PC includes an optional, off-by-default TUN mode. Enabling it changes system routing; UDP is blocked. The TUN mode has not been tested with a live network adapter.

## 中文

Orbot PC 是面向 Windows 的 Tor 桌面客户端，使用 Tor Project 官方 Windows Expert Bundle。用户点击“连接 Tor”后，程序启动 Tor 并等待网络引导成功；成功后启用本机 SOCKS5、HTTP/HTTPS 代理，并自动设置当前 Windows 用户的系统代理。连接与代理均可在应用中停止或恢复。

### 功能

- 本机回环地址 SOCKS5，以及支持 HTTP 和 HTTPS `CONNECT` 的本机代理。
- Tor 状态与引导进度监控、New Identity、Cookie 控制端口认证。
- 直连、内置 obfs4/Snowflake/Snowflake AMP、WebTunnel/自定义桥接行和可选出口国家。
- Smart Connect 可按用户设定顺序轮换连接方式；启动失败或引导停滞时自动重试下一种传输。
- Snowflake 志愿中继默认关闭；用户可手动启动/停止，最多同时中继 1 个客户端。
- 桥接页可请求 Tor Project Moat 并应用其按服务器优先级返回的桥接配置；请求通过随包 lyrebird 的 `meek_lite` 临时隧道发送。
- Tor 高级配置：DNS/HTTP 隧道监听、节点和网络偏好、连接隔离、填充选项与出站 HTTPS/SOCKS 代理。
- Tor v3 Onion Service 本地端口映射。
- Onion Service 配置可导出/导入 ZIP；备份只包含端口映射和服务名称，不复制私钥。
- v3 Onion 客户端授权：导入、导出、保存与移除 `.auth_private` X25519 密钥；Windows ACL 限制授权密钥。
- Windows 当前用户 HTTP/HTTPS 代理自动启用；停止 Tor 或退出程序时恢复此前保存的代理设置。
- Tor 出站代理未手动配置时，会检测已启用 Windows 代理中可连通的本机 SOCKS5 端点；只接受回环 IP 且跳过应用自身端口。
- 可选 Windows TUN 虚拟网卡：连接成功后在概览页手动启动，TCP 经 Tor，Tor/桥接进程避免回环，DNS 经 Tor DoH，UDP 阻断。
- 可选当前用户登录后启动；应用仍保持 Tor 和代理功能关闭，需用户手动连接。
- 关闭窗口后驻留通知区域；右键托盘图标可重新打开窗口或退出。
- 端口冲突时自动选择可用的本机端口。
- 界面按 Windows 当前 UI 语言自动选择简体中文、俄语或英语（其它语言使用英语）。
- 引导无进展五分钟或总耗时十五分钟会自动结束连接尝试并显示当前阶段，避免无限停留在“正在连接”。

### 使用

1. 启动程序并在 Windows UAC 提示中允许管理员权限。管理员权限用于本程序的 Windows 适配与代理设置；开启登录自启后，Windows 可能在启动时再次显示 UAC 提示。
2. 在“桥接与网络”选择直连、obfs4、Snowflake 或配置桥接行。网络环境无法直连 Tor 时，可按需选择桥接。
3. 点击“连接 Tor”。待引导成功后，程序自动启用本机代理和当前 Windows 用户的 HTTP/HTTPS 代理。
4. TUN 需在 Tor 完成连接后从概览页单独启动；该模式会修改系统路由、阻断 UDP，目前未在真实网卡环境测试。停止 TUN 后再断开 Tor。
5. 点击“断开连接”会停止 Tor、本机代理并恢复原有 Windows 代理设置。关闭窗口只会将程序隐藏到通知区域；要彻底退出，请右键托盘图标并选择“退出”。

本机端点默认使用 `127.0.0.1`。SOCKS5 可供支持 SOCKS5 的应用使用；Windows 系统代理采用 HTTP/HTTPS 端点，只有遵循 Windows 用户代理设置的应用会使用它。若 Tor 无法引导，请查看“运行日志”，检查当前网络、桥接模式和桥接行。

Windows 桥接传输会直接启动项目目录中的官方 pluggable transport；桥接程序路径和配置均由应用生成，连接能否完成仍取决于当前网络能否访问所选入口。

### Windows 能力边界

系统代理不是全局 VPN，也不会接管忽略 Windows 用户代理设置的应用、游戏或服务。可选 TUN 模式会修改系统路由，以 sing-box/Wintun 接管设备 TCP 流量；UDP 明确阻断，DNS 通过 Tor 内的 DoH 查询。该功能尚未在真实网卡环境验证，首次启用请确保可通过本机恢复网络。Tor 和桥接可用性依赖当前网络；连接状态不代表匿名性或安全性保证。

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
- Snowflake AMP-cache rendezvous using Tor's bundled Snowflake transport.
- Smart Connect retries transports in the configured order after startup failure or stalled bootstrap.
- An optional Snowflake volunteer relay can be started and stopped manually; its capacity is limited to one client and it is off by default.
- The bridges page can request Moat settings and apply the server-prioritized bridge; requests use the bundled lyrebird `meek_lite` managed transport.
- Tor configuration options for DNS/HTTP tunnel listeners, routing preferences, SOCKS isolation, padding, and upstream HTTPS/SOCKS proxies.
- Local port forwarding through Tor v3 Onion Services.
- Onion Service mappings can be exported/imported as a validated ZIP; private service keys are never copied into the backup.
- v3 Onion client authorization import, protected storage, export, and removal.
- Native Windows DNSTT managed transport with bundled bridge lines and Moat fallback through Guardian Project's DNSTT endpoint.
- Automatic current-user Windows HTTP/HTTPS proxy configuration after Tor bootstrap, with restoration of the saved settings when Tor stops or the app exits.
- When no Tor upstream is configured, checks the enabled Windows proxy for a reachable loopback SOCKS5 endpoint and uses it only after a successful handshake.
- Optional Windows TUN mode routes TCP through Tor, sends Tor and bridge processes through the physical interface, sends DNS through DoH over Tor, and blocks UDP. Start it manually after Tor connects; it has not been tested with a live adapter.
- Minimize-to-tray behavior; right-click the tray icon to reopen the window or exit.
- Automatic selection of available local ports when configured ports are occupied.
- UI language is selected from the Windows UI culture: Simplified Chinese, Russian, or English fallback.
- A five-minute no-progress timeout and fifteen-minute overall bootstrap timeout report the active phase instead of leaving the UI in an endless connecting state.

### Getting started

1. Start the app and approve the Windows UAC prompt. The application manifest requests administrator privileges for its Windows integration.
2. Choose Direct, obfs4, Snowflake, or configure bridge lines under **Bridges & Network**. Use a bridge when your network cannot reach Tor directly.
3. Select **Connect Tor**. The local proxies and current-user Windows HTTP/HTTPS proxy are enabled after bootstrap succeeds.
4. Start TUN separately from the overview only after Tor connects. It changes system routing, blocks UDP, and has not been tested with a live adapter. Stop TUN before disconnecting Tor.
5. Select **Disconnect** to stop Tor and restore the previous Windows proxy settings. Closing the window hides the app in the notification area. To exit completely, right-click its tray icon and choose **Exit**.

The optional sign-in startup entry is per-user. Since this build requests administrator privileges, Windows may show a UAC prompt when it starts at sign-in.

Endpoints bind to `127.0.0.1`. Use SOCKS5 in applications that support it. The Windows system proxy uses the HTTP/HTTPS endpoint and affects only applications that honor the current user's Windows proxy settings. If bootstrap fails, inspect **Logs** and check the network and bridge configuration.

Bridge transports are launched from the bundled Tor Expert Bundle using Windows-compatible executable paths. Whether a bridge reaches the Tor network still depends on network access to the selected bridge.

### Windows limitations

The Windows user proxy is not system-wide. An optional sing-box/Wintun TUN mode can route device TCP traffic; UDP is blocked and DNS uses DoH over Tor. This mode changes system routing and has not been tested with a live network adapter. Abnormal-exit route recovery is unverified; start it only after Tor connects and stop it before changing or stopping Tor. Moat first uses the bundled lyrebird `meek_lite` transport and falls back to the bundled DNSTT managed transport through Guardian Project's Moat endpoint. The optional Snowflake volunteer relay starts only when enabled and is limited to one client. Tor and bridge reachability depend on the network; a connected status is not a guarantee of anonymity or security.

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
- Snowflake AMP, Smart Connect с последовательным повтором подключения и расширенные параметры Tor.
- Добровольный ретранслятор Snowflake можно включать и выключать вручную; по умолчанию он отключён и обслуживает не более одного клиента.
- Получение настроек мостов Moat с приоритетом, заданным сервером, через поставляемый туннель `meek_lite`.
- Перенаправление локальных портов через Onion Services v3.
- Конфигурации Onion Services можно экспортировать и импортировать в проверяемом ZIP; закрытые ключи в резервную копию не включаются.
- Импорт, хранение, экспорт и удаление ключей клиентской авторизации v3 Onion.
- Автоматическая настройка HTTP/HTTPS-прокси текущего пользователя Windows после подключения Tor и восстановление сохранённых параметров при остановке Tor или выходе из приложения.
- Если исходящий прокси Tor не задан, приложение проверяет активные настройки прокси Windows на доступный локальный SOCKS5 и использует его только после успешной проверки.
- Необязательный режим Windows TUN направляет TCP через Tor, Tor и мосты идут через физический интерфейс, DNS отправляется через DoH внутри Tor, UDP блокируется. Запускайте вручную после подключения Tor; работа адаптера не проверялась.
- Сворачивание в область уведомлений. Щёлкните правой кнопкой по значку, чтобы открыть окно или завершить работу.
- Автоматический выбор свободных локальных портов, если заданные порты заняты.
- Язык интерфейса выбирается по языку Windows: упрощённый китайский, русский или английский по умолчанию.
- Если подключение не продвигается пять минут или длится больше пятнадцати минут, попытка завершается с указанием текущего этапа вместо бесконечного ожидания.

### Начало работы

1. Запустите приложение и подтвердите запрос UAC Windows. Для интеграции с Windows манифест приложения запрашивает права администратора.
2. В разделе **«Мосты и сеть»** выберите прямое подключение, obfs4, Snowflake или задайте строки мостов. Используйте мост, если сеть не позволяет подключиться к Tor напрямую.
3. Нажмите **«Подключить Tor»**. После успешного подключения включатся локальные прокси и HTTP/HTTPS-прокси текущего пользователя Windows.
4. Включайте TUN отдельно на странице обзора только после подключения Tor. Он изменяет маршруты, блокирует UDP и не проверялся на реальном адаптере. Остановите TUN перед отключением Tor.
5. Нажмите **«Отключить»**, чтобы остановить Tor и восстановить прежние параметры прокси. Закрытие окна только скрывает приложение в области уведомлений. Для полного выхода нажмите правой кнопкой на значке и выберите **«Выход»**.

Прокси доступны только по адресу `127.0.0.1`. SOCKS5 можно указать в приложениях с поддержкой SOCKS5. Системный прокси Windows использует HTTP/HTTPS и действует только для программ, которые учитывают пользовательские настройки прокси Windows. Если подключение не удаётся, откройте раздел **«Журнал»** и проверьте сеть и параметры мостов.

Мосты запускаются из поставляемого Tor Expert Bundle с корректными для Windows путями. Возможность подключения через выбранный мост зависит от доступа к нему из текущей сети.

### Ограничения Windows

Системный прокси Windows не является VPN для всей системы. Необязательный режим sing-box/Wintun TUN может маршрутизировать TCP-трафик устройства; UDP блокируется, DNS выполняется через DoH внутри Tor. Этот режим изменяет системную маршрутизацию и не проверен на реальном сетевом адаптере. Запускайте его только после подключения Tor и остановите перед изменением или отключением Tor. Moat сначала использует поставляемый транспорт `meek_lite` из lyrebird, а затем при необходимости переключается на поставляемый транспорт DNSTT через сервер Guardian Project. Дополнительный добровольный ретранслятор Snowflake входит в пакет, запускается только вручную и обслуживает не более одного клиента. Доступность Tor и мостов зависит от сети; статус подключения не является гарантией анонимности или безопасности.

### Сборка

Исходный код, Tor runtime, кэши SDK/NuGet, временные файлы и результаты сборки находятся в `E:\tvbox\orbot-pc`. Команду публикации PowerShell см. в китайском разделе выше; также можно запустить `build-local.ps1` из каталога проекта.

### Runtime и лицензии

Tor Expert Bundle поставляется проектом [Tor Project](https://dist.torproject.org/torbrowser/16.0.1/tor-expert-bundle-windows-x86_64-16.0.1.tar.gz). SHA-256: `9eed3c3ff1fe43df034ce360196c9ec95472560d77ff55614d0cd5120edea27b`. Для Tor и остальных runtime-компонентов действуют собственные лицензии. При распространении сборок сохраняйте каталог `LICENSES` и документацию runtime.
