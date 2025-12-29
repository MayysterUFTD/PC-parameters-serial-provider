using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
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
        private NumericUpDown _intervalNum, _refreshNum;
        private CheckBox _autoChk, _winChk;
        private ListView _list;
        private TextBox _searchBox, _previewBox;
        private Label _cntLbl, _selLbl, _sizeLbl, _bwLbl, _loadLbl, _statusLbl;
        private ProgressBar _sizeBar;
        private PictureBox _iconPic;
        private Button _saveBtn, _startStopBtn, _helpBtn;
        private TrayIconManager _iconMgr;
        private System.Windows.Forms.Timer _uiTimer;
        private System.Windows.Forms.Timer _previewAnimTimer;
        private int _previewAnimFrame = 0;

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

            _uiTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _uiTimer.Tick += OnUiTimerTick;

            // Timer do animacji preview
            _previewAnimTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _previewAnimTimer.Tick += (s, e) => { _previewAnimFrame++; UpdatePreview(); };

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
            _previewAnimTimer.Start();
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
                try { BeginInvoke((Action)InitSensorList); }
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
            lock (_lockObj) { sensors = new List<SensorInfo>(_sensors); }

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

        /// <summary>
        /// Generuje unikalną, czytelną nazwę dla sensora (do . h/. c)
        /// </summary>
        private string GetSensorEnumName(SensorInfo s)
        {
            var cat = GetCategory(s);
            var hw = s.Hardware.ToLower();
            var nm = s.Name.ToLower();
            var tp = s.Type;

            // Określ specyficzny suffix na podstawie nazwy
            string suffix = "";

            if (cat == "CPU")
            {
                if (nm.Contains("package")) suffix = "_PACKAGE";
                else if (nm.Contains("tctl") || nm.Contains("tdie")) suffix = "_TDIE";
                else if (nm.Contains("ccd1")) suffix = "_CCD1";
                else if (nm.Contains("ccd2")) suffix = "_CCD2";
                else if (nm.Contains("core #1") || nm.Contains("core 1")) suffix = "_CORE1";
                else if (nm.Contains("core #2") || nm.Contains("core 2")) suffix = "_CORE2";
                else if (nm.Contains("core #3") || nm.Contains("core 3")) suffix = "_CORE3";
                else if (nm.Contains("core #4") || nm.Contains("core 4")) suffix = "_CORE4";
                else if (nm.Contains("core #5") || nm.Contains("core 5")) suffix = "_CORE5";
                else if (nm.Contains("core #6") || nm.Contains("core 6")) suffix = "_CORE6";
                else if (nm.Contains("core #7") || nm.Contains("core 7")) suffix = "_CORE7";
                else if (nm.Contains("core #8") || nm.Contains("core 8")) suffix = "_CORE8";
                else if (nm.Contains("total")) suffix = "_TOTAL";
                else if (nm.Contains("core")) suffix = "_CORES";
            }
            else if (cat == "GPU")
            {
                if (nm.Contains("hotspot") || nm.Contains("hot spot")) suffix = "_HOTSPOT";
                else if (nm.Contains("memory") || nm.Contains("vram")) suffix = "_VRAM";
                else if (nm.Contains("core")) suffix = "_CORE";
                else if (nm.Contains("video")) suffix = "_VIDEO";
            }
            else if (cat == "RAM")
            {
                if (nm.Contains("used")) suffix = "_USED";
                else if (nm.Contains("available")) suffix = "_AVAILABLE";
                else if (nm.Contains("virtual")) suffix = "_VIRTUAL";
            }
            else if (cat == "DISK")
            {
                // Dodaj nazwę dysku jeśli jest
                if (hw.Contains("samsung")) suffix = "_SAMSUNG";
                else if (hw.Contains("crucial")) suffix = "_CRUCIAL";
                else if (hw.Contains("wd") || hw.Contains("western")) suffix = "_WD";
            }

            return $"SENSOR_{cat}_{tp.ToUpper()}{suffix}";
        }

        /// <summary>
        /// Generuje nazwę funkcji getter dla sensora
        /// </summary>
        private string GetSensorFuncName(SensorInfo s)
        {
            var enumName = GetSensorEnumName(s);
            // SENSOR_CPU_TEMPERATURE_PACKAGE -> get_cpu_temperature_package
            var funcName = enumName.Replace("SENSOR_", "get_").ToLower();
            return funcName;
        }

        private void BuildUI()
        {
            Text = "Hardware Monitor - Settings";
            Size = new Size(1150, 850);
            MinimumSize = new Size(1050, 750);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);
            Icon = CreateAppIcon();
            BackColor = Color.FromArgb(250, 250, 252);

            var mainTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(15)
            };
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));  // Status
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));  // Connection
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); // Appearance
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));  // Startup
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Content
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // Buttons

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
            var grp = new GroupBox
            {
                Text = "📊 Status",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(15, 8, 15, 8)
            };

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };

            _startStopBtn = new Button
            {
                Text = "▶ Start",
                Width = 130,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 20, 0)
            };
            _startStopBtn.FlatAppearance.BorderSize = 0;
            _startStopBtn.Click += OnStartStopClick;
            flow.Controls.Add(_startStopBtn);

            _statusLbl = new Label
            {
                Text = "● Stopped",
                AutoSize = true,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.Gray,
                Margin = new Padding(0, 8, 0, 0)
            };
            flow.Controls.Add(_statusLbl);

            grp.Controls.Add(flow);
            return grp;
        }

        private GroupBox BuildConnectionGroup()
        {
            var grp = new GroupBox
            {
                Text = "📡 Connection",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(15, 8, 15, 8)
            };

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };

            // Port
            flow.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
            _portCb = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 8, 0) };
            _portCb.SelectedIndexChanged += OnSettingChanged;
            flow.Controls.Add(_portCb);

            var refreshBtn = new Button { Text = "🔄", Width = 35, Height = 28, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 6, 20, 0) };
            refreshBtn.FlatAppearance.BorderSize = 0;
            refreshBtn.Click += (s, e) => RefreshPorts();
            flow.Controls.Add(refreshBtn);

            // Baud
            flow.Controls.Add(new Label { Text = "Baud:", AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
            _baudCb = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 20, 0) };
            _baudCb.Items.AddRange(new object[] { 9600, 57600, 115200, 230400, 460800, 921600 });
            _baudCb.SelectedIndexChanged += OnSettingChanged;
            flow.Controls.Add(_baudCb);

            // Send Interval
            flow.Controls.Add(new Label { Text = "Send:", AutoSize = true, Margin = new Padding(0, 10, 5, 0) });
            _intervalNum = new NumericUpDown { Width = 70, Minimum = 50, Maximum = 5000, Increment = 50, Margin = new Padding(0, 6, 3, 0) };
            _intervalNum.ValueChanged += OnSettingChanged;
            flow.Controls.Add(_intervalNum);
            flow.Controls.Add(new Label { Text = "ms", AutoSize = true, Margin = new Padding(0, 10, 15, 0) });

            // Refresh Interval (NOWE!)
            flow.Controls.Add(new Label { Text = "Refresh:", AutoSize = true, Margin = new Padding(0, 10, 5, 0) });
            _refreshNum = new NumericUpDown { Width = 70, Minimum = 50, Maximum = 2000, Increment = 50, Margin = new Padding(0, 6, 3, 0) };
            _refreshNum.ValueChanged += OnSettingChanged;
            flow.Controls.Add(_refreshNum);
            flow.Controls.Add(new Label { Text = "ms", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });

            grp.Controls.Add(flow);
            return grp;

        }

        private GroupBox BuildAppearanceGroup()
        {
            var grp = new GroupBox
            {
                Text = "🎨 Appearance",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(15, 8, 15, 8)
            };

            var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            tbl.Controls.Add(new Label { Text = "Protocol:", AutoSize = true, Margin = new Padding(0, 10, 10, 0) }, 0, 0);
            _protoCb = new ComboBox { Width = 210, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 0, 0) };
            _protoCb.Items.AddRange(new[] { "⚡ Binary (5 B/sensor)", "📝 Text (12 B/sensor)", "📋 JSON (80 B/sensor)" });
            _protoCb.SelectedIndexChanged += OnSettingChanged;
            tbl.Controls.Add(_protoCb, 1, 0);

            tbl.Controls.Add(new Label { Text = "Tray Icon:", AutoSize = true, Margin = new Padding(0, 10, 10, 0) }, 0, 1);
            _iconCb = new ComboBox { Width = 210, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 0, 0) };
            _iconCb.Items.AddRange(new[] { "🔴 Status Color", "🌡️ Temperature", "📊 Load Bars", "💻 Modern Chip", "⚡ Animated" });
            _iconCb.SelectedIndexChanged += (s, e) => { UpdateIconPreview(); OnSettingChanged(s, e); };
            tbl.Controls.Add(_iconCb, 1, 1);

            _iconPic = new PictureBox
            {
                Width = 44,
                Height = 44,
                BackColor = Color.FromArgb(44, 62, 80),
                Margin = new Padding(20, 6, 0, 0),
                BorderStyle = BorderStyle.FixedSingle
            };
            tbl.Controls.Add(_iconPic, 2, 1);

            grp.Controls.Add(tbl);
            return grp;
        }

        private GroupBox BuildStartupGroup()
        {
            var grp = new GroupBox
            {
                Text = "🚀 Startup",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(15, 8, 15, 8)
            };

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };

            _autoChk = new CheckBox { Text = "Auto-start monitoring on launch", AutoSize = true, Margin = new Padding(0, 8, 60, 0) };
            _autoChk.CheckedChanged += OnSettingChanged;
            flow.Controls.Add(_autoChk);

            _winChk = new CheckBox { Text = "Start with Windows", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            _winChk.CheckedChanged += OnSettingChanged;
            flow.Controls.Add(_winChk);

            grp.Controls.Add(flow);
            return grp;
        }

        private Panel BuildContentPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };

            var rightPanel = new Panel { Dock = DockStyle.Right, Width = 360, Padding = new Padding(12, 0, 0, 0) };
            BuildInfoPanel(rightPanel);

            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 0) };
            BuildSensorsPanel(leftPanel);

            panel.Controls.Add(leftPanel);
            panel.Controls.Add(rightPanel);

            return panel;
        }

        private void BuildSensorsPanel(Panel parent)
        {
            var grp = new GroupBox { Text = "📊 Sensors", Dock = DockStyle.Fill, Padding = new Padding(12) };
            var mainPanel = new Panel { Dock = DockStyle.Fill };

            // Search
            var searchPanel = new Panel { Dock = DockStyle.Top, Height = 38 };
            _searchBox = new TextBox { Width = 320, Location = new Point(0, 6) };
            _searchBox.TextChanged += (s, e) => { if (_init) FilterList(); };
            searchPanel.Controls.Add(_searchBox);

            var clrBtn = new Button
            {
                Text = "✕",
                Width = 32,
                Height = 28,
                Location = new Point(325, 5),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White
            };
            clrBtn.FlatAppearance.BorderSize = 0;
            clrBtn.Click += (s, e) => _searchBox.Text = "";
            searchPanel.Controls.Add(clrBtn);

            // Filters
            var filterPanel = new Panel { Dock = DockStyle.Top, Height = 38 };
            filterPanel.Controls.Add(new Label { Text = "Type:", AutoSize = true, Location = new Point(0, 10) });
            _typeCb = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(45, 6) };
            _typeCb.SelectedIndexChanged += (s, e) => { if (_init) FilterList(); };
            filterPanel.Controls.Add(_typeCb);

            filterPanel.Controls.Add(new Label { Text = "Hardware:", AutoSize = true, Location = new Point(195, 10) });
            _hwCb = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(265, 6) };
            _hwCb.SelectedIndexChanged += (s, e) => { if (_init) FilterList(); };
            filterPanel.Controls.Add(_hwCb);

            // Buttons
            var btnPanel = new Panel { Dock = DockStyle.Top, Height = 42 };

            var allBtn = new Button
            {
                Text = "☑ All",
                Width = 65,
                Height = 30,
                Location = new Point(0, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White
            };
            allBtn.FlatAppearance.BorderSize = 0;
            allBtn.Click += (s, e) => SetAll(true);
            btnPanel.Controls.Add(allBtn);

            var noneBtn = new Button
            {
                Text = "☐ None",
                Width = 75,
                Height = 30,
                Location = new Point(70, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White
            };
            noneBtn.FlatAppearance.BorderSize = 0;
            noneBtn.Click += (s, e) => SetAll(false);
            btnPanel.Controls.Add(noneBtn);

            var hdrBtn = new Button
            {
                Text = "📄 Generate . h/.c",
                Width = 130,
                Height = 30,
                Location = new Point(150, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White
            };
            hdrBtn.FlatAppearance.BorderSize = 0;
            hdrBtn.Click += GenerateHeaderFiles;
            btnPanel.Controls.Add(hdrBtn);

            _cntLbl = new Label { AutoSize = true, Location = new Point(290, 12), ForeColor = Color.Gray };
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
            _list.Columns.Add("Name", 160);
            _list.Columns.Add("Type", 90);
            _list.Columns.Add("Value", 90);
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
            var grp = new GroupBox { Text = "📦 Packet Info", Dock = DockStyle.Fill, Padding = new Padding(15) };
            var panel = new Panel { Dock = DockStyle.Fill };

            int y = 0;

            panel.Controls.Add(new Label { Text = "Selected Sensors:", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 24;

            _selLbl = new Label { Text = "0", Font = new Font("Segoe UI", 36, FontStyle.Bold), ForeColor = Color.FromArgb(52, 152, 219), AutoSize = true, Location = new Point(0, y) };
            panel.Controls.Add(_selLbl);
            y += 65;

            panel.Controls.Add(new Label { Text = "Packet Size:", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 24;

            _sizeLbl = new Label { Text = "0 B", Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = Color.FromArgb(46, 204, 113), AutoSize = true, Location = new Point(0, y) };
            panel.Controls.Add(_sizeLbl);
            y += 40;

            _sizeBar = new ProgressBar { Width = 300, Height = 16, Maximum = 500, Location = new Point(0, y) };
            panel.Controls.Add(_sizeBar);
            y += 26;

            _bwLbl = new Label { Text = "0 B/s", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.FromArgb(155, 89, 182), AutoSize = true, Location = new Point(0, y) };
            panel.Controls.Add(_bwLbl);
            y += 45;

            // Help button
            _helpBtn = new Button
            {
                Text = "❓ Protocol Help",
                Width = 140,
                Height = 30,
                Location = new Point(0, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 196, 15),
                ForeColor = Color.Black
            };
            _helpBtn.FlatAppearance.BorderSize = 0;
            _helpBtn.Click += ShowProtocolHelp;
            panel.Controls.Add(_helpBtn);
            y += 45;

            panel.Controls.Add(new Label { Text = "Live Preview:", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 24;

            _previewBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5f),
                BackColor = Color.FromArgb(20, 20, 25),
                ForeColor = Color.FromArgb(46, 204, 113),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(0, y),
                Size = new Size(310, 220)
            };
            panel.Controls.Add(_previewBox);

            panel.Resize += (s, e) =>
            {
                _previewBox.Width = Math.Max(200, panel.Width - 30);
                _previewBox.Height = Math.Max(150, panel.Height - y - 20);
                _sizeBar.Width = Math.Max(200, panel.Width - 30);
            };

            grp.Controls.Add(panel);
            parent.Controls.Add(grp);
        }

        private Panel BuildButtonsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };

            var exitBtn = new Button
            {
                Text = "🚪 Exit",
                Width = 110,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(149, 165, 166),
                ForeColor = Color.White,
                Margin = new Padding(10, 0, 0, 0)
            };
            exitBtn.FlatAppearance.BorderSize = 0;
            exitBtn.Click += (s, e) => { if (ConfirmExit()) Close(); };
            flow.Controls.Add(exitBtn);

            _saveBtn = new Button
            {
                Text = "💾 Save",
                Width = 110,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White
            };
            _saveBtn.FlatAppearance.BorderSize = 0;
            _saveBtn.Click += Save;
            flow.Controls.Add(_saveBtn);

            panel.Controls.Add(flow);
            return panel;
        }

        private void ShowProtocolHelp(object s, EventArgs e)
        {
            var helpForm = new Form
            {
                Text = "Protocol Documentation",
                Size = new Size(800, 700),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(30, 30, 35),
                Font = new Font("Consolas", 10)
            };

            var textBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 35),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10)
            };

            textBox.Text = @"
================================================================================
                        HARDWARE MONITOR PROTOCOL v1.0
================================================================================

1. BINARY PROTOCOL (Recommended - most efficient)
--------------------------------------------------------------------------------

Packet structure:
+-------+-----+-------+------------------+-------+-----+
| START | VER | COUNT |   SENSOR DATA    | CRC16 | END |
| 0xAA  | 1B  |  1B   |    N x 5B        |  2B   |0x55 |
+-------+-----+-------+------------------+-------+-----+

Each sensor (5 bytes):
+----------+----------------------------------+
| ID (1B)  | VALUE (4B float, little-endian) |
+----------+----------------------------------+

Example (2 sensors, CPU=65. 5°C, GPU=70.0°C):
AA 01 02 01 00 00 83 42 10 00 00 8C 42 [CRC] 55
|  |  |  |  |---------|  |  |---------|       |
|  |  |  |  CPU=65.5     |  GPU=70.0          End
|  |  |  CPU ID (0x01)   GPU ID (0x10)
|  |  2 sensors
|  Version 1
Start


2. SENSOR IDs
--------------------------------------------------------------------------------

CPU Sensors:          GPU Sensors:          RAM Sensors: 
  0x01 - Temp Pkg       0x10 - Temp Core      0x20 - Data Used
  0x02 - Load Total     0x11 - Load Core      0x21 - Data Avail
  0x03 - Clock          0x12 - Clock Core     0x22 - Load %
  0x04 - Power Pkg      0x13 - Clock Mem
  0x05 - Temp Core      0x14 - Power        DISK Sensors: 
  0x06 - Load Core      0x15 - Load Mem       0x30 - Temperature
  0x07 - Power Core     0x16 - Fan RPM        0x31 - Load %
  0x08 - Temp CCD       0x17 - Temp Mem
                        0x18 - Temp Hotspot Custom:  0x80-0xFE


3. TEXT PROTOCOL (For debugging)
--------------------------------------------------------------------------------

$START
01: 65.5
10:70.0
02:45.2
$END: XX

Format:  <ID_HEX>:<VALUE>


4. JSON PROTOCOL (For web apps)
--------------------------------------------------------------------------------

{
  ""timestamp"": ""2024-01-15T12:00:00Z"",
  ""sensors"": [
    {""id"": ""0x01"", ""name"": ""CPU Temp"", ""value"": 65.5, ""unit"": ""°C""},
    {""id"": ""0x10"", ""name"": ""GPU Temp"", ""value"": 70.0, ""unit"": ""°C""}
  ]
}


5. ESP32/ESP-IDF EXAMPLE
--------------------------------------------------------------------------------

#include ""hw_monitor.h""
#include ""driver/uart.h""

void app_main() {
    hw_init();
    uart_config_t cfg = {
        .baud_rate = 115200,
        . data_bits = UART_DATA_8_BITS,
        .parity = UART_PARITY_DISABLE,
        .stop_bits = UART_STOP_BITS_1,
    };
    uart_driver_install(UART_NUM_1, 1024, 0, 0, NULL, 0);
    uart_param_config(UART_NUM_1, &cfg);
    
    uint8_t buf[512];
    while (1) {
        int len = uart_read_bytes(UART_NUM_1, buf, sizeof(buf), 100);
        if (len > 0 && hw_parse(buf, len)) {
            float cpu = hw_get_cpu_temperature_pkg();
            float gpu = hw_get_gpu_temperature_core();
            printf(""CPU: %.1f°C, GPU: %.1f°C\n"", cpu, gpu);
        }
    }
}

================================================================================
";

            helpForm.Controls.Add(textBox);
            helpForm.ShowDialog(this);
        }

        private void OnFormClosing(object s, FormClosingEventArgs e)
        {
            _closing = true;
            _uiTimer?.Stop();
            _uiTimer?.Dispose();
            _previewAnimTimer?.Stop();
            _previewAnimTimer?.Dispose();
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
            _intervalNum.Value = Math.Clamp(_cfg.Config.SendIntervalMs, 50, 5000);
            _refreshNum.Value = Math.Clamp(_cfg.Config.RefreshIntervalMs, 50, 2000);  // NOWE
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
            _bwLbl.Text = $"{size * pps:0} B/s ({pps:0.0} packets/s)";

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var sel = _list.CheckedItems.Cast<ListViewItem>()
                .Take(6)
                .Select(i => i.Tag as SensorInfo)
                .Where(x => x != null)
                .ToList();

            if (sel.Count == 0)
            {
                _previewBox.Text = "(select sensors to see live preview)";
                return;
            }

            var sb = new StringBuilder();
            var proto = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);

            // Symuluj zmieniające się wartości
            var rand = new Random(_previewAnimFrame);

            if (proto == ProtocolMode.Binary)
            {
                sb.AppendLine("┌─────────────────────────────────────────────┐");
                sb.AppendLine("│ BINARY PACKET (Live)                        │");
                sb.AppendLine("├─────────────────────────────────────────────┤");
                sb.AppendLine();

                // Header
                sb.AppendLine($"START: AA");
                sb.AppendLine($"VER:    01");
                sb.AppendLine($"COUNT: {sel.Count:X2} ({sel.Count} sensors)");
                sb.AppendLine();
                sb.AppendLine("DATA:");

                foreach (var s in sel)
                {
                    var id = GetSensorId(s);
                    var baseVal = s.Value ?? 50f;
                    var simVal = baseVal + (float)(rand.NextDouble() * 2 - 1); // ±1 variation
                    var bytes = BitConverter.GetBytes(simVal);

                    sb.AppendLine($"  [{id:X2}] {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}  = {simVal:F1} ({s.Name})");
                }

                sb.AppendLine();
                sb.AppendLine($"CRC16: XX XX");
                sb.AppendLine($"END:    55");
                sb.AppendLine();
                sb.AppendLine("├─────────────────────────────────────────────┤");
                sb.AppendLine($"│ Total: {6 + sel.Count * 5} bytes                              │");
                sb.AppendLine("└─────────────────────────────────────────────┘");
            }
            else if (proto == ProtocolMode.Text)
            {
                sb.AppendLine("┌─────────────────────────────────────────────┐");
                sb.AppendLine("│ TEXT PROTOCOL (Live)                        │");
                sb.AppendLine("├─────────────────────────────────────────────┤");
                sb.AppendLine();
                sb.AppendLine("$START");

                foreach (var s in sel)
                {
                    var id = GetSensorId(s);
                    var baseVal = s.Value ?? 50f;
                    var simVal = baseVal + (float)(rand.NextDouble() * 2 - 1);
                    sb.AppendLine($"{id:X2}:{simVal:F1}");
                }

                sb.AppendLine("$END: XX");
                sb.AppendLine();
                sb.AppendLine("└─────────────────────────────────────────────┘");
            }
            else
            {
                sb.AppendLine("{");
                sb.AppendLine($"  \"timestamp\": \"{DateTime.UtcNow: o}\",");
                sb.AppendLine("  \"sensors\":  [");

                for (int i = 0; i < sel.Count; i++)
                {
                    var s = sel[i];
                    var baseVal = s.Value ?? 50f;
                    var simVal = baseVal + (float)(rand.NextDouble() * 2 - 1);
                    var comma = i < sel.Count - 1 ? "," : "";
                    sb.AppendLine($"    {{\"id\": \"0x{GetSensorId(s):X2}\",\"name\":\"{s.Name}\",\"value\":{simVal:F1}}}{comma}");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");
            }

            if (GetTotalSelected() > 6)
                sb.AppendLine($"\n... and {GetTotalSelected() - 6} more sensors");

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
            _cfg.Config.RefreshIntervalMs = (int)_refreshNum.Value;  // NOWE
            _cfg.Config.ProtocolMode = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);
            _cfg.Config.IconStyle = (IconStyle)Math.Max(0, _iconCb.SelectedIndex);
            _cfg.Config.AutoStart = _autoChk.Checked;
            _cfg.Config.StartWithWindows = _winChk.Checked;

            var visIds = _list.Items.Cast<ListViewItem>().Select(i => (i.Tag as SensorInfo)?.Id).Where(x => x != null).ToList();
            var chkIds = _list.CheckedItems.Cast<ListViewItem>().Select(i => (i.Tag as SensorInfo)?.Id).Where(x => x != null).ToList();
            _cfg.Config.SelectedSensors = _cfg.Config.SelectedSensors.Where(id => !visIds.Contains(id)).Concat(chkIds).Distinct().ToList();
            _cfg.SaveConfig();
            _mon.RefreshIntervalMs = _cfg.Config.RefreshIntervalMs;
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
            var r = MessageBox.Show("Save changes?", "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) Save(null, null);
            return r != DialogResult.Cancel;
        }

        #region Header Generation

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
                Filter = "C Header|*.h",
                FileName = "hw_monitor. h",
                Title = "Save Header File (. c will be created automatically)"
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            var hFile = dlg.FileName;
            var cFile = Path.ChangeExtension(hFile, ".c");
            var baseName = Path.GetFileNameWithoutExtension(hFile).ToUpper().Replace("-", "_").Replace(" ", "_");

            // Buduj unikalne nazwy - każdy sensor musi mieć unikalne ID i nazwę
            var sensorData = new List<(SensorInfo sensor, string enumName, byte id)>();
            var usedEnumNames = new HashSet<string>();
            var usedIds = new HashSet<byte>();

            foreach (var sensor in sel)
            {
                var id = GetSensorId(sensor);

                // Jeśli ID już użyte, przypisz nowe z custom range
                while (usedIds.Contains(id))
                {
                    id = _nextCustomId++;
                }
                usedIds.Add(id);

                // Generuj unikalną nazwę
                var baseEnumName = GenerateEnumName(sensor);
                var enumName = baseEnumName;
                int suffix = 2;

                while (usedEnumNames.Contains(enumName))
                {
                    enumName = $"{baseEnumName}_{suffix}";
                    suffix++;
                }
                usedEnumNames.Add(enumName);

                sensorData.Add((sensor, enumName, id));
            }

            // Generate files
            var hContent = GenerateHeader(sensorData, baseName, Path.GetFileName(hFile));
            var cContent = GenerateSource(sensorData, Path.GetFileName(hFile), baseName);

            File.WriteAllText(hFile, hContent);
            File.WriteAllText(cFile, cContent);

            MessageBox.Show(
                $"Generated:\n\n" +
                $"• {Path.GetFileName(hFile)}\n" +
                $"• {Path.GetFileName(cFile)}\n\n" +
                $"Sensors:  {sensorData.Count}\n" +
                $"Location: {Path.GetDirectoryName(hFile)}",
                "Success",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private string GenerateEnumName(SensorInfo s)
        {
            var cat = GetCategory(s);
            var hw = s.Hardware.ToLower();
            var nm = s.Name.ToLower();
            var tp = s.Type.ToUpper();

            // Buduj szczegółową nazwę
            var parts = new List<string> { "SENSOR", cat, tp };

            // Dodaj specyficzne info
            if (cat == "CPU")
            {
                if (nm.Contains("package")) parts.Add("PKG");
                else if (nm.Contains("tctl") || nm.Contains("tdie")) parts.Add("TDIE");
                else if (nm.Contains("ccd"))
                {
                    if (nm.Contains("1")) parts.Add("CCD1");
                    else if (nm.Contains("2")) parts.Add("CCD2");
                    else parts.Add("CCD");
                }
                else if (nm.Contains("core"))
                {
                    // Wyciągnij numer rdzenia
                    var match = System.Text.RegularExpressions.Regex.Match(nm, @"#? (\d+)");
                    if (match.Success)
                        parts.Add($"CORE{match.Groups[1].Value}");
                    else
                        parts.Add("CORE");
                }
                else if (nm.Contains("total")) parts.Add("TOTAL");
            }
            else if (cat == "GPU")
            {
                if (nm.Contains("hotspot") || nm.Contains("hot spot")) parts.Add("HOTSPOT");
                else if (nm.Contains("memory") || nm.Contains("vram")) parts.Add("VRAM");
                else if (nm.Contains("core")) parts.Add("CORE");
                else if (nm.Contains("video")) parts.Add("VIDEO");
            }
            else if (cat == "RAM")
            {
                if (nm.Contains("used")) parts.Add("USED");
                else if (nm.Contains("available")) parts.Add("AVAIL");
                else if (nm.Contains("virtual")) parts.Add("VIRT");
            }
            else if (cat == "DISK")
            {
                if (hw.Contains("samsung")) parts.Add("SAMSUNG");
                else if (hw.Contains("crucial")) parts.Add("CRUCIAL");
                else if (hw.Contains("wd") || hw.Contains("western")) parts.Add("WD");
                else if (hw.Contains("nvme")) parts.Add("NVME");
            }

            return string.Join("_", parts);
        }

        private string GenerateHeader(List<(SensorInfo sensor, string enumName, byte id)> sensors, string baseName, string fileName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("/**");
            sb.AppendLine($" * @file {fileName}");
            sb.AppendLine(" * @brief Hardware Monitor - Sensor Definitions");
            sb.AppendLine($" * @date {DateTime.Now:yyyy-MM-dd HH:mm: ss}");
            sb.AppendLine(" * @note Auto-generated file");
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
            sb.AppendLine("#include <stdbool.h>");
            sb.AppendLine("#include <stddef.h>");
            sb.AppendLine();
            sb.AppendLine("/* Protocol */");
            sb.AppendLine("#define HW_START_BYTE  0xAA");
            sb.AppendLine("#define HW_END_BYTE    0x55");
            sb.AppendLine("#define HW_VERSION     0x01");
            sb.AppendLine();
            sb.AppendLine("/* Sensor IDs */");
            sb.AppendLine("typedef enum {");

            foreach (var (sensor, enumName, id) in sensors)
            {
                sb.AppendLine($"    {enumName} = 0x{id:X2},");
            }

            sb.AppendLine("    SENSOR_UNKNOWN = 0xFF");
            sb.AppendLine("} hw_sensor_id_t;");
            sb.AppendLine();
            sb.AppendLine($"#define HW_SENSOR_COUNT {sensors.Count}");
            sb.AppendLine();
            sb.AppendLine("/* Data structure */");
            sb.AppendLine("typedef struct {");
            sb.AppendLine("    hw_sensor_id_t id;");
            sb.AppendLine("    float value;");
            sb.AppendLine("    bool valid;");
            sb.AppendLine("} hw_sensor_t;");
            sb.AppendLine();
            sb.AppendLine("/* Storage */");
            sb.AppendLine("extern hw_sensor_t hw_sensors[HW_SENSOR_COUNT];");
            sb.AppendLine();
            sb.AppendLine("/* Functions */");
            sb.AppendLine("void hw_init(void);");
            sb.AppendLine("bool hw_parse(const uint8_t* data, size_t len);");
            sb.AppendLine("float hw_get(hw_sensor_id_t id);");
            sb.AppendLine("bool hw_valid(hw_sensor_id_t id);");
            sb.AppendLine("const char* hw_name(hw_sensor_id_t id);");
            sb.AppendLine("const char* hw_unit(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/* Getters */");

            foreach (var (sensor, enumName, id) in sensors)
            {
                var funcName = enumName.Replace("SENSOR_", "hw_get_").ToLower();
                sb.AppendLine($"#define {funcName}() hw_get({enumName})");
            }

            sb.AppendLine();
            sb.AppendLine("#ifdef __cplusplus");
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine($"#endif /* {baseName}_H */");

            return sb.ToString();
        }

        private string GenerateSource(List<(SensorInfo sensor, string enumName, byte id)> sensors, string headerFile, string baseName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("/**");
            sb.AppendLine($" * @file {Path.ChangeExtension(headerFile, ".c")}");
            sb.AppendLine(" * @brief Hardware Monitor - Implementation");
            sb.AppendLine($" * @date {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine($"#include \"{headerFile}\"");
            sb.AppendLine("#include <string.h>");
            sb.AppendLine();
            sb.AppendLine("hw_sensor_t hw_sensors[HW_SENSOR_COUNT];");
            sb.AppendLine();
            sb.AppendLine("void hw_init(void) {");
            sb.AppendLine("    memset(hw_sensors, 0, sizeof(hw_sensors));");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        hw_sensors[i].id = SENSOR_UNKNOWN;");
            sb.AppendLine("        hw_sensors[i].value = -999.0f;");
            sb.AppendLine("        hw_sensors[i].valid = false;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("bool hw_parse(const uint8_t* data, size_t len) {");
            sb.AppendLine("    if (! data || len < 6) return false;");
            sb.AppendLine("    if (data[0] != HW_START_BYTE) return false;");
            sb.AppendLine("    if (data[1] != HW_VERSION) return false;");
            sb.AppendLine();
            sb.AppendLine("    uint8_t count = data[2];");
            sb.AppendLine("    if (len < (size_t)(6 + count * 5)) return false;");
            sb.AppendLine("    if (data[len - 1] != HW_END_BYTE) return false;");
            sb.AppendLine();
            sb.AppendLine("    size_t offset = 3;");
            sb.AppendLine("    for (uint8_t i = 0; i < count && i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        hw_sensors[i]. id = (hw_sensor_id_t)data[offset];");
            sb.AppendLine("        union { float f; uint8_t b[4]; } conv;");
            sb.AppendLine("        conv.b[0] = data[offset + 1];");
            sb.AppendLine("        conv.b[1] = data[offset + 2];");
            sb.AppendLine("        conv.b[2] = data[offset + 3];");
            sb.AppendLine("        conv. b[3] = data[offset + 4];");
            sb.AppendLine("        hw_sensors[i]. value = conv.f;");
            sb.AppendLine("        hw_sensors[i].valid = true;");
            sb.AppendLine("        offset += 5;");
            sb.AppendLine("    }");
            sb.AppendLine("    return true;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("float hw_get(hw_sensor_id_t id) {");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (hw_sensors[i].id == id && hw_sensors[i].valid)");
            sb.AppendLine("            return hw_sensors[i].value;");
            sb.AppendLine("    }");
            sb.AppendLine("    return -999.0f;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("bool hw_valid(hw_sensor_id_t id) {");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (hw_sensors[i].id == id)");
            sb.AppendLine("            return hw_sensors[i].valid;");
            sb.AppendLine("    }");
            sb.AppendLine("    return false;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("const char* hw_name(hw_sensor_id_t id) {");
            sb.AppendLine("    switch (id) {");

            foreach (var (sensor, enumName, id) in sensors)
            {
                var name = sensor.Name.Replace("\"", "'");
                if (name.Length > 32) name = name.Substring(0, 29) + "...";
                sb.AppendLine($"        case {enumName}:  return \"{name}\";");
            }

            sb.AppendLine("        default: return \"Unknown\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("const char* hw_unit(hw_sensor_id_t id) {");
            sb.AppendLine("    switch (id) {");

            // Grupuj po typie sensora (jednostce)
            var grouped = sensors.GroupBy(s => s.sensor.Type);
            foreach (var group in grouped)
            {
                var unit = group.First().sensor.Unit ?? "";
                foreach (var (sensor, enumName, id) in group)
                {
                    sb.AppendLine($"        case {enumName}:");
                }
                sb.AppendLine($"            return \"{unit}\";");
            }

            sb.AppendLine("        default:  return \"\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }



        #endregion

        private string GenerateHeaderContent(List<(SensorInfo sensor, string enumName, string funcName, byte id)> sensors, string baseName, string fileName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("/**");
            sb.AppendLine($" * @file {fileName}");
            sb.AppendLine($" * @brief Hardware Monitor Sensor Definitions");
            sb.AppendLine($" * @date {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($" * @note Auto-generated - DO NOT EDIT MANUALLY");
            sb.AppendLine(" *");
            sb.AppendLine(" * Compatible with: ESP-IDF, Arduino, plain C");
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
            sb.AppendLine("#include <stdbool.h>");
            sb.AppendLine("#include <stddef.h>");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  PROTOCOL CONSTANTS                                                       */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("#define HW_PROTO_START_BYTE   0xAA");
            sb.AppendLine("#define HW_PROTO_END_BYTE     0x55");
            sb.AppendLine("#define HW_PROTO_VERSION      0x01");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  SENSOR IDENTIFIERS                                                       */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("typedef enum {");

            // Group by category for readability
            var grouped = sensors.GroupBy(s => GetCategory(s.sensor)).OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                sb.AppendLine($"    /* ---- {group.Key} Sensors ---- */");
                foreach (var (sensor, enumName, funcName, id) in group)
                {
                    var comment = $"{sensor.Hardware} / {sensor.Name}";
                    if (comment.Length > 45) comment = comment.Substring(0, 42) + "...";
                    sb.AppendLine($"    {enumName,-45} = 0x{id:X2},  /* {comment} */");
                }
                sb.AppendLine();
            }

            sb.AppendLine("    SENSOR_ID_UNKNOWN = 0xFF");
            sb.AppendLine("} hw_sensor_id_t;");
            sb.AppendLine();
            sb.AppendLine($"#define HW_SENSOR_COUNT  {sensors.Count}");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  DATA STRUCTURES                                                          */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Sensor data container");
            sb.AppendLine(" */");
            sb.AppendLine("typedef struct {");
            sb.AppendLine("    hw_sensor_id_t id;        /**< Sensor identifier */");
            sb.AppendLine("    float          value;     /**< Current value */");
            sb.AppendLine("    bool           valid;     /**< Data validity flag */");
            sb.AppendLine("    uint32_t       timestamp; /**< Last update time (ms) */");
            sb.AppendLine("} hw_sensor_t;");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  GLOBAL DATA                                                              */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("extern hw_sensor_t hw_sensors[HW_SENSOR_COUNT];");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  FUNCTION PROTOTYPES                                                      */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Initialize all sensors to default state");
            sb.AppendLine(" */");
            sb.AppendLine("void hw_sensors_init(void);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Parse incoming binary packet");
            sb.AppendLine(" * @param data Pointer to packet buffer");
            sb.AppendLine(" * @param len  Length of packet");
            sb.AppendLine(" * @return true if packet was valid and parsed");
            sb.AppendLine(" */");
            sb.AppendLine("bool hw_sensors_parse(const uint8_t* data, size_t len);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Get sensor value by ID");
            sb.AppendLine(" * @param id Sensor identifier");
            sb.AppendLine(" * @return Sensor value or -999. 0f if invalid/not found");
            sb.AppendLine(" */");
            sb.AppendLine("float hw_sensors_get_value(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Check if sensor has valid data");
            sb.AppendLine(" * @param id Sensor identifier");
            sb.AppendLine(" * @return true if sensor data is valid");
            sb.AppendLine(" */");
            sb.AppendLine("bool hw_sensors_is_valid(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Get sensor name string");
            sb.AppendLine(" * @param id Sensor identifier");
            sb.AppendLine(" * @return Sensor name or \"Unknown\"");
            sb.AppendLine(" */");
            sb.AppendLine("const char* hw_sensors_get_name(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Get sensor unit string");
            sb.AppendLine(" * @param id Sensor identifier");
            sb.AppendLine(" * @return Unit string (°C, %, MHz, etc.)");
            sb.AppendLine(" */");
            sb.AppendLine("const char* hw_sensors_get_unit(hw_sensor_id_t id);");
            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine(" * @brief Invalidate all sensor data (call on timeout)");
            sb.AppendLine(" */");
            sb.AppendLine("void hw_sensors_invalidate_all(void);");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  CONVENIENCE GETTER FUNCTIONS                                             */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();

            foreach (var (sensor, enumName, funcName, id) in sensors)
            {
                sb.AppendLine($"/** @brief Get {sensor.Name} value */");
                sb.AppendLine($"static inline float {funcName}(void) {{ return hw_sensors_get_value({enumName}); }}");
                sb.AppendLine();
            }

            sb.AppendLine("#ifdef __cplusplus");
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine($"#endif /* {baseName}_H */");

            return sb.ToString();
        }

        private string GenerateSourceContent(List<(SensorInfo sensor, string enumName, string funcName, byte id)> sensors, string headerFile, string baseName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("/**");
            sb.AppendLine($" * @file {Path.ChangeExtension(headerFile, ".c")}");
            sb.AppendLine($" * @brief Hardware Monitor Sensor Implementation");
            sb.AppendLine($" * @date {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($" * @note Auto-generated - DO NOT EDIT MANUALLY");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine($"#include \"{headerFile}\"");
            sb.AppendLine("#include <string.h>");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  GLOBAL DATA                                                              */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("hw_sensor_t hw_sensors[HW_SENSOR_COUNT];");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  INITIALIZATION                                                           */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("void hw_sensors_init(void)");
            sb.AppendLine("{");
            sb.AppendLine("    memset(hw_sensors, 0, sizeof(hw_sensors));");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        hw_sensors[i].id = SENSOR_ID_UNKNOWN;");
            sb.AppendLine("        hw_sensors[i].value = -999.0f;");
            sb.AppendLine("        hw_sensors[i].valid = false;");
            sb.AppendLine("        hw_sensors[i].timestamp = 0;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  PACKET PARSER                                                            */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("bool hw_sensors_parse(const uint8_t* data, size_t len)");
            sb.AppendLine("{");
            sb.AppendLine("    /* Validate minimum length */");
            sb.AppendLine("    if (! data || len < 6) {");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /* Check start byte */");
            sb.AppendLine("    if (data[0] != HW_PROTO_START_BYTE) {");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /* Check protocol version */");
            sb.AppendLine("    if (data[1] != HW_PROTO_VERSION) {");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /* Get sensor count */");
            sb.AppendLine("    uint8_t count = data[2];");
            sb.AppendLine("    size_t expected_len = 3 + (count * 5) + 3;  /* header + data + crc + end */");
            sb.AppendLine();
            sb.AppendLine("    if (len < expected_len) {");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /* Check end byte */");
            sb.AppendLine("    if (data[expected_len - 1] != HW_PROTO_END_BYTE) {");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /* TODO: Verify CRC16 at data[expected_len-3] and data[expected_len-2] */");
            sb.AppendLine();
            sb.AppendLine("    /* Parse sensor data */");
            sb.AppendLine("    size_t offset = 3;");
            sb.AppendLine("    for (uint8_t i = 0; i < count && i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        hw_sensors[i]. id = (hw_sensor_id_t)data[offset];");
            sb.AppendLine();
            sb.AppendLine("        /* Parse IEEE 754 float (little-endian) */");
            sb.AppendLine("        union {");
            sb.AppendLine("            float f;");
            sb.AppendLine("            uint8_t b[4];");
            sb.AppendLine("        } converter;");
            sb.AppendLine();
            sb.AppendLine("        converter.b[0] = data[offset + 1];");
            sb.AppendLine("        converter. b[1] = data[offset + 2];");
            sb.AppendLine("        converter.b[2] = data[offset + 3];");
            sb.AppendLine("        converter.b[3] = data[offset + 4];");
            sb.AppendLine();
            sb.AppendLine("        hw_sensors[i].value = converter.f;");
            sb.AppendLine("        hw_sensors[i].valid = true;");
            sb.AppendLine("        /* hw_sensors[i]. timestamp = get_timestamp_ms(); */");
            sb.AppendLine();
            sb.AppendLine("        offset += 5;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    return true;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  GETTERS                                                                  */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("float hw_sensors_get_value(hw_sensor_id_t id)");
            sb.AppendLine("{");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (hw_sensors[i].id == id && hw_sensors[i].valid) {");
            sb.AppendLine("            return hw_sensors[i].value;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return -999.0f;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("bool hw_sensors_is_valid(hw_sensor_id_t id)");
            sb.AppendLine("{");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (hw_sensors[i]. id == id) {");
            sb.AppendLine("            return hw_sensors[i].valid;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return false;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("void hw_sensors_invalidate_all(void)");
            sb.AppendLine("{");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        hw_sensors[i].valid = false;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  METADATA                                                                 */");
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine();
            sb.AppendLine("const char* hw_sensors_get_name(hw_sensor_id_t id)");
            sb.AppendLine("{");
            sb.AppendLine("    switch (id) {");

            foreach (var (sensor, enumName, funcName, id) in sensors)
            {
                var name = sensor.Name.Replace("\"", "\\\"");
                if (name.Length > 30) name = name.Substring(0, 27) + "...";
                sb.AppendLine($"        case {enumName}:  return \"{name}\";");
            }

            sb.AppendLine("        default: return \"Unknown\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("const char* hw_sensors_get_unit(hw_sensor_id_t id)");
            sb.AppendLine("{");
            sb.AppendLine("    switch (id) {");

            // Group by unit
            var byUnit = sensors.GroupBy(s => s.sensor.Unit ?? "");
            foreach (var group in byUnit)
            {
                foreach (var (sensor, enumName, funcName, id) in group)
                {
                    sb.AppendLine($"        case {enumName}:");
                }
                var unit = group.Key.Replace("\"", "\\\"");
                sb.AppendLine($"            return \"{unit}\";");
                sb.AppendLine();
            }

            sb.AppendLine("        default: return \"\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("/*===========================================================================*/");
            sb.AppendLine("/*  END OF FILE                                                              */");
            sb.AppendLine("/*===========================================================================*/");

            return sb.ToString();
        }

        #endregion
    }
}