using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;
using HardwareMonitorTray.Protocol;

namespace HardwareMonitorTray
{
    public class SettingsForm : Form
    {
        private readonly ConfigManager _cfg;
        private readonly HardwareMonitorService _mon;
        private readonly Action<bool> _onToggleMonitoring;
        private readonly Func<bool> _isMonitoringRunning;

        private volatile bool _init = false;
        private volatile bool _dirty = false;
        private volatile bool _closing = false;
        private volatile bool _refreshing = false;

        private List<SensorInfo> _sensors = new List<SensorInfo>();
        private readonly object _lockObj = new object();

        private ComboBox _portCb, _baudCb, _protoCb, _iconCb, _typeCb, _hwCb;
        private NumericUpDown _intervalNum;
        private CheckBox _autoChk, _winChk;
        private ListView _list;
        private TextBox _searchBox, _previewBox;
        private Label _cntLbl, _selLbl, _sizeLbl, _bwLbl, _loadLbl, _statusLbl;
        private ProgressBar _sizeBar;
        private PictureBox _iconPic;
        private Button _saveBtn, _startStopBtn;
        private TrayIconManager _iconMgr;
        private System.Windows.Forms.Timer _uiTimer;

        private Dictionary<string, byte> _sensorIdMap = new Dictionary<string, byte>();
        private byte _nextCustomId = 0x80;

        public SettingsForm(ConfigManager cfg, HardwareMonitorService mon,
            Action<bool> onToggleMonitoring, Func<bool> isMonitoringRunning)
        {
            _cfg = cfg;
            _mon = mon;
            _onToggleMonitoring = onToggleMonitoring;
            _isMonitoringRunning = isMonitoringRunning;
            _iconMgr = new TrayIconManager();

            BuildUI();
            LoadSettings();

            _mon.OnDataReady += OnSensorData;

            // Wolniejszy timer = mniej blokowania
            _uiTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _uiTimer.Tick += OnUiTimerTick;

            Shown += OnFormShown;
        }

        private void OnFormShown(object s, EventArgs e)
        {
            if (_mon.HasData)
            {
                _sensors = _mon.GetSensors();
                InitSensorList();
            }
            _uiTimer.Start();
            UpdateStartStopButton();
            UpdateStatusLabel();
        }

        private void OnUiTimerTick(object s, EventArgs e)
        {
            if (_closing || _refreshing || !_init) return;

            _uiTimer.Stop();
            try
            {
                RefreshValuesAsync();
                UpdateStatusLabel();
            }
            finally
            {
                if (!_closing) _uiTimer.Start();
            }
        }

        private void OnSensorData(List<SensorInfo> data)
        {
            if (_closing) return;

            lock (_lockObj)
            {
                _sensors = new List<SensorInfo>(data);
            }

            if (!_init)
            {
                try
                {
                    BeginInvoke((Action)InitSensorList);
                }
                catch { }
            }
        }

        private void InitSensorList()
        {
            if (_init || _closing) return;

            BuildSensorIdMap();
            PopulateFilters();
            FilterList();
            _loadLbl.Visible = false;
            _list.Visible = true;
            _init = true;
        }

        private void BuildSensorIdMap()
        {
            _sensorIdMap.Clear();
            _nextCustomId = 0x80;
            var counters = new Dictionary<string, int>();

            List<SensorInfo> sensors;
            lock (_lockObj)
            {
                sensors = new List<SensorInfo>(_sensors);
            }

            foreach (var sensor in sensors)
            {
                var baseId = GetBaseIdForSensor(sensor);
                if (baseId != 0xFF)
                {
                    var key = $"{GetCategory(sensor)}_{sensor.Type}_{baseId:X2}";
                    if (!counters.ContainsKey(key)) counters[key] = 0;
                    var offset = counters[key]++;
                    _sensorIdMap[sensor.Id] = offset == 0 ? baseId : _nextCustomId++;
                }
                else
                {
                    _sensorIdMap[sensor.Id] = _nextCustomId++;
                }
            }
        }

        private string GetCategory(SensorInfo s)
        {
            var hw = s.Hardware.ToLower();
            if (hw.Contains("cpu") || hw.Contains("ryzen") || hw.Contains("intel")) return "CPU";
            if (hw.Contains("gpu") || hw.Contains("nvidia") || hw.Contains("radeon")) return "GPU";
            if (hw.Contains("memory")) return "RAM";
            if (hw.Contains("ssd") || hw.Contains("nvme") || hw.Contains("hdd")) return "DISK";
            return "SYS";
        }

        private byte GetBaseIdForSensor(SensorInfo s)
        {
            var hw = s.Hardware.ToLower();
            var nm = s.Name.ToLower();
            var tp = s.Type.ToLower();

            if (hw.Contains("cpu") || hw.Contains("ryzen") || hw.Contains("intel"))
            {
                if (tp == "temperature")
                {
                    if (nm.Contains("package") || nm.Contains("tctl") || nm.Contains("tdie")) return 0x01;
                    if (nm.Contains("ccd")) return 0x08;
                    return 0x05;
                }
                if (tp == "load") return nm.Contains("total") ? (byte)0x02 : (byte)0x06;
                if (tp == "clock") return 0x03;
                if (tp == "power") return nm.Contains("package") ? (byte)0x04 : (byte)0x07;
            }

            if (hw.Contains("gpu") || hw.Contains("nvidia") || hw.Contains("radeon"))
            {
                if (tp == "temperature")
                {
                    if (nm.Contains("hot")) return 0x18;
                    if (nm.Contains("mem")) return 0x17;
                    return 0x10;
                }
                if (tp == "load") return nm.Contains("mem") ? (byte)0x15 : (byte)0x11;
                if (tp == "clock") return nm.Contains("mem") ? (byte)0x13 : (byte)0x12;
                if (tp == "power") return 0x14;
                if (tp == "fan") return 0x16;
            }

            if (hw.Contains("memory"))
            {
                if (tp == "load") return 0x22;
                if (tp == "data") return 0x20;
            }

            if (hw.Contains("ssd") || hw.Contains("nvme")) return tp == "temperature" ? (byte)0x30 : (byte)0x31;

            return 0xFF;
        }

        private byte GetSensorId(SensorInfo s) =>
            _sensorIdMap.TryGetValue(s.Id, out var id) ? id : (byte)0xFF;

        private void BuildUI()
        {
            Text = "Hardware Monitor - Settings";
            Size = new Size(1100, 820);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);
            Icon = CreateAppIcon();
            BackColor = Color.FromArgb(250, 250, 252);

            // Main layout - use TableLayoutPanel for stable layout
            var mainTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(10)
            };
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // Status
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // Connection
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));  // Appearance
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Startup
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));  // Buttons

            mainTable.Controls.Add(BuildStatusGroup(), 0, 0);
            mainTable.Controls.Add(BuildConnectionGroup(), 0, 1);
            mainTable.Controls.Add(BuildAppearanceGroup(), 0, 2);
            mainTable.Controls.Add(BuildStartupGroup(), 0, 3);
            mainTable.Controls.Add(BuildContentPanel(), 0, 4);
            mainTable.Controls.Add(BuildButtonsPanel(), 0, 5);

            Controls.Add(mainTable);

            FormClosing += OnFormClosing;
        }

        private GroupBox BuildStatusGroup()
        {
            var grp = new GroupBox { Text = "📊 Status", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 5, 8, 0) };

            _startStopBtn = new Button
            {
                Text = "▶ Start",
                Width = 120,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _startStopBtn.FlatAppearance.BorderSize = 0;
            _startStopBtn.Click += OnStartStopClick;
            flow.Controls.Add(_startStopBtn);

            _statusLbl = new Label
            {
                Text = "● Stopped",
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.Gray,
                Margin = new Padding(25, 9, 0, 0)
            };
            flow.Controls.Add(_statusLbl);

            grp.Controls.Add(flow);
            return grp;
        }

        private GroupBox BuildConnectionGroup()
        {
            var grp = new GroupBox { Text = "📡 Connection", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 5, 8, 0) };

            flow.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(0, 8, 5, 0) });
            _portCb = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 5, 0) };
            _portCb.SelectedIndexChanged += OnSettingChanged;
            flow.Controls.Add(_portCb);

            var refreshBtn = new Button { Text = "🔄", Width = 35, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 20, 0) };
            refreshBtn.FlatAppearance.BorderSize = 0;
            refreshBtn.Click += (s, e) => RefreshPorts();
            flow.Controls.Add(refreshBtn);

            flow.Controls.Add(new Label { Text = "Baud:", AutoSize = true, Margin = new Padding(0, 8, 5, 0) });
            _baudCb = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 20, 0) };
            _baudCb.Items.AddRange(new object[] { 9600, 57600, 115200, 230400, 460800 });
            _baudCb.SelectedIndexChanged += OnSettingChanged;
            flow.Controls.Add(_baudCb);

            flow.Controls.Add(new Label { Text = "Interval:", AutoSize = true, Margin = new Padding(0, 8, 5, 0) });
            _intervalNum = new NumericUpDown { Width = 85, Minimum = 50, Maximum = 60000, Increment = 50, Margin = new Padding(0, 4, 3, 0) };
            _intervalNum.ValueChanged += OnSettingChanged;
            flow.Controls.Add(_intervalNum);
            flow.Controls.Add(new Label { Text = "ms", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });

            grp.Controls.Add(flow);
            return grp;
        }

        private GroupBox BuildAppearanceGroup()
        {
            var grp = new GroupBox { Text = "🎨 Appearance", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };
            var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(8, 5, 8, 0) };

            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            tbl.Controls.Add(new Label { Text = "Protocol:", AutoSize = true, Margin = new Padding(0, 8, 5, 0) }, 0, 0);
            _protoCb = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 0, 0) };
            _protoCb.Items.AddRange(new[] { "⚡ Binary (5 B/sensor)", "📝 Text (12 B/sensor)", "📋 JSON (80 B/sensor)" });
            _protoCb.SelectedIndexChanged += OnSettingChanged;
            tbl.Controls.Add(_protoCb, 1, 0);

            tbl.Controls.Add(new Label { Text = "Tray Icon:", AutoSize = true, Margin = new Padding(0, 8, 5, 0) }, 0, 1);
            _iconCb = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 0, 0) };
            _iconCb.Items.AddRange(new[] { "🔴 Status Color", "🌡️ Temperature", "📊 Load Bars", "💻 Modern Chip", "⚡ Animated" });
            _iconCb.SelectedIndexChanged += (s, e) => { UpdateIconPreview(); OnSettingChanged(s, e); };
            tbl.Controls.Add(_iconCb, 1, 1);

            _iconPic = new PictureBox { Width = 40, Height = 40, BackColor = Color.FromArgb(44, 62, 80), Margin = new Padding(15, 4, 0, 0), BorderStyle = BorderStyle.FixedSingle };
            tbl.Controls.Add(_iconPic, 2, 1);

            grp.Controls.Add(tbl);
            return grp;
        }

        private GroupBox BuildStartupGroup()
        {
            var grp = new GroupBox { Text = "🚀 Startup", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 5, 8, 0) };

            _autoChk = new CheckBox { Text = "Auto-start monitoring on launch", AutoSize = true, Margin = new Padding(0, 5, 50, 0) };
            _autoChk.CheckedChanged += OnSettingChanged;
            flow.Controls.Add(_autoChk);

            _winChk = new CheckBox { Text = "Start with Windows", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
            _winChk.CheckedChanged += OnSettingChanged;
            flow.Controls.Add(_winChk);

            grp.Controls.Add(flow);
            return grp;
        }

        private Panel BuildContentPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };

            var rightPanel = new Panel { Dock = DockStyle.Right, Width = 330, Padding = new Padding(10, 0, 0, 0) };
            BuildInfoPanel(rightPanel);

            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 10, 0) };
            BuildSensorsPanel(leftPanel);

            panel.Controls.Add(leftPanel);
            panel.Controls.Add(rightPanel);

            return panel;
        }

        private void BuildSensorsPanel(Panel parent)
        {
            var grp = new GroupBox { Text = "📊 Sensors", Dock = DockStyle.Fill };
            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            // Search
            var searchPanel = new Panel { Dock = DockStyle.Top, Height = 35 };
            _searchBox = new TextBox { Width = 300, Location = new Point(0, 5) };
            _searchBox.TextChanged += (s, e) => { if (_init) FilterList(); };
            searchPanel.Controls.Add(_searchBox);

            var clrBtn = new Button { Text = "✕", Width = 30, Height = 26, Location = new Point(305, 4), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(231, 76, 60), ForeColor = Color.White };
            clrBtn.FlatAppearance.BorderSize = 0;
            clrBtn.Click += (s, e) => _searchBox.Text = "";
            searchPanel.Controls.Add(clrBtn);

            // Filters
            var filterPanel = new Panel { Dock = DockStyle.Top, Height = 35 };
            filterPanel.Controls.Add(new Label { Text = "Type:", AutoSize = true, Location = new Point(0, 9) });
            _typeCb = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(45, 5) };
            _typeCb.SelectedIndexChanged += (s, e) => { if (_init) FilterList(); };
            filterPanel.Controls.Add(_typeCb);

            filterPanel.Controls.Add(new Label { Text = "Hardware:", AutoSize = true, Location = new Point(180, 9) });
            _hwCb = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(250, 5) };
            _hwCb.SelectedIndexChanged += (s, e) => { if (_init) FilterList(); };
            filterPanel.Controls.Add(_hwCb);

            // Buttons
            var btnPanel = new Panel { Dock = DockStyle.Top, Height = 38 };

            var allBtn = new Button { Text = "☑ All", Width = 60, Height = 28, Location = new Point(0, 5), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 204, 113), ForeColor = Color.White };
            allBtn.FlatAppearance.BorderSize = 0;
            allBtn.Click += (s, e) => SetAll(true);
            btnPanel.Controls.Add(allBtn);

            var noneBtn = new Button { Text = "☐ None", Width = 70, Height = 28, Location = new Point(65, 5), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(231, 76, 60), ForeColor = Color.White };
            noneBtn.FlatAppearance.BorderSize = 0;
            noneBtn.Click += (s, e) => SetAll(false);
            btnPanel.Controls.Add(noneBtn);

            var hdrBtn = new Button { Text = "📄 . h", Width = 55, Height = 28, Location = new Point(140, 5), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White };
            hdrBtn.FlatAppearance.BorderSize = 0;
            hdrBtn.Click += GenerateHeaderFiles;
            btnPanel.Controls.Add(hdrBtn);

            _cntLbl = new Label { AutoSize = true, Location = new Point(205, 10), ForeColor = Color.Gray };
            btnPanel.Controls.Add(_cntLbl);

            // ListView
            var listPanel = new Panel { Dock = DockStyle.Fill };
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                GridLines = true,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            _list.Columns.Add("", 30);
            _list.Columns.Add("Hardware", 180);
            _list.Columns.Add("Name", 150);
            _list.Columns.Add("Type", 90);
            _list.Columns.Add("Value", 85);
            _list.Columns.Add("ID", 60);
            _list.ItemChecked += OnItemChecked;
            EnableDoubleBuffer(_list);

            _loadLbl = new Label
            {
                Text = "⏳ Loading sensors...",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 12)
            };
            listPanel.Controls.Add(_list);
            listPanel.Controls.Add(_loadLbl);

            mainPanel.Controls.Add(listPanel);
            mainPanel.Controls.Add(btnPanel);
            mainPanel.Controls.Add(filterPanel);
            mainPanel.Controls.Add(searchPanel);

            grp.Controls.Add(mainPanel);
            parent.Controls.Add(grp);
        }

        private void BuildInfoPanel(Panel parent)
        {
            var grp = new GroupBox { Text = "📦 Packet Info", Dock = DockStyle.Fill };
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            int y = 0;

            panel.Controls.Add(new Label { Text = "Selected Sensors:", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 22;

            _selLbl = new Label { Text = "0", Font = new Font("Segoe UI", 32, FontStyle.Bold), ForeColor = Color.FromArgb(52, 152, 219), AutoSize = true, Location = new Point(0, y) };
            panel.Controls.Add(_selLbl);
            y += 60;

            panel.Controls.Add(new Label { Text = "Packet Size:", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 22;

            _sizeLbl = new Label { Text = "0 B", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Color.FromArgb(46, 204, 113), AutoSize = true, Location = new Point(0, y) };
            panel.Controls.Add(_sizeLbl);
            y += 35;

            _sizeBar = new ProgressBar { Width = 270, Height = 14, Maximum = 500, Location = new Point(0, y) };
            panel.Controls.Add(_sizeBar);
            y += 24;

            _bwLbl = new Label { Text = "0 B/s", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(155, 89, 182), AutoSize = true, Location = new Point(0, y) };
            panel.Controls.Add(_bwLbl);
            y += 40;

            panel.Controls.Add(new Label { Text = "Preview:", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 24;

            _previewBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = Color.FromArgb(46, 204, 113),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(0, y),
                Size = new Size(270, 200)
            };
            panel.Controls.Add(_previewBox);

            panel.Resize += (s, e) =>
            {
                _previewBox.Width = Math.Max(150, panel.Width - 30);
                _previewBox.Height = Math.Max(100, panel.Height - y - 20);
                _sizeBar.Width = Math.Max(150, panel.Width - 30);
            };

            grp.Controls.Add(panel);
            parent.Controls.Add(grp);
        }

        private Panel BuildButtonsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };

            var exitBtn = new Button { Text = "🚪 Exit", Width = 100, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(149, 165, 166), ForeColor = Color.White };
            exitBtn.FlatAppearance.BorderSize = 0;
            exitBtn.Click += (s, e) => { if (ConfirmExit()) Close(); };
            flow.Controls.Add(exitBtn);

            _saveBtn = new Button { Text = "💾 Save", Width = 100, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White };
            _saveBtn.FlatAppearance.BorderSize = 0;
            _saveBtn.Click += Save;
            flow.Controls.Add(_saveBtn);

            panel.Controls.Add(flow);
            return panel;
        }

        private void OnFormClosing(object s, FormClosingEventArgs e)
        {
            _closing = true;
            _uiTimer?.Stop();
            _uiTimer?.Dispose();
            _mon.OnDataReady -= OnSensorData;
            _iconMgr?.Dispose();
        }

        private Icon CreateAppIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(52, 152, 219), Color.FromArgb(41, 128, 185), 45f);
            g.FillEllipse(brush, 2, 2, 28, 28);
            g.FillRectangle(Brushes.White, 10, 10, 5, 5);
            g.FillRectangle(Brushes.White, 17, 10, 5, 5);
            g.FillRectangle(Brushes.White, 10, 17, 5, 5);
            g.FillRectangle(Brushes.White, 17, 17, 5, 5);
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void EnableDoubleBuffer(ListView lv) =>
            typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(lv, true);

        private void OnStartStopClick(object sender, EventArgs e)
        {
            if (_dirty) Save(null, null);
            _onToggleMonitoring(!_isMonitoringRunning());
            UpdateStartStopButton();
            UpdateStatusLabel();
        }

        private void UpdateStartStopButton()
        {
            bool running = _isMonitoringRunning();
            _startStopBtn.Text = running ? "⏹ Stop" : "▶ Start";
            _startStopBtn.BackColor = running ? Color.FromArgb(231, 76, 60) : Color.FromArgb(46, 204, 113);
        }

        private void UpdateStatusLabel()
        {
            bool running = _isMonitoringRunning();
            if (running)
            {
                _statusLbl.Text = $"● Running on {_cfg.Config.ComPort} @ {_cfg.Config.BaudRate} ({_cfg.Config.ProtocolMode})";
                _statusLbl.ForeColor = Color.FromArgb(46, 204, 113);
            }
            else
            {
                _statusLbl.Text = "● Stopped";
                _statusLbl.ForeColor = Color.Gray;
            }
        }

        private void LoadSettings()
        {
            RefreshPorts();
            if (!string.IsNullOrEmpty(_cfg.Config.ComPort)) _portCb.SelectedItem = _cfg.Config.ComPort;
            _baudCb.SelectedItem = _cfg.Config.BaudRate;
            if (_baudCb.SelectedIndex < 0) _baudCb.SelectedIndex = 2;
            _intervalNum.Value = Math.Clamp(_cfg.Config.SendIntervalMs, 50, 60000);
            _protoCb.SelectedIndex = Math.Max(0, (int)_cfg.Config.ProtocolMode);
            _iconCb.SelectedIndex = Math.Max(0, (int)_cfg.Config.IconStyle);
            _autoChk.Checked = _cfg.Config.AutoStart;
            _winChk.Checked = _cfg.Config.StartWithWindows;
            UpdateIconPreview();
            _dirty = false;
            UpdateTitle();
        }

        private void RefreshPorts()
        {
            _portCb.Items.Clear();
            _portCb.Items.AddRange(SerialPortService.GetAvailablePorts());
            if (_portCb.Items.Count > 0) _portCb.SelectedIndex = 0;
        }

        private void PopulateFilters()
        {
            List<SensorInfo> sensors;
            lock (_lockObj) { sensors = new List<SensorInfo>(_sensors); }

            _typeCb.Items.Clear();
            _typeCb.Items.Add("All");
            foreach (var t in sensors.Select(s => s.Type).Distinct().OrderBy(x => x))
                _typeCb.Items.Add(t);
            _typeCb.SelectedIndex = 0;

            _hwCb.Items.Clear();
            _hwCb.Items.Add("All");
            foreach (var h in sensors.Select(s => s.Hardware).Distinct().OrderBy(x => x))
                _hwCb.Items.Add(h);
            _hwCb.SelectedIndex = 0;
        }

        private void FilterList()
        {
            if (_closing) return;

            _list.ItemChecked -= OnItemChecked;
            _list.BeginUpdate();
            _list.Items.Clear();

            List<SensorInfo> sensors;
            lock (_lockObj) { sensors = new List<SensorInfo>(_sensors); }

            var q = sensors.AsEnumerable();
            var search = _searchBox.Text?.ToLower() ?? "";

            if (!string.IsNullOrEmpty(search))
                q = q.Where(x => x.Name.ToLower().Contains(search) || x.Hardware.ToLower().Contains(search));

            if (_typeCb.SelectedIndex > 0)
                q = q.Where(x => x.Type == _typeCb.SelectedItem.ToString());

            if (_hwCb.SelectedIndex > 0)
                q = q.Where(x => x.Hardware == _hwCb.SelectedItem.ToString());

            foreach (var s in q.Take(250))
            {
                var it = new ListViewItem { Checked = _cfg.Config.SelectedSensors.Contains(s.Id) };
                it.SubItems.Add(s.Hardware);
                it.SubItems.Add(s.Name);
                it.SubItems.Add(s.Type);
                it.SubItems.Add(FormatValue(s.Value, s.Unit));
                it.SubItems.Add($"0x{GetSensorId(s):X2}");
                it.Tag = s;
                it.ForeColor = GetTypeColor(s.Type);
                _list.Items.Add(it);
            }

            _list.EndUpdate();
            _list.ItemChecked += OnItemChecked;
            UpdateInfo();
        }

        private void RefreshValuesAsync()
        {
            if (_refreshing || _closing || !_init) return;
            _refreshing = true;

            try
            {
                List<SensorInfo> fresh;
                lock (_lockObj) { fresh = new List<SensorInfo>(_sensors); }

                if (fresh.Count == 0) return;

                _list.BeginUpdate();
                foreach (ListViewItem it in _list.Items)
                {
                    var s = it.Tag as SensorInfo;
                    if (s == null) continue;
                    var upd = fresh.Find(x => x.Id == s.Id);
                    if (upd != null)
                    {
                        it.SubItems[4].Text = FormatValue(upd.Value, upd.Unit);
                        it.Tag = upd;
                    }
                }
                _list.EndUpdate();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void OnItemChecked(object s, ItemCheckedEventArgs e)
        {
            if (_init && !_refreshing)
            {
                _dirty = true;
                UpdateTitle();
                UpdateInfo();
            }
        }

        private void SetAll(bool chk)
        {
            _list.ItemChecked -= OnItemChecked;
            _list.BeginUpdate();
            foreach (ListViewItem i in _list.Items) i.Checked = chk;
            _list.EndUpdate();
            _list.ItemChecked += OnItemChecked;
            _dirty = true;
            UpdateTitle();
            UpdateInfo();
        }

        private string FormatValue(float? v, string u) => v.HasValue && !float.IsNaN(v.Value) ? $"{v.Value:0.0} {u}" : "-";

        private Color GetTypeColor(string t) => t switch
        {
            "Temperature" => Color.FromArgb(231, 76, 60),
            "Load" => Color.FromArgb(52, 152, 219),
            "Clock" => Color.FromArgb(155, 89, 182),
            "Power" => Color.FromArgb(241, 196, 15),
            "Fan" => Color.FromArgb(46, 204, 113),
            _ => Color.FromArgb(127, 140, 141)
        };

        private int GetTotalSelected()
        {
            var visIds = _list.Items.Cast<ListViewItem>().Select(i => (i.Tag as SensorInfo)?.Id).Where(x => x != null).ToList();
            var chkIds = _list.CheckedItems.Cast<ListViewItem>().Select(i => (i.Tag as SensorInfo)?.Id).Where(x => x != null).ToList();
            return _cfg.Config.SelectedSensors.Where(id => !visIds.Contains(id)).Concat(chkIds).Distinct().Count();
        }

        private void UpdateInfo()
        {
            var total = GetTotalSelected();
            _cntLbl.Text = $"✓ {_list.CheckedItems.Count}/{_list.Items.Count} ({total} total)";
            _selLbl.Text = total.ToString();

            var proto = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);
            int size = proto switch
            {
                ProtocolMode.Binary => 6 + total * 5,
                ProtocolMode.Text => 12 + total * 12,
                _ => 50 + total * 85
            };

            _sizeLbl.Text = size < 1024 ? $"{size} B" : $"{size / 1024.0:0.0} KB";
            _sizeLbl.ForeColor = proto switch
            {
                ProtocolMode.Binary => Color.FromArgb(46, 204, 113),
                ProtocolMode.Text => Color.FromArgb(241, 196, 15),
                _ => Color.FromArgb(231, 76, 60)
            };
            _sizeBar.Value = Math.Min(size, 500);

            var pps = 1000.0 / Math.Max(1, (double)_intervalNum.Value);
            _bwLbl.Text = $"{size * pps:0} B/s ({pps:0.0}/s)";

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var sel = _list.CheckedItems.Cast<ListViewItem>()
                .Take(8)
                .Select(i => i.Tag as SensorInfo)
                .Where(x => x != null)
                .ToList();

            if (sel.Count == 0)
            {
                _previewBox.Text = "(select sensors to preview)";
                return;
            }

            var sb = new StringBuilder();
            var proto = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);

            if (proto == ProtocolMode.Binary)
            {
                sb.AppendLine($"AA 01 {sel.Count:X2}");
                foreach (var s in sel)
                    sb.AppendLine($"  {GetSensorId(s):X2} [float] // {s.Name}:{s.Value:0.0}");
                sb.AppendLine("[CRC16] 55");
            }
            else if (proto == ProtocolMode.Text)
            {
                sb.AppendLine("$START");
                foreach (var s in sel)
                    sb.AppendLine($"{GetSensorId(s):X2}:{s.Value:0.0}");
                sb.AppendLine("$END: XX");
            }
            else
            {
                sb.AppendLine("{");
                sb.AppendLine("  \"sensors\": [");
                foreach (var s in sel)
                    sb.AppendLine($"    {{\"id\": \"0x{GetSensorId(s):X2}\",\"n\":\"{s.Name}\",\"v\":{s.Value:0.0}}},");
                sb.AppendLine("  ]");
                sb.AppendLine("}");
            }

            if (GetTotalSelected() > 8)
                sb.AppendLine($"\n... +{GetTotalSelected() - 8} more sensors");

            _previewBox.Text = sb.ToString();
        }

        private void UpdateIconPreview()
        {
            if (_iconCb.SelectedIndex >= 0)
            {
                var ico = _iconCb.SelectedIndex switch
                {
                    0 => _iconMgr.CreateStatusIcon(TrayIconManager.IconState.Running),
                    1 => _iconMgr.CreateTemperatureIcon(45),
                    2 => _iconMgr.CreateLoadIcon(60, 40),
                    3 => _iconMgr.CreateModernIcon(true, 45),
                    _ => _iconMgr.CreatePulseIcon(0)
                };
                _iconPic.Image = ico.ToBitmap();
            }
        }

        private void UpdateTitle() => Text = _dirty ? "Hardware Monitor - Settings *" : "Hardware Monitor - Settings";

        private void OnSettingChanged(object s, EventArgs e)
        {
            if (_init)
            {
                _dirty = true;
                UpdateTitle();
                UpdateInfo();
            }
        }

        private void Save(object s, EventArgs e)
        {
            _cfg.Config.ComPort = _portCb.SelectedItem?.ToString() ?? "";
            _cfg.Config.BaudRate = (int)(_baudCb.SelectedItem ?? 115200);
            _cfg.Config.SendIntervalMs = (int)_intervalNum.Value;
            _cfg.Config.ProtocolMode = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);
            _cfg.Config.IconStyle = (IconStyle)Math.Max(0, _iconCb.SelectedIndex);
            _cfg.Config.AutoStart = _autoChk.Checked;
            _cfg.Config.StartWithWindows = _winChk.Checked;

            var visIds = _list.Items.Cast<ListViewItem>().Select(i => (i.Tag as SensorInfo)?.Id).Where(x => x != null).ToList();
            var chkIds = _list.CheckedItems.Cast<ListViewItem>().Select(i => (i.Tag as SensorInfo)?.Id).Where(x => x != null).ToList();
            _cfg.Config.SelectedSensors = _cfg.Config.SelectedSensors.Where(id => !visIds.Contains(id)).Concat(chkIds).Distinct().ToList();
            _cfg.SaveConfig();

            _dirty = false;
            UpdateTitle();

            _saveBtn.Text = "✓ Saved";
            _saveBtn.BackColor = Color.FromArgb(39, 174, 96);
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (ss, ee) => { t.Stop(); t.Dispose(); if (!_closing) { _saveBtn.Text = "💾 Save"; _saveBtn.BackColor = Color.FromArgb(52, 152, 219); } };
            t.Start();
        }

        private bool ConfirmExit()
        {
            if (!_dirty) return true;
            var r = MessageBox.Show("Save changes? ", "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) Save(null, null);
            return r != DialogResult.Cancel;
        }

        #region Header Generation

        private void GenerateHeaderFiles(object s, EventArgs e)
        {
            var sel = _list.CheckedItems.Cast<ListViewItem>()
                .Select(i => i.Tag as SensorInfo)
                .Where(x => x != null)
                .ToList();

            if (sel.Count == 0)
            {
                MessageBox.Show("Select at least one sensor.", "No Sensors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "C Header|*. h",
                FileName = "hw_sensors.h",
                Title = "Save Header File (will also create . c file)"
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            var hFile = dlg.FileName;
            var cFile = Path.ChangeExtension(hFile, ".c");
            var baseName = Path.GetFileNameWithoutExtension(hFile).ToUpper();

            // Generate . h
            var hContent = GenerateHeaderContent(sel, baseName, Path.GetFileName(hFile));
            File.WriteAllText(hFile, hContent);

            // Generate . c
            var cContent = GenerateSourceContent(sel, Path.GetFileName(hFile));
            File.WriteAllText(cFile, cContent);

            MessageBox.Show(
                $"Generated files:\n\n" +
                $"• {Path.GetFileName(hFile)}\n" +
                $"• {Path.GetFileName(cFile)}\n\n" +
                $"Sensors: {sel.Count}\n" +
                $"Location: {Path.GetDirectoryName(hFile)}",
                "✓ Files Generated",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private string GenerateHeaderContent(List<SensorInfo> sensors, string baseName, string fileName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("/**");
            sb.AppendLine($" * Hardware Monitor Sensor Definitions");
            sb.AppendLine($" * Generated:  {DateTime.Now:yyyy-MM-dd HH:mm: ss}");
            sb.AppendLine($" * Sensors: {sensors.Count}");
            sb.AppendLine(" *");
            sb.AppendLine(" * Compatible with ESP-IDF, Arduino, and plain C");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine($"#ifndef {baseName}_H");
            sb.AppendLine($"#define {baseName}_H");
            sb.AppendLine();
            sb.AppendLine("#ifdef __cplusplus");
            sb.AppendLine("extern \"C\" {");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine("#include <stdbool. h>");
            sb.AppendLine("#include <stddef.h>");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Protocol Constants");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("#define HW_PROTO_START   0xAA");
            sb.AppendLine("#define HW_PROTO_END     0x55");
            sb.AppendLine("#define HW_PROTO_VERSION 0x01");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Sensor IDs");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("typedef enum {");

            // Group sensors by category
            var grouped = sensors.GroupBy(s => GetCategory(s)).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                sb.AppendLine($"    /* {group.Key} */");
                foreach (var sensor in group)
                {
                    var id = GetSensorId(sensor);
                    var enumName = $"SENSOR_{GetCategory(sensor)}_{sensor.Type.ToUpper()}";
                    if (id >= 0x80) enumName += $"_{id - 0x80}";
                    sb.AppendLine($"    {enumName,-40} = 0x{id:X2},  /* {sensor.Name} */");
                }
                sb.AppendLine();
            }

            sb.AppendLine("    SENSOR_UNKNOWN = 0xFF");
            sb.AppendLine("} hw_sensor_id_t;");
            sb.AppendLine();
            sb.AppendLine($"#define HW_SENSOR_COUNT {sensors.Count}");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Data Structures");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("typedef struct {");
            sb.AppendLine("    hw_sensor_id_t id;");
            sb.AppendLine("    float value;");
            sb.AppendLine("    bool valid;");
            sb.AppendLine("    uint32_t timestamp;");
            sb.AppendLine("} hw_sensor_data_t;");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Global Storage");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("extern hw_sensor_data_t hw_sensors[HW_SENSOR_COUNT];");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Function Prototypes");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * Initialize sensor storage");
            sb.AppendLine(" */");
            sb.AppendLine("void hw_sensors_init(void);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * Parse binary packet from PC");
            sb.AppendLine(" * @param data Pointer to packet data");
            sb.AppendLine(" * @param len Length of packet");
            sb.AppendLine(" * @return true if packet was valid");
            sb.AppendLine(" */");
            sb.AppendLine("bool hw_sensors_parse(const uint8_t* data, size_t len);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * Get sensor value by ID");
            sb.AppendLine(" * @param id Sensor ID");
            sb.AppendLine(" * @return Sensor value or -999. 0 if not found/invalid");
            sb.AppendLine(" */");
            sb.AppendLine("float hw_sensors_get(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * Check if sensor data is valid");
            sb.AppendLine(" * @param id Sensor ID");
            sb.AppendLine(" * @return true if sensor has valid data");
            sb.AppendLine(" */");
            sb.AppendLine("bool hw_sensors_valid(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * Get sensor name");
            sb.AppendLine(" * @param id Sensor ID");
            sb.AppendLine(" * @return Sensor name string");
            sb.AppendLine(" */");
            sb.AppendLine("const char* hw_sensors_name(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * Get sensor unit");
            sb.AppendLine(" * @param id Sensor ID");
            sb.AppendLine(" * @return Unit string (°C, %, MHz, etc.)");
            sb.AppendLine(" */");
            sb.AppendLine("const char* hw_sensors_unit(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Convenience Getter Macros");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();

            foreach (var sensor in sensors)
            {
                var id = GetSensorId(sensor);
                var macroName = $"HW_GET_{GetCategory(sensor)}_{sensor.Type.ToUpper()}";
                if (id >= 0x80) macroName += $"_{id - 0x80}";
                sb.AppendLine($"#define {macroName}() hw_sensors_get(0x{id:X2})");
            }

            sb.AppendLine();
            sb.AppendLine("#ifdef __cplusplus");
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine($"#endif /* {baseName}_H */");

            return sb.ToString();
        }

        private string GenerateSourceContent(List<SensorInfo> sensors, string headerFile)
        {
            var sb = new StringBuilder();

            sb.AppendLine("/**");
            sb.AppendLine($" * Hardware Monitor Sensor Implementation");
            sb.AppendLine($" * Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine($"#include \"{headerFile}\"");
            sb.AppendLine("#include <string.h>");
            sb.AppendLine();
            sb.AppendLine("/* Sensor storage */");
            sb.AppendLine("hw_sensor_data_t hw_sensors[HW_SENSOR_COUNT];");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Initialization");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("void hw_sensors_init(void) {");
            sb.AppendLine("    memset(hw_sensors, 0, sizeof(hw_sensors));");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        hw_sensors[i].id = SENSOR_UNKNOWN;");
            sb.AppendLine("        hw_sensors[i].value = -999.0f;");
            sb.AppendLine("        hw_sensors[i].valid = false;");
            sb.AppendLine("        hw_sensors[i].timestamp = 0;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Packet Parser");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("bool hw_sensors_parse(const uint8_t* data, size_t len) {");
            sb.AppendLine("    if (! data || len < 6) return false;");
            sb.AppendLine("    if (data[0] != HW_PROTO_START) return false;");
            sb.AppendLine("    if (data[1] != HW_PROTO_VERSION) return false;");
            sb.AppendLine();
            sb.AppendLine("    uint8_t count = data[2];");
            sb.AppendLine("    size_t expected = 3 + (count * 5) + 3;  /* header + data + crc + end */");
            sb.AppendLine("    if (len < expected) return false;");
            sb.AppendLine("    if (data[len - 1] != HW_PROTO_END) return false;");
            sb.AppendLine();
            sb.AppendLine("    /* TODO: Verify CRC16 at data[len-3], data[len-2] */");
            sb.AppendLine();
            sb.AppendLine("    size_t offset = 3;");
            sb.AppendLine("    for (uint8_t i = 0; i < count && i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        hw_sensors[i]. id = (hw_sensor_id_t)data[offset];");
            sb.AppendLine();
            sb.AppendLine("        /* Parse float (little-endian) */");
            sb.AppendLine("        union {");
            sb.AppendLine("            float f;");
            sb.AppendLine("            uint8_t b[4];");
            sb.AppendLine("        } conv;");
            sb.AppendLine("        conv.b[0] = data[offset + 1];");
            sb.AppendLine("        conv.b[1] = data[offset + 2];");
            sb.AppendLine("        conv.b[2] = data[offset + 3];");
            sb.AppendLine("        conv. b[3] = data[offset + 4];");
            sb.AppendLine();
            sb.AppendLine("        hw_sensors[i]. value = conv.f;");
            sb.AppendLine("        hw_sensors[i].valid = true;");
            sb.AppendLine("        /* hw_sensors[i]. timestamp = get_timestamp_ms(); */");
            sb.AppendLine();
            sb.AppendLine("        offset += 5;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    return true;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Getters");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("float hw_sensors_get(hw_sensor_id_t id) {");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (hw_sensors[i].id == id && hw_sensors[i].valid) {");
            sb.AppendLine("            return hw_sensors[i].value;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return -999.0f;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("bool hw_sensors_valid(hw_sensor_id_t id) {");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (hw_sensors[i]. id == id) {");
            sb.AppendLine("            return hw_sensors[i].valid;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return false;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("/* ========================================");
            sb.AppendLine("   Metadata");
            sb.AppendLine("   ======================================== */");
            sb.AppendLine();
            sb.AppendLine("const char* hw_sensors_name(hw_sensor_id_t id) {");
            sb.AppendLine("    switch (id) {");

            foreach (var sensor in sensors)
            {
                var id = GetSensorId(sensor);
                var enumName = $"SENSOR_{GetCategory(sensor)}_{sensor.Type.ToUpper()}";
                if (id >= 0x80) enumName += $"_{id - 0x80}";
                sb.AppendLine($"        case {enumName}:  return \"{sensor.Name}\";");
            }

            sb.AppendLine("        default: return \"Unknown\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("const char* hw_sensors_unit(hw_sensor_id_t id) {");
            sb.AppendLine("    switch (id) {");

            // Group by unit
            var byUnit = sensors.GroupBy(s => s.Unit ?? "");
            foreach (var group in byUnit)
            {
                foreach (var sensor in group)
                {
                    var id = GetSensorId(sensor);
                    var enumName = $"SENSOR_{GetCategory(sensor)}_{sensor.Type.ToUpper()}";
                    if (id >= 0x80) enumName += $"_{id - 0x80}";
                    sb.AppendLine($"        case {enumName}:");
                }
                sb.AppendLine($"            return \"{group.Key}\";");
            }

            sb.AppendLine("        default: return \"\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion
    }
}