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
        private ContextMenuStrip _contextMenu;
        private HardwareMonitorService _monitorService;
        private SerialPortService _serialService;
        private SensorDataCollector _dataCollector;
        private ConfigManager _configManager;
        private TrayIconManager _iconManager;
        private System.Windows.Forms.Timer _sendTimer;
        private System.Windows.Forms.Timer _iconAnimationTimer;
        private bool _isRunning = false;
        private JsonSerializerOptions _jsonOptions;
        private int _animationFrame = 0;
        private float _lastCpuTemp = 0;
        private float _lastCpuLoad = 0;
        private float _lastGpuLoad = 0;

        public TrayApplicationContext()
        {
            _configManager = new ConfigManager();
            _monitorService = new HardwareMonitorService();
            _serialService = new SerialPortService();
            _dataCollector = new SensorDataCollector(_monitorService);
            _iconManager = new TrayIconManager();

            _serialService.Mode = _configManager.Config.ProtocolMode;

            _jsonOptions = new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                WriteIndented = false
            };

            InitializeTrayIcon();
            InitializeTimers();

            if (_configManager.Config.AutoStart)
            {
                StartMonitoring();
            }
        }

        private void InitializeTrayIcon()
        {
            _contextMenu = new ContextMenuStrip();

            var startStopItem = new ToolStripMenuItem("▶️ Start/Stop", null, OnStartStopClick)
            {
                Font = new Font(_contextMenu.Font, FontStyle.Bold)
            };
            _contextMenu.Items.Add(startStopItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("⚙️ Settings", null, OnSettingsClick);
            _contextMenu.Items.Add(settingsItem);

            var statsItem = new ToolStripMenuItem("📊 Statistics", null, OnStatisticsClick);
            _contextMenu.Items.Add(statsItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Nowa opcja - przypnij ikonę
            var pinIconItem = new ToolStripMenuItem("📌 Pin to Taskbar", null, OnPinIconClick);
            _contextMenu.Items.Add(pinIconItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("❌ Exit", null, OnExitClick);
            _contextMenu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon()
            {
                Icon = _iconManager.CreateModernIcon(false),
                ContextMenuStrip = _contextMenu,
                Visible = true,
                Text = "Hardware Monitor - Stopped"
            };

            _trayIcon.DoubleClick += OnSettingsClick;

            // Przy pierwszym uruchomieniu pokaż instrukcję
            if (_configManager.Config.FirstRun)
            {
                _configManager.Config.FirstRun = false;
                _configManager.SaveConfig();

                _trayIcon.ShowBalloonTip(5000, "💡 Tip",
                    "Right-click the tray icon and select 'Pin to Taskbar' to keep it visible.",
                    ToolTipIcon.Info);
            }
        }

        private void OnPinIconClick(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Would you like to open Taskbar Settings to pin the icon?\n\n" +
                "Click 'Yes' to open settings, or 'No' for instructions.",
                "📌 Pin Icon to Taskbar",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                TrayIconHelper.OpenTaskbarSettings();
            }
            else if (result == DialogResult.No)
            {
                TrayIconHelper.ShowPinInstructions();
            }
        }

        private void InitializeTimers()
        {
            _sendTimer = new System.Windows.Forms.Timer();
            _sendTimer.Interval = _configManager.Config.SendIntervalMs;
            _sendTimer.Tick += OnTimerTick;

            _iconAnimationTimer = new System.Windows.Forms.Timer();
            _iconAnimationTimer.Interval = 500;
            _iconAnimationTimer.Tick += OnIconAnimationTick;
        }

        private void OnIconAnimationTick(object sender, EventArgs e)
        {
            if (_isRunning && _configManager.Config.IconStyle == IconStyle.Animated)
            {
                _animationFrame++;
                var icon = _iconManager.CreatePulseIcon(_animationFrame);
                UpdateIcon(icon);
            }
        }

        private void UpdateTrayIcon()
        {
            var style = _configManager.Config.IconStyle;
            Icon newIcon;

            if (!_isRunning)
            {
                newIcon = style switch
                {
                    IconStyle.Modern => _iconManager.CreateModernIcon(false),
                    _ => _iconManager.CreateStatusIcon(TrayIconManager.IconState.Stopped)
                };
            }
            else
            {
                newIcon = style switch
                {
                    IconStyle.Temperature => _iconManager.CreateTemperatureIcon(_lastCpuTemp),
                    IconStyle.LoadBars => _iconManager.CreateLoadIcon(_lastCpuLoad, _lastGpuLoad),
                    IconStyle.Modern => _iconManager.CreateModernIcon(true, _lastCpuTemp),
                    IconStyle.Animated => _iconManager.CreatePulseIcon(_animationFrame),
                    _ => GetStatusIconByTemp(_lastCpuTemp)
                };
            }

            UpdateIcon(newIcon);
        }

        private Icon GetStatusIconByTemp(float temp)
        {
            if (temp >= 85)
                return _iconManager.CreateStatusIcon(TrayIconManager.IconState.Hot);
            else if (temp >= 75)
                return _iconManager.CreateStatusIcon(TrayIconManager.IconState.Warning);
            else
                return _iconManager.CreateStatusIcon(TrayIconManager.IconState.Running);
        }

        private void UpdateIcon(Icon newIcon)
        {
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = newIcon;

            if (oldIcon != null && oldIcon != SystemIcons.Application && oldIcon != SystemIcons.Shield)
            {
                try { oldIcon.Dispose(); } catch { }
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                if (_serialService.Mode == ProtocolMode.Json)
                {
                    var data = _monitorService.GetSelectedSensorData(_configManager.Config.SelectedSensors);
                    var json = JsonSerializer.Serialize(data, _jsonOptions);
                    _serialService.SendRawData(json);
                }
                else
                {
                    var sensors = _dataCollector.CollectData(_configManager.Config.SelectedSensors);
                    _serialService.SendData(sensors);

                    foreach (var sensor in sensors)
                    {
                        switch (sensor.Id)
                        {
                            case SensorId.CpuTemp:
                                _lastCpuTemp = sensor.Value;
                                break;
                            case SensorId.CpuLoad:
                                _lastCpuLoad = sensor.Value;
                                break;
                            case SensorId.GpuLoad:
                                _lastGpuLoad = sensor.Value;
                                break;
                        }
                    }
                }

                if (_configManager.Config.IconStyle != IconStyle.Animated)
                {
                    UpdateTrayIcon();
                }

                UpdateTooltip();
            }
            catch (Exception ex)
            {
                var icon = _iconManager.CreateStatusIcon(TrayIconManager.IconState.Error);
                UpdateIcon(icon);
                _trayIcon.ShowBalloonTip(3000, "Error", ex.Message, ToolTipIcon.Error);
            }
        }

        private void UpdateTooltip()
        {
            string modeText = _serialService.Mode switch
            {
                ProtocolMode.Binary => "BIN",
                ProtocolMode.Text => "TXT",
                _ => "JSON"
            };

            string tooltip = $"Hardware Monitor - {_configManager.Config.ComPort} [{modeText}]\n";

            if (_lastCpuTemp > 0)
                tooltip += $"CPU:  {_lastCpuTemp:F0}°C ({_lastCpuLoad:F0}%)\n";
            if (_lastGpuLoad > 0)
                tooltip += $"GPU:  {_lastGpuLoad:F0}%";

            if (tooltip.Length > 63)
                tooltip = tooltip.Substring(0, 63);

            _trayIcon.Text = tooltip;
        }

        private void OnStatisticsClick(object sender, EventArgs e)
        {
            var stats = $"📡 Protocol: {_serialService.Mode}\n" +
                        $"🎨 Icon Style: {_configManager.Config.IconStyle}\n\n" +
                        $"📤 Packets Sent: {_serialService.PacketsSent}\n" +
                        $"❌ Errors: {_serialService.PacketsErrors}\n" +
                        $"✅ Success Rate: {_serialService.SuccessRate:F1}%\n\n" +
                        $"🔧 Sensors Selected: {_configManager.Config.SelectedSensors.Count}\n" +
                        $"⏱️ Send Interval: {_configManager.Config.SendIntervalMs}ms\n\n" +
                        $"🌡️ CPU Temp: {_lastCpuTemp: F1}°C\n" +
                        $"📊 CPU Load:  {_lastCpuLoad:F1}%\n" +
                        $"🎮 GPU Load: {_lastGpuLoad:F1}%";

            MessageBox.Show(stats, "📊 Statistics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void StartMonitoring()
        {
            try
            {
                _serialService.Connect(_configManager.Config.ComPort, _configManager.Config.BaudRate);
                _serialService.Mode = _configManager.Config.ProtocolMode;
                _sendTimer.Start();

                if (_configManager.Config.IconStyle == IconStyle.Animated)
                {
                    _iconAnimationTimer.Start();
                }

                _isRunning = true;
                UpdateTrayIcon();
                UpdateTooltip();

                _trayIcon.ShowBalloonTip(2000, "🖥️ Hardware Monitor",
                    $"Started on {_configManager.Config.ComPort} [{_serialService.Mode}]", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                var icon = _iconManager.CreateStatusIcon(TrayIconManager.IconState.Error);
                UpdateIcon(icon);

                MessageBox.Show($"Failed to start:  {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopMonitoring()
        {
            _sendTimer.Stop();
            _iconAnimationTimer.Stop();
            _serialService.Disconnect();
            _isRunning = false;

            UpdateTrayIcon();
            _trayIcon.Text = "Hardware Monitor - Stopped";
        }

        private void OnStartStopClick(object sender, EventArgs e)
        {
            if (_isRunning)
                StopMonitoring();
            else
                StartMonitoring();
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            var wasRunning = _isRunning;
            if (wasRunning) StopMonitoring();

            using (var settingsForm = new SettingsForm(_configManager, _monitorService))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    _sendTimer.Interval = _configManager.Config.SendIntervalMs;
                    _serialService.Mode = _configManager.Config.ProtocolMode;

                    // Restart animation timer if needed
                    if (_configManager.Config.IconStyle == IconStyle.Animated && wasRunning)
                    {
                        _iconAnimationTimer.Start();
                    }
                }
            }

            if (wasRunning) StartMonitoring();
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            StopMonitoring();
            _monitorService.Dispose();
            _iconManager.Dispose();
            _trayIcon.Visible = false;
            Application.Exit();
        }
    }
}