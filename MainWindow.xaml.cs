using OrbotPc.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace OrbotPc;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _basePath;
    private readonly string _statePath;
    private readonly string _settingsPath;
    private readonly string _appLogPath;
    private readonly string _clientAuthPath;
    private readonly WindowsStartupController _startupController;
    private readonly TorRuntime _tor;
    private readonly WindowsProxyController _windowsProxy;
    private readonly SnowflakeVolunteerRuntime _snowflakeVolunteer;
    private readonly DispatcherTimer _monitorTimer;
    private readonly ObservableCollection<ServiceView> _serviceRows = [];
    private OrbotSettings _settings = new();
    private HttpProxyServer? _httpProxy;
    private bool _loadingUi;
    private bool _polling;
    private bool _stopping;
    private bool _closing;
    private bool _allowClose;
    private bool _exitRequested;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly string _uiLanguage;
    private int _pollCount;
    private int _bootstrapQueryFailures;
    private DateTime _connectStartedUtc;
    private DateTime _lastBootstrapProgressUtc;
    private int _lastBootstrapProgress = -1;
    private string? _proxyEnableError;
    private DateTime _nextProxyEnableRetryUtc;
    private string _activePage = "Overview";
    private string _uiState = "";
    private SmartConnectPlanner? _smartConnectPlanner;
    private DateTime _smartConnectStartedUtc;
    private MoatTunnelRuntime? _moatTunnel;

    public MainWindow()
    {
        InitializeComponent();
        _uiLanguage = UiLocalization.Language;
        ApplyUiLocalization();
        _basePath = AppContext.BaseDirectory;
        _statePath = Path.Combine(_basePath, ".data");
        _settingsPath = Path.Combine(_statePath, "settings.json");
        _appLogPath = Path.Combine(_statePath, "logs", "orbot-pc.log");
        _clientAuthPath = Path.Combine(_statePath, "tor", "client-onion-auth");
        _startupController = new WindowsStartupController(_statePath, Environment.ProcessPath ?? Path.Combine(_basePath, "OrbotPC.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(_appLogPath)!);
        _windowsProxy = new WindowsProxyController(_statePath);
        _windowsProxy.RestoreIfOwned();
        _snowflakeVolunteer = new SnowflakeVolunteerRuntime(_basePath);
        _snowflakeVolunteer.LogMessage += message => Dispatcher.BeginInvoke(() => AppendLog(message));
        _snowflakeVolunteer.Exited += exitCode => Dispatcher.BeginInvoke(() =>
        {
            SetSnowflakeVolunteerUi(false);
            if (!_closing) AppendLog($"Snowflake volunteer relay stopped with exit code {exitCode}.");
        });
        _tor = new TorRuntime(_basePath);
        _tor.LogMessage += message => Dispatcher.BeginInvoke(() => AppendLog(message));
        _tor.Exited += () => Dispatcher.BeginInvoke(new Action(() => _ = HandleUnexpectedExitAsync()));
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += MonitorTimer_Tick;

        _trayIcon = CreateTrayIcon();

        LoadSettings();
        EnsureAvailablePorts();
        RefreshClientAuthList();
        ServicesList.ItemsSource = _serviceRows;
        RefreshServices();
        SetUiState("Stopped", T("Tor 已停止"), 0);
        AppendLog($"Interface language selected from Windows: {_uiLanguage}.");
        AppendLog("Orbot PC ready. Tor and all proxy features remain off until started manually.");
    }

    private static string T(string value) => UiLocalization.Translate(value);

    private void ApplyUiLocalization()
    {
        var visited = new HashSet<DependencyObject>();
        Localize(this);
        Title = T(Title);

        void Localize(DependencyObject element)
        {
            if (!visited.Add(element)) return;
            if (element is TextBlock textBlock) textBlock.Text = T(textBlock.Text);
            if (element is ContentControl contentControl && contentControl.Content is string content)
                contentControl.Content = T(content);
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
                Localize(child);
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "orbot.ico");
        var icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application;
        var tray = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Orbot PC",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        var open = new Forms.ToolStripMenuItem(T("打开 Orbot PC"));
        open.Click += (_, _) => RestoreFromTray();
        var exit = new Forms.ToolStripMenuItem(T("退出"));
        exit.Click += (_, _) => ExitFromTray();
        tray.ContextMenuStrip.Items.Add(open);
        tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        tray.ContextMenuStrip.Items.Add(exit);
        tray.DoubleClick += (_, _) => RestoreFromTray();
        tray.BalloonTipTitle = "Orbot PC";
        return tray;
    }

    private void RestoreFromTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _trayIcon.ShowBalloonTip(1200, "Orbot PC", T("程序仍在后台运行。右键通知区域图标可退出。"), Forms.ToolTipIcon.Info);
        AppendLog("Window hidden to the notification area; Tor and local proxy remain running.");
    }

    private void ExitFromTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _exitRequested = true;
            _trayIcon.Visible = false;
            Close();
        });
    }

    private void LoadSettings()
    {
        _loadingUi = true;
        try
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<OrbotSettings>(json) ?? new OrbotSettings();
                    using var document = JsonDocument.Parse(json);
                    var settingsVersion = document.RootElement.TryGetProperty(nameof(OrbotSettings.SettingsVersion), out var versionElement)
                        && versionElement.TryGetInt32(out var parsedVersion) ? parsedVersion : 0;
                    if (settingsVersion < 2)
                    {
                        _settings.UseWindowsProxy = true;
                        _settings.SettingsVersion = 2;
                        SaveSettingsToDisk();
                        AppendLog("Migrated legacy preferences to enable Windows proxy automatically after Tor connects.");
                    }
                }
                catch (JsonException) { _settings = new OrbotSettings(); }
            }
            SocksPortBox.Text = _settings.SocksPort.ToString();
            HttpPortBox.Text = _settings.HttpPort.ToString();
            ControlPortBox.Text = _settings.ControlPort.ToString();
            ExitCountryBox.Text = _settings.ExitCountry;
            BridgeLinesBox.Text = _settings.CustomBridges;
            WindowsProxyCheckBox.IsChecked = _settings.UseWindowsProxy;
            StartOnLoginCheckBox.IsChecked = _settings.StartOnLogin;
            DnsPortBox.Text = _settings.DnsPort.ToString();
            HttpTunnelPortBox.Text = _settings.HttpTunnelPort.ToString();
            AutomapHostsCheckBox.IsChecked = _settings.AutomapHostsOnResolve;
            VirtualAddrNetworkBox.Text = _settings.VirtualAddrNetwork;
            EntryNodesBox.Text = _settings.EntryNodes;
            ExcludeNodesBox.Text = _settings.ExcludeNodes;
            ExcludeExitNodesBox.Text = _settings.ExcludeExitNodes;
            ReachableAddressesBox.Text = _settings.ReachableAddresses;
            StrictNodesCheckBox.IsChecked = _settings.StrictNodes;
            ConnectionPaddingCheckBox.IsChecked = _settings.ConnectionPadding;
            CircuitPaddingCheckBox.IsChecked = _settings.CircuitPadding;
            ReducedConnectionPaddingCheckBox.IsChecked = _settings.ReducedConnectionPadding;
            ReducedCircuitPaddingCheckBox.IsChecked = _settings.ReducedCircuitPadding;
            ClientUseIPv4CheckBox.IsChecked = _settings.ClientUseIPv4;
            ClientUseIPv6CheckBox.IsChecked = _settings.ClientUseIPv6;
            PreferIPv6CheckBox.IsChecked = _settings.PreferIPv6;
            IPv6TrafficCheckBox.IsChecked = _settings.IPv6Traffic;
            NoIPv4TrafficCheckBox.IsChecked = _settings.NoIPv4Traffic;
            IsolateDestAddrCheckBox.IsChecked = _settings.IsolateDestAddr == true;
            IsolateDestPortCheckBox.IsChecked = _settings.IsolateDestPort == true;
            IsolateClientProtocolCheckBox.IsChecked = _settings.IsolateClientProtocol == true;
            KeepAliveIsolateSocksAuthCheckBox.IsChecked = _settings.KeepAliveIsolateSocksAuth == true;
            OutboundProxyHostBox.Text = _settings.OutboundProxyHost;
            OutboundProxyPortBox.Text = _settings.OutboundProxyPort == 0 ? "" : _settings.OutboundProxyPort.ToString();
            OutboundProxyUsernameBox.Text = _settings.OutboundProxyUsername;
            OutboundProxyPasswordBox.Password = _settings.OutboundProxyPassword;
            EnableSmartConnectCheckBox.IsChecked = _settings.EnableSmartConnect;
            SmartConnectModesBox.Text = string.Join(",", _settings.SmartConnectModes ?? ["none", "snowflake", "snowflake-amp", "obfs4", "dnstt"]);
            SmartConnectFallbackSecondsBox.Text = _settings.SmartConnectFallbackSeconds.ToString();
            foreach (var item in OutboundProxyTypeCombo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, _settings.OutboundProxyType, StringComparison.OrdinalIgnoreCase))
                {
                    OutboundProxyTypeCombo.SelectedItem = item;
                    break;
                }
            }
            if (OutboundProxyTypeCombo.SelectedItem is null) OutboundProxyTypeCombo.SelectedIndex = 0;
            foreach (var item in BridgeModeCombo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, _settings.BridgeMode, StringComparison.OrdinalIgnoreCase))
                {
                    BridgeModeCombo.SelectedItem = item;
                    break;
                }
            }
            if (BridgeModeCombo.SelectedItem is null) BridgeModeCombo.SelectedIndex = 0;
            UpdateEndpoints();
        }
        finally { _loadingUi = false; }
    }

    private void EnsureAvailablePorts()
    {
        var used = new HashSet<int>();
        _settings.SocksPort = ChooseAvailablePort(_settings.SocksPort, used);
        _settings.HttpPort = ChooseAvailablePort(_settings.HttpPort, used);
        _settings.ControlPort = ChooseAvailablePort(_settings.ControlPort, used);
        SocksPortBox.Text = _settings.SocksPort.ToString();
        HttpPortBox.Text = _settings.HttpPort.ToString();
        ControlPortBox.Text = _settings.ControlPort.ToString();
        UpdateEndpoints();
        AppendLog($"Selected local ports: SOCKS5 {_settings.SocksPort}, HTTP {_settings.HttpPort}, control {_settings.ControlPort}.");
    }

    private static int ChooseAvailablePort(int preferred, ISet<int> used)
    {
        if (preferred is >= 1024 and <= 65535 && !used.Contains(preferred) && IsPortAvailable(preferred))
        {
            used.Add(preferred);
            return preferred;
        }
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            if (used.Add(port)) return port;
        }
        throw new InvalidOperationException("Could not allocate three free local proxy ports.");
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    private OrbotSettings ReadSettingsFromUi()
    {
        if (!TryPort(SocksPortBox.Text, out var socksPort)) throw new InvalidOperationException("SOCKS5 port must be between 1 and 65535.");
        if (!TryPort(HttpPortBox.Text, out var httpPort)) throw new InvalidOperationException("HTTP proxy port must be between 1 and 65535.");
        if (!TryPort(ControlPortBox.Text, out var controlPort)) throw new InvalidOperationException("Tor control port must be between 1 and 65535.");
        if (new[] { socksPort, httpPort, controlPort }.Distinct().Count() != 3) throw new InvalidOperationException("SOCKS5, HTTP, and control ports must be different.");
        if (socksPort == httpPort || socksPort == controlPort || httpPort == controlPort) throw new InvalidOperationException("Proxy and control ports must not overlap.");

        var mode = (BridgeModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
        var exitCountry = ExitCountryBox.Text.Trim();
        if (exitCountry.Length is not 0 and not 2) throw new InvalidOperationException("Exit country must be empty or a two-letter country code.");
        if (!TryOptionalPort(DnsPortBox.Text, out var dnsPort)) throw new InvalidOperationException("DNS port must be 0 (disabled) or between 1 and 65535.");
        if (!TryOptionalPort(HttpTunnelPortBox.Text, out var httpTunnelPort)) throw new InvalidOperationException("HTTP tunnel port must be 0 (disabled) or between 1 and 65535.");
        if (dnsPort > 0 && dnsPort == httpTunnelPort) throw new InvalidOperationException("DNS and HTTP tunnel ports must be different.");

        var proxyType = (OutboundProxyTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
        var proxyHost = OutboundProxyHostBox.Text.Trim();
        var proxyPortText = OutboundProxyPortBox.Text.Trim();
        var proxyPort = 0;
        if (proxyType != "none" && !TryPort(proxyPortText, out proxyPort))
            throw new InvalidOperationException("Outbound proxy port must be between 1 and 65535.");
        if (proxyType == "none" && (!string.IsNullOrWhiteSpace(proxyHost) || !string.IsNullOrWhiteSpace(proxyPortText) ||
                                     !string.IsNullOrWhiteSpace(OutboundProxyUsernameBox.Text) || !string.IsNullOrEmpty(OutboundProxyPasswordBox.Password)))
            throw new InvalidOperationException("Clear outbound proxy fields or select a proxy type.");

        var smartConnectModes = SmartConnectModesBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (smartConnectModes.Count == 0) throw new InvalidOperationException("Smart Connect requires at least one transport mode.");
        if (!int.TryParse(SmartConnectFallbackSecondsBox.Text.Trim(), out var smartConnectFallbackSeconds) || smartConnectFallbackSeconds is < 10 or > 900)
            throw new InvalidOperationException("Smart Connect fallback timeout must be between 10 and 900 seconds.");
        var settings = new OrbotSettings
        {
            SocksPort = socksPort,
            HttpPort = httpPort,
            ControlPort = controlPort,
            DisableNetwork = _settings.DisableNetwork,
            UseWindowsProxy = WindowsProxyCheckBox.IsChecked == true,
            StartOnLogin = StartOnLoginCheckBox.IsChecked == true,
            BridgeMode = mode,
            CustomBridges = BridgeLinesBox.Text,
            ExitCountry = exitCountry,
            DnsPort = dnsPort,
            HttpTunnelPort = httpTunnelPort,
            AutomapHostsOnResolve = AutomapHostsCheckBox.IsChecked == true,
            VirtualAddrNetwork = VirtualAddrNetworkBox.Text.Trim(),
            EntryNodes = EntryNodesBox.Text.Trim(),
            ExcludeNodes = ExcludeNodesBox.Text.Trim(),
            ExcludeExitNodes = ExcludeExitNodesBox.Text.Trim(),
            ReachableAddresses = ReachableAddressesBox.Text.Trim(),
            StrictNodes = StrictNodesCheckBox.IsChecked == true,
            ConnectionPadding = ConnectionPaddingCheckBox.IsChecked,
            CircuitPadding = CircuitPaddingCheckBox.IsChecked,
            ReducedConnectionPadding = ReducedConnectionPaddingCheckBox.IsChecked,
            ReducedCircuitPadding = ReducedCircuitPaddingCheckBox.IsChecked,
            ClientUseIPv4 = ClientUseIPv4CheckBox.IsChecked,
            ClientUseIPv6 = ClientUseIPv6CheckBox.IsChecked,
            PreferIPv6 = PreferIPv6CheckBox.IsChecked,
            IPv6Traffic = IPv6TrafficCheckBox.IsChecked,
            NoIPv4Traffic = NoIPv4TrafficCheckBox.IsChecked,
            IsolateDestAddr = IsolateDestAddrCheckBox.IsChecked == true,
            IsolateDestPort = IsolateDestPortCheckBox.IsChecked == true,
            IsolateClientProtocol = IsolateClientProtocolCheckBox.IsChecked == true,
            KeepAliveIsolateSocksAuth = KeepAliveIsolateSocksAuthCheckBox.IsChecked == true,
            OutboundProxyType = proxyType,
            OutboundProxyHost = proxyHost,
            OutboundProxyPort = proxyPort,
            OutboundProxyUsername = OutboundProxyUsernameBox.Text.Trim(),
            OutboundProxyPassword = OutboundProxyPasswordBox.Password,
            EnableSmartConnect = EnableSmartConnectCheckBox.IsChecked == true,
            SmartConnectModes = smartConnectModes,
            SmartConnectFallbackSeconds = smartConnectFallbackSeconds,
            OnionServices = _settings.OnionServices
        };
        settings.ValidateAdvanced();
        return settings;
    }

    private static bool TryPort(string value, out int port) => int.TryParse(value.Trim(), out port) && port is >= 1 and <= 65535;
    private static bool TryOptionalPort(string value, out int port)
    {
        if (string.IsNullOrWhiteSpace(value)) { port = 0; return true; }
        return int.TryParse(value.Trim(), out port) && port is >= 0 and <= 65535;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_tor.IsRunning)
        {
            await StopTorAsync();
            return;
        }
        try
        {
            _settings = ReadSettingsFromUi();
            ApplyStartupSetting(_settings.StartOnLogin);
            _smartConnectPlanner = _settings.EnableSmartConnect
                ? new SmartConnectPlanner(_settings.SmartConnectModes)
                : null;
            if (_smartConnectPlanner is not null)
            {
                _settings.BridgeMode = _smartConnectPlanner.Reset();
                _smartConnectStartedUtc = DateTime.UtcNow;
                AppendLog("Smart Connect enabled. Attempt order: " + string.Join(" -> ", _smartConnectPlanner.Modes));
            }
            SaveSettingsToDisk();
            UpdateEndpoints();
            ConnectButton.IsEnabled = false;
            SetUiState("Starting", T("正在启动 Tor"), 0);
            AppendLog("Starting Tor with the saved Windows proxy settings.");
            _connectStartedUtc = DateTime.UtcNow;
            _lastBootstrapProgressUtc = _connectStartedUtc;
            _lastBootstrapProgress = -1;
            _bootstrapQueryFailures = 0;
            _proxyEnableError = null;
            _nextProxyEnableRetryUtc = DateTime.MinValue;
            if (!await StartCurrentOrNextSmartConnectModeAsync())
                throw new InvalidOperationException("Tor could not start with any configured Smart Connect transport.");
            StartHttpProxy();
            ConnectButton.IsEnabled = true;
            ConnectButton.Content = "断开连接";
            NewIdentityButton.IsEnabled = true;
            SystemProxyButton.IsEnabled = true;
            _pollCount = 0;
            _monitorTimer.Start();
            await PollTorAsync();
        }
        catch (Exception ex)
        {
            AppendLog("Connection start failed: " + ex.Message);
            await StopTorAsync(showFailure: false);
            SetUiState("Error", T("无法连接"), 0);
            MessageBox.Show(this, ex.Message, T("Tor 启动失败"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopTorAsync(bool showFailure = false)
    {
        if (_stopping) return;
        _stopping = true;
        _monitorTimer.Stop();
        try
        {
            try
            {
                if (_windowsProxy.Disable()) AppendLog("Windows user proxy restored to its previous values.");
            }
            catch (Exception ex) { AppendLog("Could not restore Windows proxy: " + ex.Message); }
            if (_httpProxy is not null)
            {
                await _httpProxy.StopAsync();
                await _httpProxy.DisposeAsync();
                _httpProxy = null;
            }
            await _tor.StopAsync();
        }
        finally
        {
            _stopping = false;
            _smartConnectPlanner = null;
            _smartConnectStartedUtc = default;
            ConnectButton.IsEnabled = true;
            ConnectButton.Content = "连接 Tor";
            NewIdentityButton.IsEnabled = false;
            SystemProxyButton.IsEnabled = false;
            if (!_closing) SetUiState(showFailure ? "Error" : "Stopped", T(showFailure ? "连接失败" : "Tor 已停止"), 0);
        }
    }

    private void StartHttpProxy()
    {
        _httpProxy = new HttpProxyServer(_settings.HttpPort, _settings.SocksPort);
        _httpProxy.LogMessage += message => Dispatcher.BeginInvoke(() => AppendLog(message));
        _httpProxy.Start();
    }

    private async void MonitorTimer_Tick(object? sender, EventArgs e) => await PollTorAsync();

    private async Task PollTorAsync()
    {
        if (_polling || !_tor.IsRunning) return;
        _polling = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await _tor.GetBootstrapAsync(timeout.Token);
            var progressMatch = Regex.Match(result, @"PROGRESS=(\d+)");
            var progress = progressMatch.Success && int.TryParse(progressMatch.Groups[1].Value, out var parsed) ? parsed : 0;
            var summaryMatch = Regex.Match(result, "SUMMARY=\"([^\"]*)\"");
            var phase = summaryMatch.Success ? summaryMatch.Groups[1].Value : "正在连接 Tor 网络";
            _pollCount++;
            _bootstrapQueryFailures = 0;
            if (progress != _lastBootstrapProgress)
            {
                _lastBootstrapProgress = progress;
                _lastBootstrapProgressUtc = DateTime.UtcNow;
            }
            if (progress >= 100)
            {
                if (_smartConnectPlanner is not null)
                {
                    AppendLog("Smart Connect succeeded using " + _settings.BridgeMode + ".");
                    _smartConnectPlanner = null;
                    _smartConnectStartedUtc = default;
                }
                SetUiState("Connected", T("Tor 已连接"), 100);
                NetworkState.Text = T("已连接");
                BootstrapText.Text = T("Tor 网络已就绪");
                if (_settings.UseWindowsProxy && !_windowsProxy.IsEnabled && DateTime.UtcNow >= _nextProxyEnableRetryUtc)
                {
                    try
                    {
                        _windowsProxy.Enable(_settings.HttpPort);
                        _proxyEnableError = null;
                        AppendLog("Windows current-user HTTP/HTTPS proxy enabled.");
                    }
                    catch (Exception ex)
                    {
                        _proxyEnableError = ex.Message;
                        _nextProxyEnableRetryUtc = DateTime.UtcNow.AddSeconds(5);
                        AppendLog("Windows proxy enable failed; retrying automatically: " + ex.Message);
                    }
                }
            }
            else
            {
                SetUiState("Starting", T("正在连接"), progress);
                NetworkState.Text = T("建立 Tor 通路中");
                BootstrapText.Text = progress > 0 ? $"{progress}% · {T("建立通路中")}" : T("正在连接 Tor 网络");
                var elapsed = DateTime.UtcNow - _connectStartedUtc;
                var stalled = DateTime.UtcNow - _lastBootstrapProgressUtc;
                if (_smartConnectPlanner?.HasNext == true &&
                    DateTime.UtcNow - _smartConnectStartedUtc < TimeSpan.FromMinutes(15) &&
                    stalled >= TimeSpan.FromSeconds(_settings.SmartConnectFallbackSeconds))
                {
                    if (await AdvanceSmartConnectAsync($"bootstrap made no progress for {stalled.TotalSeconds:F0} seconds at {progress}% ({phase})"))
                        return;
                }
                if (_connectStartedUtc != default && (elapsed > TimeSpan.FromMinutes(15) || stalled > TimeSpan.FromMinutes(5)))
                {
                    var reason = progress > 0
                        ? $"Tor 引导在 {progress}% 停滞超过五分钟。当前阶段：{phase}。"
                        : "Tor 控制端口已响应，但引导没有开始。请检查网络或切换桥接方式。";
                    await FailBootstrapAsync(reason);
                    return;
                }
            }
            ProxyState.Text = _windowsProxy.IsEnabled ? "已启用" : _proxyEnableError is not null ? "启用失败" : "未启用";
            if (_proxyEnableError is not null && !_windowsProxy.IsEnabled)
            {
                BootstrapText.Text = T("Tor 已连接；Windows 代理启用失败，正在重试。检查日志查看原因。");
                ConnectionDetail.Text = T("Tor 通路已建立，但 Windows 系统代理未能启用：") + _proxyEnableError;
            }
            SidebarProxyState.Text = _tor.IsRunning ? "Tor 进程运行中" : "已停止";
            if (_pollCount % 6 == 0) RefreshServices();
        }
        catch (Exception ex)
        {
            if (!_tor.IsRunning) return;
            _pollCount++;
            _bootstrapQueryFailures++;
            if (_pollCount > 5) AppendLog("Tor status query failed: " + ex.Message);
            SetUiState("Starting", T("等待 Tor 控制端口"), 0);
            var elapsed = DateTime.UtcNow - _connectStartedUtc;
            if (_bootstrapQueryFailures >= 20 || (_connectStartedUtc != default && elapsed > TimeSpan.FromMinutes(15)))
            {
                if (_smartConnectPlanner?.HasNext == true && await AdvanceSmartConnectAsync("Tor bootstrap status could not be read"))
                    return;
                await FailBootstrapAsync("无法读取 Tor 引导状态。请检查 Tor 日志、网络连接或桥接配置。", ex);
            }
        }
        finally { _polling = false; }
    }

    private async Task<bool> StartCurrentOrNextSmartConnectModeAsync()
    {
        if (_smartConnectPlanner is null)
        {
            await _tor.StartAsync(_settings);
            return true;
        }

        while (true)
        {
            try
            {
                AppendLog("Starting Tor transport: " + _settings.BridgeMode);
                await _tor.StartAsync(_settings);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"Tor transport {_settings.BridgeMode} failed to start: {ex.Message}");
                try { await _tor.StopAsync(); } catch { }
                if (!TrySelectNextSmartConnectMode()) return false;
            }
        }
    }

    private async Task<bool> AdvanceSmartConnectAsync(string reason)
    {
        if (_smartConnectPlanner is null || !_smartConnectPlanner.HasNext) return false;
        _monitorTimer.Stop();
        _stopping = true;
        try
        {
            while (TrySelectNextSmartConnectMode())
            {
                try
                {
                    AppendLog($"Smart Connect switching to {_settings.BridgeMode}; previous attempt: {reason}.");
                    if (_tor.IsRunning) await _tor.ApplySettingsAsync(_settings);
                    else await _tor.StartAsync(_settings);
                    _connectStartedUtc = DateTime.UtcNow;
                    _lastBootstrapProgressUtc = _connectStartedUtc;
                    _lastBootstrapProgress = -1;
                    _bootstrapQueryFailures = 0;
                    _proxyEnableError = null;
                    _nextProxyEnableRetryUtc = DateTime.MinValue;
                    SetUiState("Starting", T("正在连接"), 0);
                    BootstrapText.Text = T("正在连接 Tor 网络");
                    _monitorTimer.Start();
                    return true;
                }
                catch (Exception ex)
                {
                    AppendLog($"Smart Connect transport {_settings.BridgeMode} failed to start: {ex.Message}");
                    try { await _tor.StopAsync(); } catch { }
                }
            }
            AppendLog("Smart Connect exhausted its configured transports.");
            return false;
        }
        finally { _stopping = false; }
    }

    private bool TrySelectNextSmartConnectMode()
    {
        if (_smartConnectPlanner is null || !_smartConnectPlanner.TryAdvance(out var nextMode)) return false;
        _settings.BridgeMode = nextMode;
        SaveSettingsToDisk();
        foreach (var item in BridgeModeCombo.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(item.Tag as string, nextMode, StringComparison.OrdinalIgnoreCase)) continue;
            BridgeModeCombo.SelectedItem = item;
            break;
        }
        return true;
    }

    private async Task FailBootstrapAsync(string reason, Exception? error = null)
    {
        AppendLog("Tor bootstrap failed: " + reason + (error is null ? string.Empty : $" ({error.Message})"));
        _monitorTimer.Stop();
        await StopTorAsync(showFailure: true);
        if (!_closing)
        {
            SetUiState("Error", T("连接失败"), 0);
            BootstrapText.Text = T(reason);
        }
    }

    private async Task HandleUnexpectedExitAsync()
    {
        if (_stopping || _closing) return;
        _stopping = true;
        _monitorTimer.Stop();
        try
        {
            try { _windowsProxy.Disable(); } catch (Exception ex) { AppendLog("Proxy recovery after Tor exit failed: " + ex.Message); }
            if (_httpProxy is not null)
            {
                await _httpProxy.StopAsync();
                await _httpProxy.DisposeAsync();
                _httpProxy = null;
            }
            AppendLog("Tor process exited unexpectedly; local proxy was stopped and the Windows proxy restored.");
            SetUiState("Error", T("Tor 意外停止"), 0);
        }
        finally
        {
            _stopping = false;
            ConnectButton.Content = "连接 Tor";
            ConnectButton.IsEnabled = true;
            NewIdentityButton.IsEnabled = false;
            SystemProxyButton.IsEnabled = false;
        }
    }

    private async void NewIdentity_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _tor.NewIdentityAsync();
            AppendLog("Requested a new Tor identity. Existing connections may continue until closed.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("无法更换身份"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void FetchMoatBridges_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var country = MoatCountryBox.Text.Trim();
            if (country.Length is not 0 and not 2 || country.Any(c => !char.IsAsciiLetter(c)))
                throw new InvalidOperationException("Moat country must be empty or a two-letter country code.");

            MoatStatusText.Text = T("正在从 Moat 请求桥接配置…");
            _moatTunnel ??= new MoatTunnelRuntime(_basePath);
            MoatSelection? selection = null;
            Exception? torMoatError = null;
            try
            {
                await _moatTunnel.StartAsync(cancellationToken: CancellationToken.None);
                using var handlerClient = _moatTunnel.CreateHttpClient();
                using var client = new MoatClient(new Uri("https://bridges.torproject.org/moat/circumvention/"), httpClient: handlerClient);
                selection = MoatClient.SelectConfiguration(await client.FetchSettingsAsync(country.Length == 0 ? null : country));
            }
            catch (Exception ex)
            {
                torMoatError = ex;
                AppendLog("Tor Project Moat request failed; trying Guardian Project DNSTT: " + ex.Message);
            }
            finally
            {
                await _moatTunnel.DisposeAsync();
                _moatTunnel = null;
            }

            if (selection is null)
            {
                const string dnsttConfig = "doh=https://dns.google/dns-query;pubkey=c07ae9dd7b86ded6121c3db173de048bfd4f41de38dd430e3dfaf83ec8f36a06;domain=t1.bypasscensorship.org";
                _moatTunnel = new MoatTunnelRuntime(_basePath);
                await _moatTunnel.StartDnsttAsync(dnsttConfig, CancellationToken.None);
                using var handlerClient = _moatTunnel.CreateDnsttHttpClient(dnsttConfig);
                using var client = new MoatClient(new Uri("https://tns1.bypasscensorship.org/moat/circumvention/"), httpClient: handlerClient);
                selection = MoatClient.SelectConfiguration(await client.FetchSettingsAsync(country.Length == 0 ? null : country));
            }

            if (selection is null)
                throw new InvalidOperationException("Neither Tor Project Moat nor Guardian Project Moat returned a bridge configuration.", torMoatError);
            var application = selection.ToBridgeApplication();
            if (application.BridgeMode != "custom" && application.BridgeLines.Count == 0)
                throw new InvalidOperationException("Moat selected a built-in transport but returned no bridge lines.");

            _loadingUi = true;
            try
            {
                foreach (var item in BridgeModeCombo.Items.OfType<ComboBoxItem>())
                {
                    if (!string.Equals(item.Tag as string, application.BridgeMode, StringComparison.OrdinalIgnoreCase)) continue;
                    BridgeModeCombo.SelectedItem = item;
                    break;
                }
                BridgeLinesBox.Text = string.Join(Environment.NewLine, application.BridgeLines);
                BridgeState.Text = BridgeModeCombo.SelectedItem is ComboBoxItem selected ? selected.Content?.ToString() ?? application.BridgeMode : application.BridgeMode;
            }
            finally { _loadingUi = false; }

            MoatStatusText.Text = T("Moat 已返回桥接配置。点击“连接 Tor”应用本次选择。");
            AppendLog($"Moat selected {application.BridgeMode} with {application.BridgeLines.Count} bridge line(s); country={selection.Country ?? "unspecified"}.");
        }
        catch (Exception ex)
        {
            MoatStatusText.Text = T("Moat 请求失败：") + ex.Message;
            AppendLog("Moat bridge request failed: " + ex.Message);
        }
        finally
        {
            if (_moatTunnel is not null)
            {
                await _moatTunnel.DisposeAsync();
                _moatTunnel = null;
            }
        }
    }

    private void SystemProxy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_tor.IsRunning)
            {
                MessageBox.Show(this, T("请先点击“连接 Tor”。Tor 进程启动后，本机 HTTP 代理端口才可用。"), T("代理尚未启动"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_windowsProxy.IsEnabled)
            {
                _windowsProxy.Disable();
                _proxyEnableError = null;
                _settings.UseWindowsProxy = false;
                WindowsProxyCheckBox.IsChecked = false;
                SaveSettingsToDisk();
                AppendLog("Windows current-user proxy disabled and original values restored.");
            }
            else
            {
                _windowsProxy.Enable(_settings.HttpPort);
                _proxyEnableError = null;
                _settings.UseWindowsProxy = true;
                WindowsProxyCheckBox.IsChecked = true;
                SaveSettingsToDisk();
                AppendLog("Windows current-user HTTP/HTTPS proxy enabled.");
            }
            UpdateProxyUi();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Windows 代理设置失败"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CopySocks_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText($"socks5://127.0.0.1:{_settings.SocksPort}");
        AppendLog("Copied local SOCKS5 endpoint.");
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var wasRunning = _tor.IsRunning;
            var proxyWasEnabled = _windowsProxy.IsEnabled;
            _settings = ReadSettingsFromUi();
            ApplyStartupSetting(_settings.StartOnLogin);
            SaveSettingsToDisk();
            UpdateEndpoints();
            if (proxyWasEnabled) _windowsProxy.Disable();
            if (wasRunning)
            {
                AppendLog("Applying changed Tor settings; restarting the Tor process.");
                _stopping = true;
                try
                {
                    if (_httpProxy is not null)
                    {
                        await _httpProxy.StopAsync();
                        await _httpProxy.DisposeAsync();
                        _httpProxy = null;
                    }
                    await _tor.ApplySettingsAsync(_settings);
                    StartHttpProxy();
                    _connectStartedUtc = DateTime.UtcNow;
                    _lastBootstrapProgressUtc = _connectStartedUtc;
                    _lastBootstrapProgress = -1;
                    _bootstrapQueryFailures = 0;
                    _proxyEnableError = null;
                    _nextProxyEnableRetryUtc = DateTime.MinValue;
                }
                finally { _stopping = false; }
                _monitorTimer.Start();
            }
            SetUiState(wasRunning ? "Starting" : "Stopped", T(wasRunning ? "正在重新连接" : "设置已保存"), 0);
            AppendLog("Settings saved to the project-local data directory.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ApplyStartupSetting(bool enabled)
    {
        if (enabled) _startupController.Enable();
        else _startupController.Disable();
    }

    private async void StartSnowflakeVolunteer_Click(object sender, RoutedEventArgs e)
    {
        StartSnowflakeVolunteerButton.IsEnabled = false;
        try
        {
            await _snowflakeVolunteer.StartAsync();
            SetSnowflakeVolunteerUi(true);
        }
        catch (Exception ex)
        {
            SetSnowflakeVolunteerUi(false);
            MessageBox.Show(this, ex.Message, T("无法启动 Snowflake 志愿中继"), MessageBoxButton.OK, MessageBoxImage.Warning);
            AppendLog("Snowflake volunteer relay could not start: " + ex.Message);
        }
    }

    private async void StopSnowflakeVolunteer_Click(object sender, RoutedEventArgs e)
    {
        StopSnowflakeVolunteerButton.IsEnabled = false;
        try { await _snowflakeVolunteer.StopAsync(); }
        catch (Exception ex) { AppendLog("Snowflake volunteer relay shutdown failed: " + ex.Message); }
        finally { SetSnowflakeVolunteerUi(false); }
    }

    private void SetSnowflakeVolunteerUi(bool running)
    {
        SnowflakeVolunteerStateText.Text = T(running ? "志愿中继运行中" : "已停止");
        StartSnowflakeVolunteerButton.IsEnabled = !running;
        StopSnowflakeVolunteerButton.IsEnabled = running;
    }

    private async void AddService_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = ReadSettingsFromUi();
            var name = ServiceNameBox.Text.Trim();
            if (name.Length is < 1 or > 48 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                throw new InvalidOperationException("Service name may contain 1–48 letters, numbers, hyphens, or underscores.");
            if (!int.TryParse(ServiceVirtualPortBox.Text, out var virtualPort) || virtualPort is < 1 or > 65535)
                throw new InvalidOperationException("Onion port must be between 1 and 65535.");
            if (!int.TryParse(ServiceTargetPortBox.Text, out var targetPort) || targetPort is < 1 or > 65535)
                throw new InvalidOperationException("Local port must be between 1 and 65535.");
            if (_settings.OnionServices.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A service with this name already exists.");
            _settings.OnionServices.Add(new OnionServiceEntry { Name = name, VirtualPort = virtualPort, TargetPort = targetPort });
            await ApplySavedSettingsAsync("Added onion service.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法添加服务", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void DeleteService_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        _settings.OnionServices.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        try { await ApplySavedSettingsAsync("Removed onion service " + name + "."); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法移除服务", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportOnionServices_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Orbot Onion service backup (*.zip)|*.zip|All files (*.*)|*.*",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = "orbot-onion-services.zip",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            OnionServiceBackup.Export(dialog.FileName, _settings.OnionServices);
            AppendLog("Exported Onion service configuration (private service keys remain local). ");
            MessageBox.Show(this, "服务配置已导出。私钥不会写入备份文件。", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法导出服务配置", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ImportOnionServices_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Orbot Onion service backup (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = OnionServiceBackup.Import(dialog.FileName);
            _settings = ReadSettingsFromUi();
            _settings.OnionServices = imported.ToList();
            await ApplySavedSettingsAsync("Imported Onion service configuration.");
            MessageBox.Show(this, "服务配置已导入。若服务私钥不在本机，Tor 会为其创建新的地址。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法导入服务配置", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void AddClientAuth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var store = new ClientOnionAuthStore(_clientAuthPath);
            store.Add(ClientAuthDomainBox.Text, ClientAuthKeyBox.Password);
            ClientAuthKeyBox.Clear();
            RefreshClientAuthList();
            AppendLog("Added v3 Onion client authorization for " + ClientOnionAuthStore.NormalizeDomain(ClientAuthDomainBox.Text) + ".");
            await RestartTorForClientAuthIfRunningAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("无法添加授权"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ImportClientAuth_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Tor client authorization (*.auth_private)|*.auth_private|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var store = new ClientOnionAuthStore(_clientAuthPath);
            var domain = store.Import(await File.ReadAllTextAsync(dialog.FileName));
            RefreshClientAuthList();
            AppendLog("Imported v3 Onion client authorization for " + domain + ".");
            await RestartTorForClientAuthIfRunningAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("无法导入授权"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void RemoveClientAuth_Click(object sender, RoutedEventArgs e)
    {
        if (ClientAuthList.SelectedItem is not string domain) return;
        try
        {
            new ClientOnionAuthStore(_clientAuthPath).Remove(domain);
            RefreshClientAuthList();
            AppendLog("Removed v3 Onion client authorization for " + domain + ".");
            await RestartTorForClientAuthIfRunningAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("无法删除授权"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ExportClientAuth_Click(object sender, RoutedEventArgs e)
    {
        if (ClientAuthList.SelectedItem is not string domain)
        {
            MessageBox.Show(this, T("请选择一个已保存的授权。"), T("无法导出授权"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Tor client authorization (*.auth_private)|*.auth_private|All files (*.*)|*.*",
            DefaultExt = ".auth_private",
            AddExtension = true,
            FileName = domain + ".auth_private",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var store = new ClientOnionAuthStore(_clientAuthPath, requirePrivateAcl: false);
            File.WriteAllText(dialog.FileName, store.Read(domain) + Environment.NewLine, new System.Text.UTF8Encoding(false));
            AppendLog("Exported v3 Onion client authorization for " + domain + ".");
            MessageBox.Show(this, T("授权已导出。请像密码一样保护该文件。"), T("导出完成"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("无法导出授权"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RefreshClientAuthList()
    {
        var store = new ClientOnionAuthStore(_clientAuthPath);
        ClientAuthList.ItemsSource = store.ListDomains();
    }

    private async Task RestartTorForClientAuthIfRunningAsync()
    {
        if (!_tor.IsRunning) return;
        AppendLog("Applying client authorization changes; restarting Tor.");
        var proxyWasEnabled = _windowsProxy.IsEnabled;
        if (proxyWasEnabled) _windowsProxy.Disable();
        _stopping = true;
        try
        {
            await _tor.ApplySettingsAsync(_settings);
            if (_httpProxy is not null)
            {
                await _httpProxy.StopAsync();
                await _httpProxy.DisposeAsync();
                _httpProxy = null;
            }
            StartHttpProxy();
            if (proxyWasEnabled)
            {
                try { _windowsProxy.Enable(_settings.HttpPort); }
                catch (Exception ex) { AppendLog("Windows proxy re-enable after client-auth update failed: " + ex.Message); }
            }
            _connectStartedUtc = DateTime.UtcNow;
            _lastBootstrapProgressUtc = _connectStartedUtc;
            _lastBootstrapProgress = -1;
            _bootstrapQueryFailures = 0;
            _monitorTimer.Start();
        }
        finally { _stopping = false; }
    }

    private async Task ApplySavedSettingsAsync(string logMessage)
    {
        var wasRunning = _tor.IsRunning;
        SaveSettingsToDisk();
        if (wasRunning)
        {
            _stopping = true;
            try { await _tor.ApplySettingsAsync(_settings); }
            finally { _stopping = false; }
            _monitorTimer.Start();
        }
        RefreshServices();
        AppendLog(logMessage);
    }

    private void SaveSettingsToDisk()
    {
        _settings.SettingsVersion = 2;
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
    }

    private void RefreshServices()
    {
        _serviceRows.Clear();
        foreach (var (name, hostname) in _tor.ReadOnionServiceAddresses(_settings))
            _serviceRows.Add(new ServiceView(name, hostname, _settings.OnionServices.FirstOrDefault(s => s.Name == name)?.VirtualPort ?? 80,
                _settings.OnionServices.FirstOrDefault(s => s.Name == name)?.TargetPort ?? 0));
        ServicesList.ItemTemplate = CreateServiceTemplate();
    }

    private DataTemplate CreateServiceTemplate()
    {
        var template = new DataTemplate(typeof(ServiceView));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(17, 24, 32)));
        border.SetValue(Border.BorderBrushProperty, (Brush)FindResource("LineBrush"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(16));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 10));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(new FrameworkElementFactory(typeof(ColumnDefinition)));
        var secondCol = new FrameworkElementFactory(typeof(ColumnDefinition));
        secondCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(110));
        grid.AppendChild(secondCol);
        var thirdCol = new FrameworkElementFactory(typeof(ColumnDefinition));
        thirdCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(100));
        grid.AppendChild(thirdCol);
        var details = new FrameworkElementFactory(typeof(StackPanel));
        details.SetValue(Grid.ColumnProperty, 0);
        var nameText = new FrameworkElementFactory(typeof(TextBlock));
        nameText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ServiceView.Name)));
        nameText.SetValue(TextBlock.FontSizeProperty, 15d);
        nameText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        details.AppendChild(nameText);
        var hostText = new FrameworkElementFactory(typeof(TextBlock));
        hostText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ServiceView.Hostname)));
        hostText.SetValue(TextBlock.ForegroundProperty, (Brush)FindResource("AccentBrush"));
        hostText.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Code, Consolas"));
        hostText.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0));
        details.AppendChild(hostText);
        grid.AppendChild(details);
        var portText = new FrameworkElementFactory(typeof(TextBlock));
        portText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ServiceView.Ports)));
        portText.SetValue(Grid.ColumnProperty, 1);
        portText.SetValue(TextBlock.ForegroundProperty, (Brush)FindResource("SecondaryBrush"));
        portText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(portText);
        var remove = new FrameworkElementFactory(typeof(Button));
        remove.SetValue(Grid.ColumnProperty, 2);
        remove.SetValue(Button.ContentProperty, "移除");
        remove.SetBinding(Button.TagProperty, new System.Windows.Data.Binding(nameof(ServiceView.Name)));
        remove.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        remove.AddHandler(Button.ClickEvent, new RoutedEventHandler(DeleteService_Click));
        grid.AppendChild(remove);
        border.AppendChild(grid);
        template.VisualTree = border;
        return template;
    }

    private void BridgeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || BridgeModeCombo.SelectedItem is not ComboBoxItem item) return;
        BridgeState.Text = item.Content?.ToString() ?? "直连";
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string page }) return;
        _activePage = page;
        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        BridgesPage.Visibility = page == "Bridges" ? Visibility.Visible : Visibility.Collapsed;
        ServicesPage.Visibility = page == "Services" ? Visibility.Visible : Visibility.Collapsed;
        ClientAuthPage.Visibility = page == "ClientAuth" ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = page == "Logs" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        var selectedButton = page switch
        {
            "Bridges" => NavBridges,
            "Services" => NavServices,
            "ClientAuth" => NavClientAuth,
            "Logs" => NavLogs,
            "Settings" => NavSettings,
            _ => NavOverview
        };
        foreach (var button in new[] { NavOverview, NavBridges, NavServices, NavClientAuth, NavLogs, NavSettings })
            button.Tag = ReferenceEquals(button, selectedButton) ? "Selected" : null;
        var pageText = page switch
        {
            "Bridges" => (T("桥接与网络"), T("根据当前网络配置连接方式与出口偏好。")),
            "Services" => (T("洋葱服务"), T("将你在本机运行的服务发布为 Tor v3 地址。")),
            "ClientAuth" => (T("洋葱客户端授权"), T("管理访问私有 v3 Onion 服务所需的客户端密钥。")),
            "Logs" => (T("运行日志"), T("Tor 控制状态与本机代理诊断。")),
            "Settings" => (T("设置"), T("管理本机代理端口和 Windows 用户代理行为。")),
            _ => (T("连接概览"), T("通过 Tor 建立加密连接，并将应用流量导入本机代理。"))
        };
        PageTitle.Text = pageText.Item1;
        PageSubtitle.Text = pageText.Item2;
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e) => LogTextBox.Clear();

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_appLogPath)!);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(_appLogPath)}\"") { UseShellExecute = true });
    }

    private void SetUiState(string state, string label, int progress)
    {
        var changed = !string.Equals(_uiState, state, StringComparison.Ordinal);
        _uiState = state;
        var connected = state == "Connected";
        var starting = state == "Starting";
        HeaderStatus.Text = label;
        SidebarProxyState.Text = _tor.IsRunning ? "Tor 进程运行中" : "已停止";
        StatusDot.Fill = connected ? (Brush)FindResource("AccentBrush") : starting ? (Brush)FindResource("WarningBrush") : new SolidColorBrush(Color.FromRgb(112, 126, 137));
        NetworkState.Text = connected ? T("已连接") : starting ? T("连接中") : state == "Error" ? T("错误") : T("未连接");
        ConnectionHeadline.Text = connected ? T("Tor 通路已建立") : starting ? T("正在连接 Tor 网络") : state == "Error" ? T("连接未能建立") : T("你的连接由你掌控");
        ConnectionDetail.Text = connected
            ? T("代理端点已在本机回环地址监听。应用可以通过 SOCKS5 或 HTTP / HTTPS 代理接入。")
            : starting ? T("Tor 正在协商中。首次连接可能需要几分钟，受桥接和当前网络影响。")
            : T("启动 Tor 后，可通过本机 SOCKS5 或 HTTP 代理连接。");
        BootstrapText.Text = connected ? T("Tor 网络已就绪") : starting ? $"{progress}% · {T("建立通路中")}" : T("Tor 已停止");
        ConnectButton.Content = _tor.IsRunning ? "断开连接" : "连接 Tor";
        ConnectButton.IsEnabled = true;
        NewIdentityButton.IsEnabled = _tor.IsRunning;
        SystemProxyButton.IsEnabled = _tor.IsRunning;
        SystemProxyButton.Content = _windowsProxy.IsEnabled ? T("恢复 Windows 代理") : T("设置 Windows 代理");
        UpdateProxyUi();
        UpdateEndpointLabels();
        if (changed)
        {
            if (connected) StartConnectedPulse(); else StopPulse();
        }
    }

    private void StartConnectedPulse()
    {
        StopPulse();
        var animation = new DoubleAnimation(0.92, 1.08, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        var orbit = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(14))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        OrbitRotation.BeginAnimation(RotateTransform.AngleProperty, orbit);
    }

    private void StopPulse()
    {
        PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PulseScale.ScaleX = 1;
        PulseScale.ScaleY = 1;
        OrbitRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        OrbitRotation.Angle = 0;
    }

    private void UpdateEndpoints()
    {
        SocksEndpoint.Text = $"127.0.0.1:{_settings.SocksPort}";
        HttpEndpoint.Text = $"127.0.0.1:{_settings.HttpPort}";
        BridgeState.Text = BridgeModeCombo.SelectedItem is ComboBoxItem selected ? selected.Content?.ToString() ?? "直连" : "直连";
        UpdateEndpointLabels();
        UpdateProxyUi();
    }

    private void UpdateEndpointLabels()
    {
        SocksEndpoint.Text = $"127.0.0.1:{(int.TryParse(SocksPortBox.Text, out var socks) ? socks : _settings.SocksPort)}";
        HttpEndpoint.Text = $"127.0.0.1:{(int.TryParse(HttpPortBox.Text, out var http) ? http : _settings.HttpPort)}";
    }

    private void UpdateProxyUi()
    {
        var active = _windowsProxy.IsEnabled;
        ProxyState.Text = active ? "已启用" : _proxyEnableError is not null ? "启用失败" : "未启用";
        SystemProxyButton.Content = active ? "恢复 Windows 代理" : "设置 Windows 代理";
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        LogTextBox.AppendText(line + Environment.NewLine);
        LogTextBox.ScrollToEnd();
        try { File.AppendAllText(_appLogPath, line + Environment.NewLine); } catch { }
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        await StopTorAsync();
        await _snowflakeVolunteer.DisposeAsync();
        try { _windowsProxy.Disable(); } catch { }
        await _tor.DisposeAsync();
        _trayIcon.Dispose();
        _allowClose = true;
        Close();
    }

    private sealed record ServiceView(string Name, string Hostname, int VirtualPort, int TargetPort)
    {
        public string Ports => $"{VirtualPort} → 127.0.0.1:{TargetPort}";
    }
}
