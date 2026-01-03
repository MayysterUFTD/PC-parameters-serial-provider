using System;
using System.Windows.Forms;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareMonitorTray.Protocol;

namespace HardwareMonitorTray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _menu;
        private HardwareMonitorService _monitor;
        private SerialPortService _serial;
        private SensorDataCollector _collector;
        private ConfigManager _config;
        private TrayIconManager _iconMgr;
        private System.Windows.Forms.Timer _sendTimer;
        private System.Windows.Forms.Timer _iconTimer;
        private bool _running = false;
        private int _animFrame = 0;
        private float _lastCpuTemp = 0, _lastCpuLoad = 0, _lastGpuLoad = 0;
        private JsonSerializerOptions _jsonOpt;

        public TrayApplicationContext()
        {
            _config = new ConfigManager();
            _monitor = new HardwareMonitorService();
            _monitor.RefreshIntervalMs = _config.Config.RefreshIntervalMs;
            _serial = new SerialPortService { Mode = _config.Config.ProtocolMode };
            _collector = new SensorDataCollector(_monitor);
            _iconMgr = new TrayIconManager();

            _jsonOpt = new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                WriteIndented = false
            };

            InitTray();
            InitTimers();

            if (_config.Config.AutoStart)
                StartMonitoring();
        }

        private void InitTray()
        {
            _menu = new ContextMenuStrip();

            var startItem = new ToolStripMenuItem("▶ Start/Stop", null, OnStartStop) { Font = new Font(_menu.Font, FontStyle.Bold) };
            _menu.Items.Add(startItem);

            // NOWY PRZYCISK - Restart Serial
            var restartItem = new ToolStripMenuItem("🔄 Restart Serial", null, OnRestartSerial);
            _menu.Items.Add(restartItem);

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("⚙ Settings", null, OnSettings));
            _menu.Items.Add(new ToolStripMenuItem("📊 Statistics", null, OnStats));
            _menu.Items.Add(new ToolStripMenuItem("📌 Pin to Taskbar", null, OnPin));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("❌ Exit", null, OnExit));

            _trayIcon = new NotifyIcon
            {
                Icon = _iconMgr.CreateModernIcon(false),
                ContextMenuStrip = _menu,
                Visible = true,
                Text = "Hardware Monitor - Stopped"
            };
            _trayIcon.DoubleClick += OnSettings;
        }

        private void InitTimers()
        {
            _sendTimer = new System.Windows.Forms.Timer { Interval = _config.Config.SendIntervalMs };
            _sendTimer.Tick += OnSendTick;

            _iconTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _iconTimer.Tick += OnIconTick;
        }

        private void OnSendTick(object s, EventArgs e)
        {
            try
            {
                var selectedCount = _config.Config.SelectedSensors.Count;
                var sensors = _collector.CollectData(_config.Config.SelectedSensors);

                System.Diagnostics.Debug.WriteLine($"[SEND] Selected:  {selectedCount}, Collected: {sensors.Count}");

                if (sensors.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[SEND] No sensors to send!");
                    return;
                }

                _serial.SendData(sensors);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SEND ERROR] {ex.Message}");
            }
        }

        private void OnIconTick(object s, EventArgs e)
        {
            if (_running && _config.Config.IconStyle == IconStyle.Animated)
            {
                _animFrame++;
                UpdateIcon(_iconMgr.CreatePulseIcon(_animFrame));
            }
        }

        private void UpdateTrayIcon()
        {
            var style = _config.Config.IconStyle;
            Icon icon = !_running
                ? (style == IconStyle.Modern ? _iconMgr.CreateModernIcon(false) : _iconMgr.CreateStatusIcon(TrayIconManager.IconState.Stopped))
                : style switch
                {
                    IconStyle.Temperature => _iconMgr.CreateTemperatureIcon(_lastCpuTemp),
                    IconStyle.LoadBars => _iconMgr.CreateLoadIcon(_lastCpuLoad, _lastGpuLoad),
                    IconStyle.Modern => _iconMgr.CreateModernIcon(true, _lastCpuTemp),
                    IconStyle.Animated => _iconMgr.CreatePulseIcon(_animFrame),
                    _ => GetStatusByTemp()
                };
            UpdateIcon(icon);
        }

        private Icon GetStatusByTemp() =>
            _lastCpuTemp >= 85 ? _iconMgr.CreateStatusIcon(TrayIconManager.IconState.Hot) :
            _lastCpuTemp >= 75 ? _iconMgr.CreateStatusIcon(TrayIconManager.IconState.Warning) :
            _iconMgr.CreateStatusIcon(TrayIconManager.IconState.Running);

        private void UpdateIcon(Icon newIcon)
        {
            var old = _trayIcon.Icon;
            _trayIcon.Icon = newIcon;
            if (old != null && old != SystemIcons.Application)
                try { old.Dispose(); } catch { }
        }

        private void UpdateTooltip()
        {
            var mode = _serial.Mode == ProtocolMode.Binary ? "BIN v2" : _serial.Mode == ProtocolMode.Text ? "TXT" : "JSON";
            var tip = $"Hardware Monitor - {_config.Config.ComPort} [{mode}]";
            if (_lastCpuTemp > 0) tip += $"\nCPU: {_lastCpuTemp:0}°C ({_lastCpuLoad:0}%)";
            if (_lastGpuLoad > 0) tip += $"\nGPU: {_lastGpuLoad:0}%";
            _trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        public void StartMonitoring()
        {
            try
            {
                _serial.Connect(_config.Config.ComPort, _config.Config.BaudRate);
                _serial.Mode = _config.Config.ProtocolMode;
                _sendTimer.Interval = _config.Config.SendIntervalMs;
                _sendTimer.Start();

                if (_config.Config.IconStyle == IconStyle.Animated)
                    _iconTimer.Start();

                _running = true;
                UpdateTrayIcon();
                _trayIcon.ShowBalloonTip(2000, "Hardware Monitor", $"Started on {_config.Config.ComPort} (Protocol v2)", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                UpdateIcon(_iconMgr.CreateStatusIcon(TrayIconManager.IconState.Error));
                MessageBox.Show($"Failed to start: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void StopMonitoring()
        {
            _sendTimer.Stop();
            _iconTimer.Stop();
            _serial.Disconnect();
            _running = false;
            UpdateTrayIcon();
            _trayIcon.Text = "Hardware Monitor - Stopped";
        }

        /// <summary>
        /// Restartuje połączenie szeregowe bez zatrzymywania monitorowania
        /// </summary>
        public void RestartSerial()
        {
            if (!_running)
            {
                _trayIcon.ShowBalloonTip(2000, "Hardware Monitor", "Monitoring not running. Use Start first.", ToolTipIcon.Warning);
                return;
            }

            try
            {
                _sendTimer.Stop();
                _serial.Disconnect();

                System.Threading.Thread.Sleep(500); // Krótka pauza dla portu

                _serial.Connect(_config.Config.ComPort, _config.Config.BaudRate);
                _serial.Mode = _config.Config.ProtocolMode;
                _sendTimer.Start();

                _trayIcon.ShowBalloonTip(2000, "Hardware Monitor", $"Serial restarted on {_config.Config.ComPort}", ToolTipIcon.Info);
                System.Diagnostics.Debug.WriteLine($"[Serial] Restarted on {_config.Config.ComPort}");
            }
            catch (Exception ex)
            {
                UpdateIcon(_iconMgr.CreateStatusIcon(TrayIconManager.IconState.Error));
                _trayIcon.ShowBalloonTip(3000, "Hardware Monitor", $"Restart failed: {ex.Message}", ToolTipIcon.Error);
                System.Diagnostics.Debug.WriteLine($"[Serial] Restart error: {ex.Message}");
            }
        }

        public void ToggleMonitoring(bool start)
        {
            if (start && !_running)
                StartMonitoring();
            else if (!start && _running)
                StopMonitoring();
        }

        public bool IsMonitoringRunning() => _running;

        private void OnStartStop(object s, EventArgs e)
        {
            if (_running) StopMonitoring();
            else StartMonitoring();
        }

        private void OnRestartSerial(object s, EventArgs e)
        {
            RestartSerial();
        }

        private void OnSettings(object s, EventArgs e)
        {
            using var form = new SettingsForm(
                _config,
                _monitor,
                ToggleMonitoring,
                IsMonitoringRunning
            );

            if (form.ShowDialog() == DialogResult.OK)
            {
                if (_running)
                {
                    _sendTimer.Interval = _config.Config.SendIntervalMs;
                    _serial.Mode = _config.Config.ProtocolMode;
                }
            }
        }

        private void OnStats(object s, EventArgs e)
        {
            var mapper = SensorIdMapper.Instance;
            var msg = $"📡 Protocol: {_serial.Mode} (v2 - 16-bit IDs)\n" +
                      $"📤 Sent: {_serial.PacketsSent}\n" +
                      $"❌ Errors: {_serial.PacketsErrors}\n" +
                      $"✅ Success: {_serial.SuccessRate:0.0}%\n\n" +
                      $"🗺 Mapped Sensors: {mapper.Count}\n\n" +
                      $"🌡 CPU:  {_lastCpuTemp:0.0}°C\n" +
                      $"📊 CPU Load: {_lastCpuLoad: 0.0}%\n" +
                      $"🎮 GPU Load: {_lastGpuLoad:0.0}%";
            MessageBox.Show(msg, "Statistics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnPin(object s, EventArgs e)
        {
            MessageBox.Show(
                "To keep the icon visible:\n\n" +
                "1. Click ▲ in the taskbar\n" +
                "2. Drag the Hardware Monitor icon to the taskbar\n\n" +
                "Or:  Right-click taskbar → Taskbar settings → Select icons",
                "📌 Pin to Taskbar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExit(object s, EventArgs e)
        {
            StopMonitoring();
            _monitor.Dispose();
            _iconMgr.Dispose();
            _trayIcon.Visible = false;
            Application.Exit();
        }
    }
}