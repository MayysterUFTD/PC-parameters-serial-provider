using System;
using System.Windows.Forms;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareMonitorTray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _contextMenu;
        private HardwareMonitorService _monitorService;
        private SerialPortService _serialService;
        private ConfigManager _configManager;
        private System.Windows.Forms.Timer _sendTimer;
        private bool _isRunning = false;
        private JsonSerializerOptions _jsonOptions;

        public TrayApplicationContext()
        {
            _configManager = new ConfigManager();
            _monitorService = new HardwareMonitorService();
            _serialService = new SerialPortService();

            _jsonOptions = new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                WriteIndented = false
            };

            InitializeTrayIcon();
            InitializeTimer();

            if (_configManager.Config.AutoStart)
            {
                StartMonitoring();
            }
        }

        private void InitializeTrayIcon()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add("Start/Stop", null, OnStartStopClick);
            _contextMenu.Items.Add("Settings", null, OnSettingsClick);
            _contextMenu.Items.Add("-");
            _contextMenu.Items.Add("Exit", null, OnExitClick);

            _trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = _contextMenu,
                Visible = true,
                Text = "Hardware Monitor - Stopped"
            };

            _trayIcon.DoubleClick += OnSettingsClick;
        }

        private void InitializeTimer()
        {
            _sendTimer = new System.Windows.Forms.Timer();
            _sendTimer.Interval = _configManager.Config.SendIntervalMs;
            _sendTimer.Tick += OnTimerTick;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                var data = _monitorService.GetSelectedSensorData(_configManager.Config.SelectedSensors);
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                _serialService.SendData(json);
            }
            catch (Exception ex)
            {
                _trayIcon.ShowBalloonTip(3000, "Error", ex.Message, ToolTipIcon.Error);
            }
        }

        private void StartMonitoring()
        {
            try
            {
                _serialService.Connect(_configManager.Config.ComPort, _configManager.Config.BaudRate);
                _sendTimer.Start();
                _isRunning = true;
                _trayIcon.Text = $"Hardware Monitor - Active ({_configManager.Config.ComPort})";
                _trayIcon.Icon = SystemIcons.Shield;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start:  {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopMonitoring()
        {
            _sendTimer.Stop();
            _serialService.Disconnect();
            _isRunning = false;
            _trayIcon.Text = "Hardware Monitor - Stopped";
            _trayIcon.Icon = SystemIcons.Application;
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
                }
            }

            if (wasRunning) StartMonitoring();
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            StopMonitoring();
            _monitorService.Dispose();
            _trayIcon.Visible = false;
            Application.Exit();
        }
    }
}