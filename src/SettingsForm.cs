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

        private Button _savePacketBtn, _copyPacketBtn;
        private TextBox _packetHexBox;
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

            //BuildSensorIdMap();
            PopulateFilters();
            FilterList();
            _loadLbl.Visible = false;
            _list.Visible = true;
            _init = true;
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
        private ushort GetSensorId(SensorInfo s)
        {
            if (s == null) return 0xFFFF;
            return SensorIdMapper.Instance.GetOrAssignId(s.Id, s);
        }

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
        private Panel BuildButtonsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 12, 0, 0)
            };

            var exitBtn = new Button
            {
                Text = "🚪 Exit",
                Width = 110,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(149, 165, 166),
                ForeColor = Color.White,
                //Margin = new Padding(10, 0, 0, 0)
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
        private GroupBox BuildStatusGroup()
        {
            var grp = new GroupBox
            {
                Text = "📊 Status",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                //Padding = new Padding(15, 8, 15, 8)
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
                //Padding = new Padding(15, 8, 15, 8)
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
                Margin = new Padding(0, 0, 0, 5),
                // Padding = new Padding(15, 8, 15, 8)
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

            var exportBtn = new Button
            {
                Text = "📄 Export .h",
                Width = 100,
                Height = 30,
                Location = new Point(150, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White
            };
            exportBtn.FlatAppearance.BorderSize = 0;
            exportBtn.Click += ExportSensorStruct;
            btnPanel.Controls.Add(exportBtn);

            var mapBtn = new Button
            {
                Text = "🗺 Map",
                Width = 65,
                Height = 30,
                Location = new Point(255, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White
            };
            mapBtn.FlatAppearance.BorderSize = 0;
            mapBtn.Click += ShowSensorMap;
            btnPanel.Controls.Add(mapBtn);

            _cntLbl = new Label { AutoSize = true, Location = new Point(330, 12), ForeColor = Color.Gray };
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

            _sizeBar = new ProgressBar { Width = 300, Height = 16, Maximum = 1500, Location = new Point(0, y) };
            panel.Controls.Add(_sizeBar);
            y += 26;

            _bwLbl = new Label { Text = "0 B/s", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.FromArgb(155, 89, 182), AutoSize = true, Location = new Point(0, y) };
            panel.Controls.Add(_bwLbl);
            y += 35;

            // Buttons row
            var btnFlow = new FlowLayoutPanel { Location = new Point(0, y), Width = 320, Height = 35, FlowDirection = FlowDirection.LeftToRight };

            _helpBtn = new Button
            {
                Text = "❓ Protocol",
                Width = 95,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 196, 15),
                ForeColor = Color.Black,
                Margin = new Padding(0, 0, 5, 0)
            };
            _helpBtn.FlatAppearance.BorderSize = 0;
            _helpBtn.Click += ShowProtocolHelp;
            btnFlow.Controls.Add(_helpBtn);

            _savePacketBtn = new Button
            {
                Text = "💾 Save . bin",
                Width = 95,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                Margin = new Padding(0, 0, 5, 0)
            };
            _savePacketBtn.FlatAppearance.BorderSize = 0;
            _savePacketBtn.Click += SaveExamplePacket;
            btnFlow.Controls.Add(_savePacketBtn);

            _copyPacketBtn = new Button
            {
                Text = "📋 Copy",
                Width = 75,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                Margin = new Padding(0, 0, 0, 0)
            };
            _copyPacketBtn.FlatAppearance.BorderSize = 0;
            _copyPacketBtn.Click += CopyPacketToClipboard;
            btnFlow.Controls.Add(_copyPacketBtn);

            panel.Controls.Add(btnFlow);
            y += 40;

            // Packet HEX display
            panel.Controls.Add(new Label { Text = "Example Packet (HEX):", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 20;

            _packetHexBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font("Consolas", 8.5f),
                BackColor = Color.FromArgb(30, 30, 35),
                ForeColor = Color.FromArgb(0, 255, 128),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(0, y),
                Size = new Size(310, 60)
            };
            panel.Controls.Add(_packetHexBox);
            y += 70;

            // Live Preview
            panel.Controls.Add(new Label { Text = "Live Preview:", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) });
            y += 20;

            _previewBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(20, 20, 25),
                ForeColor = Color.FromArgb(46, 204, 113),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(0, y),
                Size = new Size(310, 140)
            };
            panel.Controls.Add(_previewBox);

            panel.Resize += (s, e) =>
            {
                int w = Math.Max(200, panel.Width - 30);
                _sizeBar.Width = w;
                _packetHexBox.Width = w;
                _previewBox.Width = w;
                _previewBox.Height = Math.Max(100, panel.Height - y - 20);
            };

            grp.Controls.Add(panel);
            parent.Controls.Add(grp);
        }
        private void ShowProtocolHelp(object s, EventArgs e)
        {
            var helpForm = new Form
            {
                Text = "Protocol v2.0 Documentation",
                Size = new Size(850, 750),
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
                    HARDWARE MONITOR PROTOCOL v2.0
================================================================================

⚡ KEY CHANGES FROM v1:
   • Sensor IDs are now 16-bit (2 bytes) instead of 8-bit
   • IDs cannot contain 0xAA (START) or 0x55 (END) bytes
   • 6 bytes per sensor instead of 5
   • Protocol version byte is now 0x02

================================================================================
1. BINARY PROTOCOL (Recommended - most efficient)
================================================================================

Packet structure:
┌───────┬─────────┬───────┬────────────────────────────┬───────┬───────┐
│ START │ VERSION │ COUNT │        SENSOR DATA         │ CRC16 │  END  │
│ 0xAA  │  0x02   │  1B   │      N × 6 bytes           │  2B   │ 0x55  │
└───────┴─────────┴───────┴────────────────────────────┴───────┴───────┘

Each sensor (6 bytes):
┌──────────┬──────────┬─────────────────────────────────────┐
│ ID_HIGH  │ ID_LOW   │     VALUE (4 bytes, little-endian)  │
│   1B     │   1B     │              float32                │
└──────────┴──────────┴─────────────────────────────────────┘

Example (2 sensors:  CPU=65.5°C @ ID 0x0001, GPU=70.0°C @ ID 0x0010):

AA 02 02 00 01 00 00 83 42 00 10 00 00 8C 42 [CRC] 55
│  │  │  │  │  └────────┘  │  │  └────────┘        │
│  │  │  │  │  CPU=65.5°C  │  │  GPU=70.0°C        End
│  │  │  │  ID_LOW=0x01    │  ID_LOW=0x10
│  │  │  ID_HIGH=0x00      ID_HIGH=0x00
│  │  Count=2
│  Version=0x02 (Protocol v2)
Start=0xAA


================================================================================
2. SENSOR IDs (16-bit)
================================================================================

Standard Ranges:
  CPU:          0x0001 - 0x000F
  GPU:          0x0010 - 0x001F
  RAM:         0x0020 - 0x002F
  Disk:        0x0030 - 0x003F
  Network:     0x0040 - 0x004F
  Motherboard: 0x0050 - 0x005F
  Battery:     0x0060 - 0x006F
  Custom:      0x0080 - 0xFFFD

⚠️  RESERVED/INVALID IDs: 
  Any ID containing 0xAA or 0x55 in either byte is INVALID! 
  
  Invalid examples:  0x00AA, 0xAA00, 0x0055, 0x5500, 0xAA55, 0x55AA
  Valid examples:    0x0001, 0x0010, 0x00FF, 0x0100, 0x1234

The application automatically skips these reserved values when assigning IDs. 


================================================================================
3. TEXT PROTOCOL (For debugging)
================================================================================

$S
0001: 65.5
0010:70.0
0002:45.2
$E: XX

Format:  <ID_HEX_4DIGITS>:<VALUE>


================================================================================
4. ESP32/Arduino EXAMPLE (Protocol v2)
================================================================================

#include ""HWMonitor.h""

HWMonitor monitor;

void setup() {
    Serial.begin(115200);
    Serial2.begin(115200);  // From PC
    monitor.begin();
    
    // Optional callback
    monitor.onSensor([](uint16_t id, float value) {
        Serial.printf(""Sensor 0x%04X = %.1f\n"", id, value);
    });
}

void loop() {
    if (monitor.update(Serial2)) {
        // New packet received
        float cpu = monitor.getCpuTemp();
        float gpu = monitor. getGpuTemp();
        
        Serial.printf(""CPU: %.1f°C, GPU: %.1f°C\n"", cpu, gpu);
        Serial.printf(""Packets OK: %d, Errors: %d\n"", 
            monitor.packetsOK, monitor.packetsError);
    }
    
    // Check for stale data
    if (monitor.isStale(3000)) {
        Serial. println(""No data from PC for 3 seconds!"");
    }
}


================================================================================
5. PARSER STATE MACHINE (v2)
================================================================================

States:
  IDLE      → Wait for START (0xAA)
  VERSION   → Read version byte (expect 0x02)
  COUNT     → Read sensor count
  ID_HIGH   → Read high byte of 16-bit sensor ID
  ID_LOW    → Read low byte of 16-bit sensor ID
  VALUE     → Read 4 bytes of float value
  CRC_LOW   → Read CRC16 low byte
  CRC_HIGH  → Read CRC16 high byte
  END       → Verify END byte (0x55)


================================================================================
6. CRC-16 CALCULATION
================================================================================

CRC-16 Modbus (polynomial 0xA001):
  - Initial value: 0xFFFF
  - Calculate over:  VERSION + COUNT + all sensor data
  - Exclude: START and END bytes

C implementation:
uint16_t crc16(uint8_t* data, size_t len) {
    uint16_t crc = 0xFFFF;
    for (size_t i = 0; i < len; i++) {
        crc ^= data[i];
        for (int j = 0; j < 8; j++) {
            if (crc & 0x0001)
                crc = (crc >> 1) ^ 0xA001;
            else
                crc >>= 1;
        }
    }
    return crc;
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
                ProtocolMode.Binary => 6 + total * 6,
                ProtocolMode.Text => 12 + total * 14,
                _ => 50 + total * 90
            };

            _sizeLbl.Text = size < 1024 ? $"{size} B" : $"{size / 1024.0:0.0} KB";
            _sizeLbl.ForeColor = proto switch
            {
                ProtocolMode.Binary => Color.FromArgb(46, 204, 113),
                ProtocolMode.Text => Color.FromArgb(241, 196, 15),
                _ => Color.FromArgb(231, 76, 60)
            };
            _sizeBar.Value = Math.Min(size, _sizeBar.Maximum);

            var pps = 1000.0 / Math.Max(1, (double)_intervalNum.Value);
            _bwLbl.Text = $"{size * pps:0} B/s ({pps:0.0} pkt/s)";

            UpdatePacketHexDisplay();  // <-- DODAJ TO
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

                    sb.AppendLine($"  [{id:X4}] {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}  = {simVal:F1} ({s.Name})");
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

        #region Sensor Map & Export
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

            var mapper = SensorIdMapper.Instance;

            foreach (var s in q.Take(250))
            {
                // Pobierz ID z centralnej mapy
                ushort sensorId = mapper.GetOrAssignId(s.Id, s);

                var it = new ListViewItem { Checked = _cfg.Config.SelectedSensors.Contains(s.Id) };
                it.SubItems.Add(s.Hardware);
                it.SubItems.Add(s.Name);
                it.SubItems.Add(s.Type);
                it.SubItems.Add(FormatValue(s.Value, s.Unit));
                it.SubItems.Add($"0x{sensorId:X4}");  // Poprawne ID z mapy
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

                var mapper = SensorIdMapper.Instance;

                _list.BeginUpdate();
                foreach (ListViewItem it in _list.Items)
                {
                    var s = it.Tag as SensorInfo;
                    if (s == null) continue;

                    var upd = fresh.Find(x => x.Id == s.Id);
                    if (upd != null)
                    {
                        it.SubItems[4].Text = FormatValue(upd.Value, upd.Unit);
                        // Aktualizuj też ID (na wypadek gdyby się zmieniło)
                        ushort sensorId = mapper.GetOrAssignId(upd.Id, upd);
                        it.SubItems[5].Text = $"0x{sensorId:X4}";
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
        private void ExportSensorStruct(object sender, EventArgs e)
        {
            var selectedIds = _list.CheckedItems.Cast<ListViewItem>()
                .Select(i => (i.Tag as SensorInfo)?.Id)
                .Where(x => x != null)
                .ToList();

            if (selectedIds.Count == 0)
            {
                MessageBox.Show("Select at least one sensor.", "No Sensors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Upewnij się że wszystkie są w mapie
            var mapper = SensorIdMapper.Instance;
            foreach (var item in _list.CheckedItems.Cast<ListViewItem>())
            {
                var sensor = item.Tag as SensorInfo;
                if (sensor != null)
                {
                    mapper.GetOrAssignId(sensor.Id, sensor);
                }
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "C Header|*. h",
                FileName = "hw_sensors.h",
                Title = "Export Sensor Map"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    mapper.ExportStruct(dlg.FileName, selectedIds);

                    MessageBox.Show(
                        $"Exported {selectedIds.Count} sensors to:\n\n{dlg.FileName}\n\n" +
                        "Include this file in your ESP32 project.",
                        "Export Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ShowSensorMap(object sender, EventArgs e)
        {
            var mapper = SensorIdMapper.Instance;

            var selectedIds = _list.CheckedItems.Cast<ListViewItem>()
                .Select(i => (i.Tag as SensorInfo)?.Id)
                .Where(x => x != null)
                .ToList();

            var form = new Form
            {
                Text = "Sensor ID Map",
                Size = new Size(700, 550),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(30, 30, 35),
                Font = new Font("Consolas", 10)
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                BackColor = Color.FromArgb(30, 30, 35),
                ForeColor = Color.FromArgb(0, 255, 128),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10),
                Text = mapper.GeneratePreview(selectedIds.Count > 0 ? selectedIds : null)
            };

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 50 };

            var resetBtn = new Button
            {
                Text = "🗑 Reset Map",
                Width = 120,
                Height = 35,
                Location = new Point(10, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White
            };
            resetBtn.FlatAppearance.BorderSize = 0;
            resetBtn.Click += (s, ev) =>
            {
                if (MessageBox.Show(
                    "Reset entire sensor map?\n\nAll sensor IDs will be reassigned on next discovery.",
                    "Confirm Reset",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    mapper.Reset();
                    textBox.Text = "(map reset - sensors will be assigned new IDs)";
                }
            };
            btnPanel.Controls.Add(resetBtn);

            var cleanupBtn = new Button
            {
                Text = "🧹 Cleanup Old",
                Width = 130,
                Height = 35,
                Location = new Point(140, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 196, 15),
                ForeColor = Color.Black
            };
            cleanupBtn.FlatAppearance.BorderSize = 0;
            cleanupBtn.Click += (s, ev) =>
            {
                int removed = mapper.Cleanup(30);
                MessageBox.Show($"Removed {removed} sensors not seen in 30 days.", "Cleanup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                textBox.Text = mapper.GeneratePreview(selectedIds.Count > 0 ? selectedIds : null);
            };
            btnPanel.Controls.Add(cleanupBtn);

            var refreshBtn = new Button
            {
                Text = "🔄 Refresh",
                Width = 100,
                Height = 35,
                Location = new Point(280, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White
            };
            refreshBtn.FlatAppearance.BorderSize = 0;
            refreshBtn.Click += (s, ev) =>
            {
                textBox.Text = mapper.GeneratePreview(selectedIds.Count > 0 ? selectedIds : null);
            };
            btnPanel.Controls.Add(refreshBtn);

            var infoLabel = new Label
            {
                Text = $"Total mapped: {mapper.Count} | File: sensor_map.json",
                AutoSize = true,
                Location = new Point(400, 16),
                ForeColor = Color.Gray
            };
            btnPanel.Controls.Add(infoLabel);

            form.Controls.Add(textBox);
            form.Controls.Add(btnPanel);
            form.ShowDialog(this);
        }

        /// <summary>
        /// Pobiera ID sensora z centralnej mapy
        /// </summary>

        #endregion


        /// <summary>
        /// Buduje przykładową paczkę binarną z aktualnie wybranych sensorów
        /// </summary>
        private byte[] BuildExamplePacket()
        {
            var selected = _list.CheckedItems.Cast<ListViewItem>()
                .Select(i => i.Tag as SensorInfo)
                .Where(x => x != null)
                .Take(250)
                .ToList();

            if (selected.Count == 0)
                return Array.Empty<byte>();

            int count = selected.Count;
            int packetSize = 3 + (count * 6) + 3; // START + VER + COUNT + DATA(6 per sensor) + CRC + END
            byte[] packet = new byte[packetSize];

            int idx = 0;

            // Header
            packet[idx++] = 0xAA;  // START
            packet[idx++] = 0x02;  // VERSION 2
            packet[idx++] = (byte)count;

            // Sensor data - 6 bytes each
            foreach (var sensor in selected)
            {
                ushort sensorId = GetSensorId(sensor);
                float value = sensor.Value ?? 0f;

                // 16-bit ID (big-endian)
                packet[idx++] = (byte)(sensorId >> 8);
                packet[idx++] = (byte)(sensorId & 0xFF);

                // Float to bytes (little-endian)
                byte[] valueBytes = BitConverter.GetBytes(value);
                packet[idx++] = valueBytes[0];
                packet[idx++] = valueBytes[1];
                packet[idx++] = valueBytes[2];
                packet[idx++] = valueBytes[3];
            }

            // CRC16
            ushort crc = CalculateCRC16(packet, 1, 2 + count * 6);
            packet[idx++] = (byte)(crc & 0xFF);
            packet[idx++] = (byte)(crc >> 8);

            // END
            packet[idx++] = 0x55;

            return packet;
        }

        private ushort CalculateCRC16(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;

            for (int i = offset; i < offset + length && i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc = (ushort)(crc >> 1);
                }
            }

            return crc;
        }

        /// <summary>
        /// Aktualizuje wyświetlanie HEX paczki
        /// </summary>
        private void UpdatePacketHexDisplay()
        {
            var proto = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);

            if (proto == ProtocolMode.Binary)
            {
                byte[] packet = BuildExamplePacket();
                if (packet.Length == 0)
                {
                    _packetHexBox.Text = "(select sensors to see packet)";
                    return;
                }

                var sb = new StringBuilder();
                for (int i = 0; i < packet.Length; i++)
                {
                    sb.Append(packet[i].ToString("X2"));
                    sb.Append(" ");
                    if ((i + 1) % 16 == 0) sb.AppendLine();
                }
                _packetHexBox.Text = sb.ToString().Trim();
            }
            else if (proto == ProtocolMode.Text)
            {
                var selected = _list.CheckedItems.Cast<ListViewItem>()
                    .Select(i => i.Tag as SensorInfo)
                    .Where(x => x != null)
                    .Take(10)
                    .ToList();

                if (selected.Count == 0)
                {
                    _packetHexBox.Text = "(select sensors)";
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("$START");
                foreach (var s in selected)
                {
                    sb.AppendLine($"{GetSensorId(s):X2}:{s.Value ?? 0:F1}");
                }
                if (GetTotalSelected() > 10) sb.AppendLine($"... +{GetTotalSelected() - 10} more");
                sb.AppendLine("$END: XX");
                _packetHexBox.Text = sb.ToString();
            }
            else
            {
                // JSON preview
                var selected = _list.CheckedItems.Cast<ListViewItem>()
                    .Select(i => i.Tag as SensorInfo)
                    .Where(x => x != null)
                    .Take(3)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine("{\"sensors\":[");
                foreach (var s in selected)
                {
                    sb.AppendLine($"  {{\"id\": \"0x{GetSensorId(s):X2}\",\"v\":{s.Value ?? 0:F1}}},");
                }
                if (GetTotalSelected() > 3) sb.AppendLine($"  ... +{GetTotalSelected() - 3} more");
                sb.AppendLine("]}");
                _packetHexBox.Text = sb.ToString();
            }
        }

        /// <summary>
        /// Zapisuje przykładową paczkę do pliku
        /// </summary>
        private void SaveExamplePacket(object sender, EventArgs e)
        {
            var proto = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);

            if (proto == ProtocolMode.Binary)
            {
                byte[] packet = BuildExamplePacket();
                if (packet.Length == 0)
                {
                    MessageBox.Show("Select at least one sensor first.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using var dlg = new SaveFileDialog
                {
                    Filter = "Binary file|*.bin|All files|*.*",
                    FileName = $"hw_packet_{DateTime.Now:yyyyMMdd_HHmmss}.bin",
                    Title = "Save Example Packet"
                };

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllBytes(dlg.FileName, packet);

                    // Zapisz też plik . txt z opisem
                    var txtFile = Path.ChangeExtension(dlg.FileName, ".txt");
                    var sb = new StringBuilder();
                    sb.AppendLine("Hardware Monitor - Example Packet");
                    sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"Sensors: {_list.CheckedItems.Count}");
                    sb.AppendLine($"Size: {packet.Length} bytes");
                    sb.AppendLine();
                    sb.AppendLine("=== PACKET STRUCTURE ===");
                    sb.AppendLine($"START:    0x{packet[0]:X2}");
                    sb.AppendLine($"VERSION: 0x{packet[1]: X2}");
                    sb.AppendLine($"COUNT:   {packet[2]} (0x{packet[2]:X2})");
                    sb.AppendLine();
                    sb.AppendLine("=== SENSOR DATA ===");

                    int offset = 3;
                    int count = packet[2];
                    for (int i = 0; i < count && offset + 5 <= packet.Length - 3; i++)
                    {
                        byte id = packet[offset];
                        float value = BitConverter.ToSingle(packet, offset + 1);
                        var sensor = _list.CheckedItems.Cast<ListViewItem>()
                            .Select(x => x.Tag as SensorInfo)
                            .FirstOrDefault(s => s != null && GetSensorId(s) == id);

                        sb.AppendLine($"[{i:D3}] ID=0x{id: X2} Value={value:F2} ({sensor?.Name ?? "Unknown"})");
                        offset += 5;
                    }

                    sb.AppendLine();
                    sb.AppendLine($"CRC16:   0x{packet[packet.Length - 3]:X2}{packet[packet.Length - 2]:X2}");
                    sb.AppendLine($"END:     0x{packet[packet.Length - 1]:X2}");
                    sb.AppendLine();
                    sb.AppendLine("=== RAW HEX ===");
                    for (int i = 0; i < packet.Length; i++)
                    {
                        sb.Append(packet[i].ToString("X2") + " ");
                        if ((i + 1) % 16 == 0) sb.AppendLine();
                    }

                    File.WriteAllText(txtFile, sb.ToString());

                    MessageBox.Show(
                        $"Saved:\n\n" +
                        $"• {Path.GetFileName(dlg.FileName)} ({packet.Length} bytes)\n" +
                        $"• {Path.GetFileName(txtFile)} (description)\n\n" +
                        $"Location: {Path.GetDirectoryName(dlg.FileName)}",
                        "Packet Saved",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            else
            {
                // Text/JSON - zapisz jako . txt
                using var dlg = new SaveFileDialog
                {
                    Filter = proto == ProtocolMode.Text ? "Text file|*.txt" : "JSON file|*.json",
                    FileName = $"hw_packet_{DateTime.Now:yyyyMMdd_HHmmss}.{(proto == ProtocolMode.Text ? "txt" : "json")}",
                    Title = "Save Example Packet"
                };

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    string content = BuildTextOrJsonPacket(proto);
                    File.WriteAllText(dlg.FileName, content);

                    MessageBox.Show(
                        $"Saved:  {Path.GetFileName(dlg.FileName)}",
                        "Packet Saved",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }

        private string BuildTextOrJsonPacket(ProtocolMode mode)
        {
            var selected = _list.CheckedItems.Cast<ListViewItem>()
                .Select(i => i.Tag as SensorInfo)
                .Where(x => x != null)
                .Take(250)
                .ToList();

            if (mode == ProtocolMode.Text)
            {
                var sb = new StringBuilder();
                sb.AppendLine("$START");
                foreach (var s in selected)
                {
                    sb.AppendLine($"{GetSensorId(s):X2}:{s.Value ?? 0:F1}");
                }
                sb.AppendLine("$END:XX");
                return sb.ToString();
            }
            else
            {
                // JSON
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"timestamp\": \"{DateTime.UtcNow: o}\",");
                sb.AppendLine("  \"sensors\":  [");

                for (int i = 0; i < selected.Count; i++)
                {
                    var s = selected[i];
                    var comma = i < selected.Count - 1 ? "," : "";
                    sb.AppendLine($"    {{\"id\": \"0x{GetSensorId(s):X2}\", \"name\": \"{s.Name}\", \"value\": {s.Value ?? 0:F1}, \"unit\": \"{s.Unit}\"}}{comma}");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");
                return sb.ToString();
            }
        }

        /// <summary>
        /// Kopiuje paczkę do schowka
        /// </summary>
        private void CopyPacketToClipboard(object sender, EventArgs e)
        {
            var proto = (ProtocolMode)Math.Max(0, _protoCb.SelectedIndex);

            string clipboardText;

            if (proto == ProtocolMode.Binary)
            {
                byte[] packet = BuildExamplePacket();
                if (packet.Length == 0)
                {
                    MessageBox.Show("Select at least one sensor first.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Format jako C array
                var sb = new StringBuilder();
                sb.AppendLine("// Hardware Monitor Example Packet");
                sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"// Sensors:  {_list.CheckedItems.Count}, Size: {packet.Length} bytes");
                sb.AppendLine();
                sb.AppendLine($"const uint8_t hw_example_packet[{packet.Length}] = {{");

                for (int i = 0; i < packet.Length; i++)
                {
                    if (i % 16 == 0) sb.Append("    ");
                    sb.Append($"0x{packet[i]: X2}");
                    if (i < packet.Length - 1) sb.Append(", ");
                    if ((i + 1) % 16 == 0 || i == packet.Length - 1) sb.AppendLine();
                }

                sb.AppendLine("};");
                sb.AppendLine();
                sb.AppendLine("// HEX string:");
                sb.Append("// ");
                for (int i = 0; i < Math.Min(packet.Length, 32); i++)
                {
                    sb.Append(packet[i].ToString("X2") + " ");
                }
                if (packet.Length > 32) sb.Append($"... (+{packet.Length - 32} bytes)");

                clipboardText = sb.ToString();
            }
            else
            {
                clipboardText = BuildTextOrJsonPacket(proto);
            }

            try
            {
                Clipboard.SetText(clipboardText);

                // Animacja przycisku
                var originalText = _copyPacketBtn.Text;
                var originalColor = _copyPacketBtn.BackColor;
                _copyPacketBtn.Text = "✓ Copied!";
                _copyPacketBtn.BackColor = Color.FromArgb(46, 204, 113);

                var timer = new System.Windows.Forms.Timer { Interval = 1500 };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    if (!_closing)
                    {
                        _copyPacketBtn.Text = originalText;
                        _copyPacketBtn.BackColor = originalColor;
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy:  {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}