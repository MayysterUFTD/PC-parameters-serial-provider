using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HardwareMonitorTray.Protocol;

namespace HardwareMonitorTray
{
    public class SettingsForm : Form
    {
        private ConfigManager _configManager;
        private HardwareMonitorService _monitorService;
        private bool _isInitialized = false;
        private bool _hasUnsavedChanges = false;

        // Connection settings
        private ComboBox _comPortCombo;
        private ComboBox _baudRateCombo;
        private NumericUpDown _intervalNumeric;
        private Button _refreshButton;

        // Protocol settings
        private ComboBox _protocolCombo;
        private Label _protocolInfoLabel;

        // Icon settings
        private ComboBox _iconStyleCombo;
        private PictureBox _iconPreview;
        private Label _iconInfoLabel;

        // Startup options
        private CheckBox _autoStartCheck;
        private CheckBox _startWithWindowsCheck;

        // Sensor selection
        private CheckedListBox _sensorsListBox;
        private TextBox _searchTextBox;
        private ComboBox _typeFilterCombo;
        private ComboBox _hardwareFilterCombo;
        private Button _clearSearchButton;
        private Button _selectAllButton;
        private Button _deselectAllButton;
        private Label _sensorCountLabel;

        // Packet info panel
        private Label _packetSizeLabel;
        private Label _bandwidthLabel;
        private Label _sensorsSelectedLabel;
        private ProgressBar _packetSizeBar;

        // Buttons
        private Button _saveButton;
        private Button _exitButton;

        // Icon preview
        private TrayIconManager _iconManager;

        public SettingsForm(ConfigManager configManager, HardwareMonitorService monitorService)
        {
            _configManager = configManager;
            _monitorService = monitorService;
            _iconManager = new TrayIconManager();

            InitializeComponents();
            LoadCurrentSettings();

            _isInitialized = true;
        }

        private void InitializeComponents()
        {
            this.Text = "Hardware Monitor Settings";
            this.Size = new Size(900, 700);
            this.MinimumSize = new Size(800, 600);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9f);

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                RowCount = 5,
                ColumnCount = 1
            };

            // Row styles - fixed heights for top sections
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));   // Connection
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));   // Appearance
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));   // Startup
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Sensors
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Buttons

            // === Connection Group ===
            var connectionGroup = CreateConnectionGroup();
            mainPanel.Controls.Add(connectionGroup, 0, 0);

            // === Appearance Group ===
            var appearanceGroup = CreateAppearanceGroup();
            mainPanel.Controls.Add(appearanceGroup, 0, 1);

            // === Startup Group ===
            var startupGroup = CreateStartupGroup();
            mainPanel.Controls.Add(startupGroup, 0, 2);

            // === Sensors Panel (with packet info) ===
            var sensorsPanel = CreateSensorsPanel();
            mainPanel.Controls.Add(sensorsPanel, 0, 3);

            // === Buttons ===
            var buttonsPanel = CreateButtonsPanel();
            mainPanel.Controls.Add(buttonsPanel, 0, 4);

            this.Controls.Add(mainPanel);
            this.FormClosing += OnFormClosing;
        }

        private GroupBox CreateConnectionGroup()
        {
            var group = new GroupBox
            {
                Text = "📡 Connection Settings",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 5, 10, 5)
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false
            };

            // COM Port
            var lblComPort = new Label
            {
                Text = "COM Port:",
                AutoSize = true,
                Margin = new Padding(0, 8, 5, 0)
            };
            panel.Controls.Add(lblComPort);

            _comPortCombo = new ComboBox
            {
                Width = 85,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 4, 0, 0)
            };
            _comPortCombo.SelectedIndexChanged += OnSettingChanged;
            panel.Controls.Add(_comPortCombo);

            _refreshButton = new Button
            {
                Text = "🔄",
                Width = 30,
                Height = 23,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(3, 4, 25, 0)
            };
            _refreshButton.Click += (s, e) => RefreshComPorts();
            panel.Controls.Add(_refreshButton);

            // Baud Rate
            var lblBaudRate = new Label
            {
                Text = "Baud Rate:",
                AutoSize = true,
                Margin = new Padding(0, 8, 5, 0)
            };
            panel.Controls.Add(lblBaudRate);

            _baudRateCombo = new ComboBox
            {
                Width = 85,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 4, 25, 0)
            };
            _baudRateCombo.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 });
            _baudRateCombo.SelectedIndexChanged += OnSettingChanged;
            panel.Controls.Add(_baudRateCombo);

            // Interval
            var lblInterval = new Label
            {
                Text = "Interval:",
                AutoSize = true,
                Margin = new Padding(0, 8, 5, 0)
            };
            panel.Controls.Add(lblInterval);

            _intervalNumeric = new NumericUpDown
            {
                Minimum = 100,
                Maximum = 60000,
                Width = 70,
                Increment = 100,
                Margin = new Padding(0, 4, 3, 0)
            };
            _intervalNumeric.ValueChanged += OnSettingChanged;
            panel.Controls.Add(_intervalNumeric);

            var lblMs = new Label
            {
                Text = "ms",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            panel.Controls.Add(lblMs);

            group.Controls.Add(panel);
            return group;
        }
        private GroupBox CreateAppearanceGroup()
        {
            var group = new GroupBox
            {
                Text = "🎨 Appearance",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 5, 10, 5)
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 2
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Row 1: Protocol
            panel.Controls.Add(new Label
            {
                Text = "Protocol:",
                AutoSize = true,
                Margin = new Padding(0, 8, 10, 0)
            }, 0, 0);

            _protocolCombo = new ComboBox
            {
                Width = 155,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 10, 0)
            };
            _protocolCombo.Items.AddRange(new object[]
            {
                "⚡ Binary (Fast)",
                "📝 Text (Debug)",
                "📋 JSON (Legacy)"
            });
            _protocolCombo.SelectedIndexChanged += OnProtocolChanged;
            panel.Controls.Add(_protocolCombo, 1, 0);

            _protocolInfoLabel = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(5, 8, 0, 0)
            };
            panel.Controls.Add(_protocolInfoLabel, 2, 0);
            panel.SetColumnSpan(_protocolInfoLabel, 3);

            // Row 2: Icon Style
            panel.Controls.Add(new Label
            {
                Text = "Tray Icon:",
                AutoSize = true,
                Margin = new Padding(0, 8, 10, 0)
            }, 0, 1);

            _iconStyleCombo = new ComboBox
            {
                Width = 155,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 10, 0)
            };
            _iconStyleCombo.Items.AddRange(new object[]
            {
                "🔴 Status (Color)",
                "🌡️ Temperature",
                "📊 Load Bars",
                "💻 Modern",
                "⚡ Animated"
            });
            _iconStyleCombo.SelectedIndexChanged += OnIconStyleChanged;
            panel.Controls.Add(_iconStyleCombo, 1, 1);

            // Icon Preview
            _iconPreview = new PictureBox
            {
                Width = 32,
                Height = 32,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(40, 40, 40),
                Margin = new Padding(5, 2, 10, 0)
            };
            panel.Controls.Add(_iconPreview, 2, 1);

            _iconInfoLabel = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(0, 10, 0, 0)
            };
            panel.Controls.Add(_iconInfoLabel, 3, 1);

            group.Controls.Add(panel);
            return group;
        }

        private GroupBox CreateStartupGroup()
        {
            var group = new GroupBox
            {
                Text = "🚀 Startup Options",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 3, 10, 3)
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            _autoStartCheck = new CheckBox
            {
                Text = "▶️ Auto-start monitoring on launch",
                AutoSize = true,
                Margin = new Padding(0, 5, 30, 0)
            };
            _autoStartCheck.CheckedChanged += OnSettingChanged;
            panel.Controls.Add(_autoStartCheck);

            _startWithWindowsCheck = new CheckBox
            {
                Text = "🪟 Start with Windows",
                AutoSize = true,
                Margin = new Padding(0, 5, 0, 0)
            };
            _startWithWindowsCheck.CheckedChanged += OnSettingChanged;
            panel.Controls.Add(_startWithWindowsCheck);

            group.Controls.Add(panel);
            return group;
        }

        private TableLayoutPanel CreateSensorsPanel()
        {
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };

            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

            // Left:  Sensors Group
            var sensorsGroup = CreateSensorsGroup();
            mainPanel.Controls.Add(sensorsGroup, 0, 0);

            // Right:  Packet Info Group
            var packetInfoGroup = CreatePacketInfoGroup();
            mainPanel.Controls.Add(packetInfoGroup, 1, 0);

            return mainPanel;
        }

        private GroupBox CreateSensorsGroup()
        {
            var group = new GroupBox
            {
                Text = "📊 Sensor Selection",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1
            };

            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // Search
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // Filters
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // Buttons
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // List

            // Search panel
            var searchPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Margin = new Padding(0)
            };
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            searchPanel.Controls.Add(new Label
            {
                Text = "🔍",
                AutoSize = true,
                Margin = new Padding(0, 5, 5, 0)
            }, 0, 0);

            _searchTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Search sensors...",
                Margin = new Padding(0, 2, 5, 0)
            };
            _searchTextBox.TextChanged += OnSearchChanged;
            searchPanel.Controls.Add(_searchTextBox, 1, 0);

            _clearSearchButton = new Button
            {
                Text = "✖",
                Width = 28,
                Height = 23,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 2, 0, 0)
            };
            _clearSearchButton.Click += (s, e) => ClearFilters();
            searchPanel.Controls.Add(_clearSearchButton, 2, 0);

            panel.Controls.Add(searchPanel, 0, 0);

            // Filter panel
            var filterPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            filterPanel.Controls.Add(new Label
            {
                Text = "Type:",
                AutoSize = true,
                Margin = new Padding(0, 6, 3, 0)
            });

            _typeFilterCombo = new ComboBox
            {
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 10, 0)
            };
            _typeFilterCombo.SelectedIndexChanged += OnFilterChanged;
            filterPanel.Controls.Add(_typeFilterCombo);

            filterPanel.Controls.Add(new Label
            {
                Text = "Hardware:",
                AutoSize = true,
                Margin = new Padding(0, 6, 3, 0)
            });

            _hardwareFilterCombo = new ComboBox
            {
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 0, 0)
            };
            _hardwareFilterCombo.SelectedIndexChanged += OnFilterChanged;
            filterPanel.Controls.Add(_hardwareFilterCombo);

            panel.Controls.Add(filterPanel, 0, 1);

            // Selection buttons
            var selectButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _selectAllButton = new Button
            {
                Text = "☑ Add All",
                AutoSize = true,
                Height = 25,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 2, 5, 0)
            };
            _selectAllButton.Click += (s, e) => AddVisibleSensors();
            selectButtonsPanel.Controls.Add(_selectAllButton);

            _deselectAllButton = new Button
            {
                Text = "☐ Remove All",
                AutoSize = true,
                Height = 25,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 2, 10, 0)
            };
            _deselectAllButton.Click += (s, e) => RemoveVisibleSensors();
            selectButtonsPanel.Controls.Add(_deselectAllButton);

            _sensorCountLabel = new Label
            {
                Text = "0 sensors",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(5, 7, 0, 0)
            };
            selectButtonsPanel.Controls.Add(_sensorCountLabel);

            panel.Controls.Add(selectButtonsPanel, 0, 2);

            // Sensors list
            _sensorsListBox = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                Margin = new Padding(0, 3, 0, 0)
            };
            _sensorsListBox.ItemCheck += OnSensorItemCheck;
            panel.Controls.Add(_sensorsListBox, 0, 3);

            group.Controls.Add(panel);
            return group;
        }

        private GroupBox CreatePacketInfoGroup()
        {
            var group = new GroupBox
            {
                Text = "📦 Packet Info",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 9,
                ColumnCount = 1
            };

            // Selected sensors label
            panel.Controls.Add(new Label
            {
                Text = "Selected Sensors:",
                AutoSize = true,
                Font = new Font(this.Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 0)
            }, 0, 0);

            _sensorsSelectedLabel = new Label
            {
                Text = "0",
                AutoSize = true,
                Font = new Font("Segoe UI", 28, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 152, 219),
                Margin = new Padding(0, 0, 0, 5)
            };
            panel.Controls.Add(_sensorsSelectedLabel, 0, 1);

            // Packet size
            panel.Controls.Add(new Label
            {
                Text = "Estimated Packet Size:",
                AutoSize = true,
                Font = new Font(this.Font, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 0)
            }, 0, 2);

            _packetSizeLabel = new Label
            {
                Text = "0 B",
                AutoSize = true,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 204, 113),
                Margin = new Padding(0, 0, 0, 3)
            };
            panel.Controls.Add(_packetSizeLabel, 0, 3);

            _packetSizeBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 15,
                Maximum = 1000,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 0, 0, 10)
            };
            panel.Controls.Add(_packetSizeBar, 0, 4);

            // Bandwidth
            panel.Controls.Add(new Label
            {
                Text = "Bandwidth Usage:",
                AutoSize = true,
                Font = new Font(this.Font, FontStyle.Bold),
                Margin = new Padding(0, 5, 0, 0)
            }, 0, 5);

            _bandwidthLabel = new Label
            {
                Text = "0 B/s",
                AutoSize = true,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(155, 89, 182),
                Margin = new Padding(0, 0, 0, 10)
            };
            panel.Controls.Add(_bandwidthLabel, 0, 6);

            // Separator
            var separator = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Height = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 5, 0, 10)
            };
            panel.Controls.Add(separator, 0, 7);

            // Protocol comparison
            var comparisonLabel = new Label
            {
                Text = "📊 Protocol Comparison:\n\n" +
                       "⚡ Binary:   ~5 B/sensor\n" +
                       "📝 Text:   ~12 B/sensor\n" +
                       "📋 JSON:   ~80 B/sensor",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(0, 0, 0, 0)
            };
            panel.Controls.Add(comparisonLabel, 0, 8);

            // Row styles
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            group.Controls.Add(panel);
            return group;
        }

        private FlowLayoutPanel CreateButtonsPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 5, 0, 0)
            };

            _exitButton = new Button
            {
                Text = "🚪 Exit",
                Width = 90,
                Height = 32,
                Margin = new Padding(5, 0, 0, 0)
            };
            _exitButton.Click += OnExitClick;
            panel.Controls.Add(_exitButton);

            _saveButton = new Button
            {
                Text = "💾 Save",
                Width = 90,
                Height = 32,
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(5, 0, 0, 0)
            };
            _saveButton.Click += OnSaveClick;
            panel.Controls.Add(_saveButton);

            return panel;
        }

        #region Settings Loading

        private void LoadCurrentSettings()
        {
            RefreshComPorts();

            if (!string.IsNullOrEmpty(_configManager.Config.ComPort))
            {
                _comPortCombo.SelectedItem = _configManager.Config.ComPort;
            }

            _baudRateCombo.SelectedItem = _configManager.Config.BaudRate;
            _intervalNumeric.Value = _configManager.Config.SendIntervalMs;

            _protocolCombo.SelectedIndex = _configManager.Config.ProtocolMode switch
            {
                ProtocolMode.Binary => 0,
                ProtocolMode.Text => 1,
                ProtocolMode.Json => 2,
                _ => 0
            };

            _iconStyleCombo.SelectedIndex = (int)_configManager.Config.IconStyle;

            _autoStartCheck.Checked = _configManager.Config.AutoStart;
            _startWithWindowsCheck.Checked = _configManager.Config.StartWithWindows;

            LoadFilters();
            LoadSensors();
            UpdateProtocolInfo();
            UpdateIconPreview();
            UpdatePacketInfo();

            _hasUnsavedChanges = false;
            UpdateTitle();
        }

        private void RefreshComPorts()
        {
            _comPortCombo.Items.Clear();
            var ports = SerialPortService.GetAvailablePorts();
            _comPortCombo.Items.AddRange(ports);

            if (ports.Length > 0)
            {
                _comPortCombo.SelectedIndex = 0;
            }
        }

        private void LoadFilters()
        {
            _typeFilterCombo.SelectedIndexChanged -= OnFilterChanged;
            _hardwareFilterCombo.SelectedIndexChanged -= OnFilterChanged;

            _typeFilterCombo.Items.Clear();
            _typeFilterCombo.Items.Add("All Types");
            foreach (var type in _monitorService.GetAvailableSensorTypes())
            {
                _typeFilterCombo.Items.Add(type);
            }
            _typeFilterCombo.SelectedIndex = 0;

            _hardwareFilterCombo.Items.Clear();
            _hardwareFilterCombo.Items.Add("All Hardware");
            foreach (var hardware in _monitorService.GetAvailableHardware())
            {
                _hardwareFilterCombo.Items.Add(hardware);
            }
            _hardwareFilterCombo.SelectedIndex = 0;

            _typeFilterCombo.SelectedIndexChanged += OnFilterChanged;
            _hardwareFilterCombo.SelectedIndexChanged += OnFilterChanged;
        }

        private void LoadSensors()
        {
            _sensorsListBox.ItemCheck -= OnSensorItemCheck;
            _sensorsListBox.Items.Clear();

            var searchQuery = _searchTextBox.Text;

            string typeFilter = _typeFilterCombo.SelectedIndex > 0
                ? _typeFilterCombo.SelectedItem.ToString()
                : null;

            string hardwareFilter = _hardwareFilterCombo.SelectedIndex > 0
                ? _hardwareFilterCombo.SelectedItem.ToString()
                : null;

            var sensors = _monitorService.SearchSensors(searchQuery);

            if (!string.IsNullOrEmpty(typeFilter))
            {
                sensors = sensors.Where(s => s.Type == typeFilter).ToList();
            }

            if (!string.IsNullOrEmpty(hardwareFilter))
            {
                sensors = sensors.Where(s => s.Hardware == hardwareFilter).ToList();
            }

            foreach (var sensor in sensors)
            {
                var displayText = $"[{sensor.Hardware}] {sensor.Name}";
                var isChecked = _configManager.Config.SelectedSensors.Contains(sensor.Id);
                _sensorsListBox.Items.Add(new SensorListItem(sensor.Id, displayText), isChecked);
            }

            _sensorsListBox.ItemCheck += OnSensorItemCheck;
            UpdateSensorCount();
        }

        #endregion

        #region UI Updates

        private void UpdateSensorCount()
        {
            var visible = _sensorsListBox.Items.Count;
            var checkedVisible = _sensorsListBox.CheckedItems.Count;
            var totalSelected = GetTotalSelectedCount();

            _sensorCountLabel.Text = $"✓ {checkedVisible}/{visible} visible • {totalSelected} total";
        }

        private void UpdatePacketInfo()
        {
            int sensorCount = GetTotalSelectedCount();
            var protocol = GetSelectedProtocol();

            _sensorsSelectedLabel.Text = sensorCount.ToString();

            int packetSize;
            switch (protocol)
            {
                case ProtocolMode.Binary:
                    packetSize = 6 + (sensorCount * 5);
                    _packetSizeLabel.ForeColor = Color.FromArgb(46, 204, 113);
                    break;
                case ProtocolMode.Text:
                    packetSize = 12 + (sensorCount * 12);
                    _packetSizeLabel.ForeColor = Color.FromArgb(241, 196, 15);
                    break;
                default:
                    packetSize = 50 + (sensorCount * 85);
                    _packetSizeLabel.ForeColor = Color.FromArgb(231, 76, 60);
                    break;
            }

            _packetSizeLabel.Text = FormatBytes(packetSize);
            _packetSizeBar.Value = Math.Min(packetSize, 1000);

            int interval = (int)_intervalNumeric.Value;
            double packetsPerSecond = 1000.0 / interval;
            double bytesPerSecond = packetSize * packetsPerSecond;

            _bandwidthLabel.Text = $"{FormatBytes((int)bytesPerSecond)}/s ({packetsPerSecond.ToString("0.0")} pkt/s)";

            if (bytesPerSecond < 1000)
                _bandwidthLabel.ForeColor = Color.FromArgb(46, 204, 113);
            else if (bytesPerSecond < 10000)
                _bandwidthLabel.ForeColor = Color.FromArgb(241, 196, 15);
            else
                _bandwidthLabel.ForeColor = Color.FromArgb(231, 76, 60);
        }

        private string FormatBytes(int bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{(bytes / 1024.0).ToString("0.0")} KB";
            else
                return $"{(bytes / (1024.0 * 1024.0)).ToString("0.00")} MB";
        }

        private int GetTotalSelectedCount()
        {
            var currentlyVisible = _sensorsListBox.Items.Cast<SensorListItem>().ToList();
            var checkedInView = _sensorsListBox.CheckedItems.Cast<SensorListItem>().Select(x => x.Id).ToList();

            var totalSelected = _configManager.Config.SelectedSensors
                .Where(id => !currentlyVisible.Any(v => v.Id == id))
                .ToList();

            totalSelected.AddRange(checkedInView);
            return totalSelected.Distinct().Count();
        }

        private ProtocolMode GetSelectedProtocol()
        {
            return _protocolCombo.SelectedIndex switch
            {
                0 => ProtocolMode.Binary,
                1 => ProtocolMode.Text,
                2 => ProtocolMode.Json,
                _ => ProtocolMode.Binary
            };
        }

        private void UpdateProtocolInfo()
        {
            _protocolInfoLabel.Text = _protocolCombo.SelectedIndex switch
            {
                0 => "~5 B/sensor • CRC16",
                1 => "Human readable",
                2 => "Full JSON • High BW",
                _ => ""
            };
        }

        private void UpdateIconPreview()
        {
            var style = (IconStyle)_iconStyleCombo.SelectedIndex;
            Icon previewIcon;
            string info;

            switch (style)
            {
                case IconStyle.Status:
                    previewIcon = _iconManager.CreateStatusIcon(TrayIconManager.IconState.Running);
                    info = "Color = temp";
                    break;
                case IconStyle.Temperature:
                    previewIcon = _iconManager.CreateTemperatureIcon(45);
                    info = "Shows °C";
                    break;
                case IconStyle.LoadBars:
                    previewIcon = _iconManager.CreateLoadIcon(65, 40);
                    info = "CPU/GPU bars";
                    break;
                case IconStyle.Modern:
                    previewIcon = _iconManager.CreateModernIcon(true, 45);
                    info = "Gradient chip";
                    break;
                case IconStyle.Animated:
                    previewIcon = _iconManager.CreatePulseIcon(0);
                    info = "Pulse dots";
                    break;
                default:
                    previewIcon = _iconManager.CreateStatusIcon(TrayIconManager.IconState.Running);
                    info = "";
                    break;
            }

            _iconPreview.Image = previewIcon.ToBitmap();
            _iconInfoLabel.Text = info;
        }

        private void UpdateTitle()
        {
            this.Text = _hasUnsavedChanges
                ? "Hardware Monitor Settings *"
                : "Hardware Monitor Settings";
        }

        #endregion

        #region Event Handlers

        private void OnSettingChanged(object sender, EventArgs e)
        {
            if (!_isInitialized) return;
            MarkAsChanged();
            UpdatePacketInfo();
        }

        private void OnProtocolChanged(object sender, EventArgs e)
        {
            if (!_isInitialized) return;
            UpdateProtocolInfo();
            UpdatePacketInfo();
            MarkAsChanged();
        }

        private void OnIconStyleChanged(object sender, EventArgs e)
        {
            if (!_isInitialized) return;
            UpdateIconPreview();
            MarkAsChanged();
        }

        private void OnSearchChanged(object sender, EventArgs e)
        {
            if (!_isInitialized) return;
            LoadSensors();
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            if (!_isInitialized) return;
            LoadSensors();
        }

        private void OnSensorItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (!_isInitialized) return;

            var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                timer.Dispose();
                UpdateSensorCount();
                UpdatePacketInfo();
                MarkAsChanged();
            };
            timer.Start();
        }

        private void ClearFilters()
        {
            _isInitialized = false;
            _searchTextBox.Text = "";
            _typeFilterCombo.SelectedIndex = 0;
            _hardwareFilterCombo.SelectedIndex = 0;
            _isInitialized = true;
            LoadSensors();
        }

        private void AddVisibleSensors()
        {
            _sensorsListBox.ItemCheck -= OnSensorItemCheck;
            for (int i = 0; i < _sensorsListBox.Items.Count; i++)
            {
                _sensorsListBox.SetItemChecked(i, true);
            }
            _sensorsListBox.ItemCheck += OnSensorItemCheck;
            UpdateSensorCount();
            UpdatePacketInfo();
            MarkAsChanged();
        }

        private void RemoveVisibleSensors()
        {
            _sensorsListBox.ItemCheck -= OnSensorItemCheck;
            for (int i = 0; i < _sensorsListBox.Items.Count; i++)
            {
                _sensorsListBox.SetItemChecked(i, false);
            }
            _sensorsListBox.ItemCheck += OnSensorItemCheck;
            UpdateSensorCount();
            UpdatePacketInfo();
            MarkAsChanged();
        }

        private void MarkAsChanged()
        {
            _hasUnsavedChanges = true;
            UpdateTitle();
        }

        #endregion

        #region Save/Exit

        private void OnSaveClick(object sender, EventArgs e)
        {
            SaveSettings();
            _hasUnsavedChanges = false;
            UpdateTitle();

            _saveButton.Text = "✓ Saved! ";
            _saveButton.BackColor = Color.FromArgb(39, 174, 96);

            var timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                timer.Dispose();
                _saveButton.Text = "💾 Save";
                _saveButton.BackColor = Color.FromArgb(46, 204, 113);
            };
            timer.Start();
        }

        private void SaveSettings()
        {
            _configManager.Config.ComPort = _comPortCombo.SelectedItem?.ToString() ?? "";
            _configManager.Config.BaudRate = (int)(_baudRateCombo.SelectedItem ?? 115200);
            _configManager.Config.SendIntervalMs = (int)_intervalNumeric.Value;
            _configManager.Config.ProtocolMode = GetSelectedProtocol();
            _configManager.Config.IconStyle = (IconStyle)_iconStyleCombo.SelectedIndex;
            _configManager.Config.AutoStart = _autoStartCheck.Checked;
            _configManager.Config.StartWithWindows = _startWithWindowsCheck.Checked;

            // Collect selected sensors
            var currentlyVisible = _sensorsListBox.Items.Cast<SensorListItem>().ToList();
            var checkedInView = _sensorsListBox.CheckedItems.Cast<SensorListItem>().Select(x => x.Id).ToList();
            var uncheckedInView = currentlyVisible.Where(x => !checkedInView.Contains(x.Id)).Select(x => x.Id).ToList();

            var selectedIds = _configManager.Config.SelectedSensors
                .Where(id => !currentlyVisible.Any(v => v.Id == id))
                .ToList();

            selectedIds.AddRange(checkedInView);
            _configManager.Config.SelectedSensors = selectedIds.Distinct().ToList();
            _configManager.SaveConfig();
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes.  Save before exiting?",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    SaveSettings();
                    this.DialogResult = DialogResult.OK;
                }
                else if (result == DialogResult.No)
                {
                    this.DialogResult = DialogResult.Cancel;
                }
                else
                {
                    return;
                }
            }
            else
            {
                this.DialogResult = DialogResult.OK;
            }
            this.Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before closing?",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    SaveSettings();
                    this.DialogResult = DialogResult.OK;
                }
                else if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }
            _iconManager?.Dispose();
        }

        #endregion

        private class SensorListItem
        {
            public string Id { get; }
            public string DisplayText { get; }

            public SensorListItem(string id, string displayText)
            {
                Id = id;
                DisplayText = displayText;
            }

            public override string ToString() => DisplayText;
        }
    }

    public enum IconStyle
    {
        Status = 0,
        Temperature = 1,
        LoadBars = 2,
        Modern = 3,
        Animated = 4
    }
}