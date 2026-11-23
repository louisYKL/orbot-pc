using System.Globalization;

namespace OrbotPc.Core;

/// <summary>Small UI string catalog selected from the Windows user's preferred UI culture.</summary>
public static class UiLocalization
{
    public static CultureInfo Culture => CultureInfo.CurrentUICulture;
    private static readonly Dictionary<string, (string English, string Russian)> Strings =
        new(StringComparer.Ordinal)
        {
            ["NETWORK"] = ("NETWORK", "СЕТЬ"),
            ["PREFERENCES"] = ("PREFERENCES", "НАСТРОЙКИ"),
            ["LOCAL PROXY"] = ("LOCAL PROXY", "ЛОКАЛЬНЫЙ ПРОКСИ"),
            ["连接概览"] = ("Connection overview", "Обзор подключения"),
            ["桥接与网络"] = ("Bridges & network", "Мосты и сеть"),
            ["洋葱服务"] = ("Onion services", "Onion-сервисы"),
            ["运行日志"] = ("Logs", "Журнал"),
            ["设置"] = ("Settings", "Настройки"),
            ["已停止"] = ("Stopped", "Остановлено"),
            ["Tor 已停止"] = ("Tor stopped", "Tor остановлен"),
            ["连接中"] = ("Connecting", "Подключение"),
            ["错误"] = ("Error", "Ошибка"),
            ["Tor 通路已建立"] = ("Tor circuit established", "Цепочка Tor установлена"),
            ["连接未能建立"] = ("Connection could not be established", "Не удалось установить соединение"),
            ["你的连接由你掌控"] = ("Your connection, under your control", "Вы управляете своим подключением"),
            ["代理端点已在本机回环地址监听。应用可以通过 SOCKS5 或 HTTP / HTTPS 代理接入。"] = ("Proxy endpoints are listening on the local loopback address. Applications can connect through SOCKS5 or HTTP/HTTPS.", "Прокси доступны на локальном loopback-адресе. Приложения могут подключаться через SOCKS5 или HTTP/HTTPS."),
            ["Tor 正在协商中。首次连接可能需要几分钟，受桥接和当前网络影响。"] = ("Tor is negotiating a connection. The first connection may take several minutes depending on bridges and the network.", "Tor устанавливает соединение. Первое подключение может занять несколько минут в зависимости от мостов и сети."),
            ["根据当前网络配置连接方式与出口偏好。"] = ("Configure the connection method and exit preference for the current network.", "Настройте способ подключения и предпочтения выхода для текущей сети."),
            ["将你在本机运行的服务发布为 Tor v3 地址。"] = ("Publish a service running on this PC as a Tor v3 address.", "Опубликуйте сервис на этом ПК как адрес Tor v3."),
            ["Tor 控制状态与本机代理诊断。"] = ("Tor control status and local proxy diagnostics.", "Состояние управления Tor и диагностика локального прокси."),
            ["管理本机代理端口和 Windows 用户代理行为。"] = ("Manage local proxy ports and Windows user-proxy behavior.", "Управляйте локальными портами и прокси Windows текущего пользователя."),
            ["Tor 已连接"] = ("Tor connected", "Tor подключён"),
            ["无法连接"] = ("Connection failed", "Не удалось подключиться"),
            ["连接失败"] = ("Connection failed", "Ошибка подключения"),
            ["正在启动 Tor"] = ("Starting Tor", "Запуск Tor"),
            ["正在连接"] = ("Connecting", "Подключение"),
            ["等待 Tor 控制端口"] = ("Waiting for Tor control port", "Ожидание управляющего порта Tor"),
            ["正在重新连接"] = ("Reconnecting", "Повторное подключение"),
            ["设置已保存"] = ("Settings saved", "Настройки сохранены"),
            ["已连接"] = ("Connected", "Подключено"),
            ["建立 Tor 通路中"] = ("Building Tor circuit", "Создание цепочки Tor"),
            ["Tor 网络已就绪"] = ("Tor network ready", "Сеть Tor готова"),
            ["正在连接 Tor 网络"] = ("Connecting to the Tor network", "Подключение к сети Tor"),
            ["Tor 已连接；Windows 代理启用失败，正在重试。检查日志查看原因。"] = ("Tor is connected; Windows proxy could not be enabled and will be retried. Check the log for details.", "Tor подключён; прокси Windows не включён, будет выполнена повторная попытка. Подробности в журнале."),
            ["Tor 通路已建立，但 Windows 系统代理未能启用："] = ("The Tor circuit is ready, but the Windows system proxy could not be enabled: ", "Цепочка Tor готова, но системный прокси Windows не включён: "),
            ["Tor 意外停止"] = ("Tor stopped unexpectedly", "Tor неожиданно остановлен"),
            ["代理尚未启动"] = ("Proxy is not running", "Прокси не запущен"),
            ["无法更换身份"] = ("Could not request a new identity", "Не удалось сменить личность"),
            ["Windows 代理设置失败"] = ("Windows proxy setup failed", "Не удалось настроить прокси Windows"),
            ["Tor 启动失败"] = ("Tor startup failed", "Не удалось запустить Tor"),
            ["请先点击“连接 Tor”。Tor 进程启动后，本机 HTTP 代理端口才可用。"] = ("Connect Tor first. The local HTTP proxy is available after the Tor process starts.", "Сначала подключите Tor. Локальный HTTP-прокси доступен после запуска Tor."),
            ["打开 Orbot PC"] = ("Open Orbot PC", "Открыть Orbot PC"),
            ["退出"] = ("Exit", "Выход"),
            ["程序仍在后台运行。右键通知区域图标可退出。"] = ("Orbot PC is still running in the background. Right-click the tray icon to exit.", "Orbot PC продолжает работать в фоне. Щёлкните правой кнопкой значок в области уведомлений, чтобы выйти."),
            ["仅本机回环接口可访问"] = ("Available only on this PC", "Доступно только на этом ПК"),
            ["通过 Tor 建立加密连接，并将应用流量导入本机代理。"] =
                ("Connect through Tor and route applications through the local proxy.", "Подключение через Tor и локальный прокси для приложений."),
            ["未连接"] = ("Disconnected", "Не подключено"),
            ["PRIVATE ROUTING"] = ("PRIVATE ROUTING", "ЧАСТНЫЙ МАРШРУТ"),
            ["你的连接由你掌控"] = ("Your connection, under your control", "Вы управляете своим подключением"),
            ["启动 Tor 后，可通过本机 SOCKS5 或 HTTP 代理连接。"] =
                ("Start Tor to connect through the local SOCKS5 or HTTP proxy.", "Запустите Tor, чтобы подключаться через локальный прокси SOCKS5 или HTTP."),
            ["连接 Tor"] = ("Connect Tor", "Подключить Tor"),
            ["断开连接"] = ("Disconnect", "Отключить"),
            ["新身份"] = ("New identity", "Новая личность"),
            ["Tor 已就绪"] = ("Tor is ready", "Tor готов"),
            ["复制 SOCKS5 地址"] = ("Copy SOCKS5 address", "Скопировать адрес SOCKS5"),
            ["设置 Windows 代理"] = ("Enable Windows proxy", "Включить прокси Windows"),
            ["恢复 Windows 代理"] = ("Restore Windows proxy", "Восстановить прокси Windows"),
            ["Tor 网络"] = ("Tor network", "Сеть Tor"),
            ["桥接模式"] = ("Bridge mode", "Режим мостов"),
            ["Windows 代理"] = ("Windows proxy", "Прокси Windows"),
            ["直连"] = ("Direct", "Напрямую"),
            ["未启用"] = ("Disabled", "Отключено"),
            ["Windows 版通过 SOCKS5 / HTTP 代理工作。系统代理只影响遵循 Windows 用户代理设置的应用；它不会自动代理所有应用或系统服务。"] =
                ("The Windows client uses SOCKS5 and HTTP proxies. The system proxy affects applications that honor Windows proxy settings; it does not automatically proxy every application or service.", "Клиент Windows использует прокси SOCKS5 и HTTP. Системный прокси действует только для приложений, учитывающих настройки Windows, и не перенаправляет автоматически весь трафик."),
            ["连接方式"] = ("Connection method", "Способ подключения"),
            ["如果网络无法直连 Tor，可尝试 Snowflake、obfs4 或粘贴可信来源提供的桥接行。桥接可用性随网络环境变化。"] =
                ("If your network cannot connect to Tor directly, try Snowflake, obfs4, or paste bridge lines from a trusted source. Bridge availability depends on your network.", "Если сеть не подключается к Tor напрямую, попробуйте Snowflake, obfs4 или вставьте строки мостов из надёжного источника. Доступность мостов зависит от сети."),
            ["桥接传输"] = ("Bridge transport", "Транспорт моста"),
            ["直连 Tor"] = ("Direct Tor", "Прямое подключение Tor"),
            ["obfs4 内置桥接"] = ("Built-in obfs4 bridges", "Встроенные мосты obfs4"),
            ["Snowflake 内置桥接"] = ("Built-in Snowflake bridges", "Встроенные мосты Snowflake"),
            ["WebTunnel（自定义桥接）"] = ("WebTunnel (custom bridges)", "WebTunnel (свои мосты)"),
            ["自定义桥接行"] = ("Custom bridge lines", "Свои строки мостов"),
            ["桥接行（每行一个，可留空使用随运行时附带的内置入口）"] =
                ("Bridge lines (one per line; leave empty to use bundled bridges)", "Строки мостов (по одной в строке; оставьте пустым для встроенных мостов)"),
            ["保存网络设置"] = ("Save network settings", "Сохранить настройки сети"),
            ["桥接地址和出口节点会暴露在本机配置文件中。不要在共享电脑上输入你不希望其他本机用户看到的信息。Tor 的桥接并不能保证绕过所有网络限制。"] =
                ("Bridge addresses and exit-node preferences are stored in a local configuration file. Do not enter information you want hidden from other users of a shared PC. Bridges cannot bypass every network restriction.", "Адреса мостов и предпочтения выходных узлов сохраняются в локальном файле. Не вводите на общем ПК данные, которые должны быть скрыты от других пользователей. Мосты не гарантируют обход любых сетевых ограничений."),
            ["发布本地服务为 .onion 地址"] = ("Publish a local service as an .onion address", "Опубликовать локальный сервис как адрес .onion"),
            ["将本机服务端口映射到 Tor v3 洋葱服务。目标服务仍由你自己的本地服务器提供。"] =
                ("Map a local service port to a Tor v3 Onion Service. The target service is still hosted by your local server.", "Свяжите локальный порт с Onion-сервисом Tor v3. Сам сервис по-прежнему работает на вашем компьютере."),
            ["服务名称"] = ("Service name", "Имя сервиса"),
            ["洋葱端口"] = ("Onion port", "Порт Onion"),
            ["本机端口"] = ("Local port", "Локальный порт"),
            ["添加并应用（重启 Tor）"] = ("Add and apply (restart Tor)", "Добавить и применить (перезапустить Tor)"),
            ["已配置的服务"] = ("Configured services", "Настроенные сервисы"),
            ["清空显示"] = ("Clear display", "Очистить"),
            ["打开日志目录"] = ("Open log folder", "Открыть папку журнала"),
            ["本机代理端口"] = ("Local proxy ports", "Локальные порты прокси"),
            ["更改后需要重启 Tor 才会应用。端口只绑定到 127.0.0.1。"] =
                ("Restart Tor to apply changes. Ports bind only to 127.0.0.1.", "Чтобы применить изменения, перезапустите Tor. Порты доступны только через 127.0.0.1."),
            ["Tor 控制端口"] = ("Tor control port", "Порт управления Tor"),
            ["出口国家代码（可选，例如 us、de；不保证可用性）"] =
                ("Exit country code (optional, e.g. us or de; availability is not guaranteed)", "Код страны выхода (необязательно, например us или de; доступность не гарантируется)"),
            ["Tor 连接成功后自动启用 Windows 当前用户 HTTP/HTTPS 代理"] =
                ("Enable the current user's Windows HTTP/HTTPS proxy after Tor connects", "Включать HTTP/HTTPS-прокси Windows после подключения Tor"),
            ["退出或 Tor 异常停止时，Orbot PC 会恢复此前保存的当前用户代理值。"] =
                ("Orbot PC restores the saved proxy settings when you exit or Tor stops unexpectedly.", "При выходе или неожиданной остановке Tor Orbot PC восстановит сохранённые настройки прокси."),
            ["保存设置"] = ("Save settings", "Сохранить настройки"),
            ["这个 Windows 版本使用 Tor Expert Bundle；它不调用 Android VPN、按应用包名路由或通知接口。全系统透明代理和按进程分流需要另行安装并授权 Windows TUN/WFP 服务。"] =
                ("This Windows version uses the Tor Expert Bundle. It does not use Android VPN, package routing, or notification APIs. System-wide transparent proxying and per-process routing require a separately installed and authorized Windows TUN/WFP service.", "Эта версия Windows использует Tor Expert Bundle и не применяет Android VPN, маршрутизацию по пакетам или Android-уведомления. Для прозрачной маршрутизации всей системы и отдельных процессов нужна отдельная служба Windows TUN/WFP с соответствующими правами.")
        };

    public static string Language => LanguageFor(Culture);

    public static string LanguageFor(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "zh" => "zh-CN",
        "ru" => "ru-RU",
        _ => "en-US"
    };

    public static string Translate(string source) => Translate(source, Culture);

    public static string Translate(string source, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(source) || culture.TwoLetterISOLanguageName == "zh") return source;
        if (!Strings.TryGetValue(source.Trim(), out var translated)) return source;
        return culture.TwoLetterISOLanguageName == "ru" ? translated.Russian : translated.English;
    }
}
