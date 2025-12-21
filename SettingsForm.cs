using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HardwareMonitorTray
{
    public class SettingsForm : Form
    {
        private ConfigManager _configManager;
        private HardwareMonitorService _monitorService;
        private bool _isInitialized = false;

        private ComboBox _comPortCombo;
        private ComboBox _baudRateCombo;
        private NumericUpDown _intervalNumeric;
        private CheckBox _autoStartCheck;
        private CheckBox _startWithWindowsCheck;
        private CheckedListBox _sensorsListBox;
        private TextBox _searchTextBox;
        private ComboBox _typeFilterCombo;
        private ComboBox _hardwareFilterCombo;
        private Button _refreshButton;
        private Button _saveButton;
        private Button _cancelButton;
        private Button _selectAllButton;
        private Button _deselectAllButton;
        private Button _clearSearchButton;
        private Label _sensorCountLabel;

        public SettingsForm(ConfigManager configManager, HardwareMonitorService monitorService)
        {
            _configManager = configManager;
            _monitorService = monitorService;

            InitializeComponents();
            LoadCurrentSettings();

            _isInitialized = true;
        }

        private void InitializeComponents()
        {
            this.Text = "Hardware Monitor Settings";
            this.Size = new Size(650, 650);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                RowCount = 8,
                ColumnCount = 2
            };

            // COM Port
            mainPanel.Controls.Add(new Label { Text = "COM Port:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var comPanel = new FlowLayoutPanel { AutoSize = true };
            _comPortCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _refreshButton = new Button { Text = "Refresh", Width = 70 };
            _refreshButton.Click += (s, e) => RefreshComPorts();
            comPanel.Controls.Add(_comPortCombo);
            comPanel.Controls.Add(_refreshButton);
            mainPanel.Controls.Add(comPanel, 1, 0);

            // Baud Rate
            mainPanel.Controls.Add(new Label { Text = "Baud Rate:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            _baudRateCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _baudRateCombo.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 });
            mainPanel.Controls.Add(_baudRateCombo, 1, 1);

            // Interval
            mainPanel.Controls.Add(new Label { Text = "Send Interval (ms):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            _intervalNumeric = new NumericUpDown { Minimum = 100, Maximum = 60000, Width = 100 };
            mainPanel.Controls.Add(_intervalNumeric, 1, 2);

            // Checkboxes
            _autoStartCheck = new CheckBox { Text = "Auto-start monitoring on application launch", AutoSize = true };
            mainPanel.Controls.Add(_autoStartCheck, 0, 3);
            mainPanel.SetColumnSpan(_autoStartCheck, 2);

            _startWithWindowsCheck = new CheckBox { Text = "Start application with Windows", AutoSize = true };
            mainPanel.Controls.Add(_startWithWindowsCheck, 0, 4);
            mainPanel.SetColumnSpan(_startWithWindowsCheck, 2);

            // Sensors group
            var sensorsGroup = new GroupBox { Text = "Select Sensors to Monitor", Dock = DockStyle.Fill };
            var sensorsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };

            // Search panel
            var searchPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 3 };
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            searchPanel.Controls.Add(new Label { Text = "Search:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _searchTextBox = new TextBox { Dock = DockStyle.Fill };
            _searchTextBox.TextChanged += OnSearchChanged;
            _searchTextBox.PlaceholderText = "Type to search sensors...";
            searchPanel.Controls.Add(_searchTextBox, 1, 0);

            _clearSearchButton = new Button { Text = "Clear", Width = 50 };
            _clearSearchButton.Click += (s, e) => ClearFilters();
            searchPanel.Controls.Add(_clearSearchButton, 2, 0);

            sensorsPanel.Controls.Add(searchPanel, 0, 0);

            // Filter panel
            var filterPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, AutoSize = true };

            filterPanel.Controls.Add(new Label { Text = "Type:", AutoSize = true, Anchor = AnchorStyles.Left });
            _typeFilterCombo = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _typeFilterCombo.SelectedIndexChanged += OnFilterChanged;
            filterPanel.Controls.Add(_typeFilterCombo);

            filterPanel.Controls.Add(new Label { Text = "Hardware:", AutoSize = true, Anchor = AnchorStyles.Left });
            _hardwareFilterCombo = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _hardwareFilterCombo.SelectedIndexChanged += OnFilterChanged;
            filterPanel.Controls.Add(_hardwareFilterCombo);

            sensorsPanel.Controls.Add(filterPanel, 0, 1);

            // Selection buttons
            var selectButtonsPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            _selectAllButton = new Button { Text = "Select All Visible", AutoSize = true };
            _deselectAllButton = new Button { Text = "Deselect All Visible", AutoSize = true };
            _sensorCountLabel = new Label { Text = "0 sensors", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(10, 5, 0, 0) };
            _selectAllButton.Click += (s, e) => SetVisibleSensors(true);
            _deselectAllButton.Click += (s, e) => SetVisibleSensors(false);
            selectButtonsPanel.Controls.Add(_selectAllButton);
            selectButtonsPanel.Controls.Add(_deselectAllButton);
            selectButtonsPanel.Controls.Add(_sensorCountLabel);
            sensorsPanel.Controls.Add(selectButtonsPanel, 0, 2);

            // Sensors list
            _sensorsListBox = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            _sensorsListBox.ItemCheck += OnSensorItemCheck;
            sensorsPanel.Controls.Add(_sensorsListBox, 0, 3);

            sensorsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sensorsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sensorsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sensorsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            sensorsGroup.Controls.Add(sensorsPanel);
            mainPanel.Controls.Add(sensorsGroup, 0, 5);
            mainPanel.SetColumnSpan(sensorsGroup, 2);

            // Row styles
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Buttons
            var buttonsPanel = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right };
            _saveButton = new Button { Text = "Save", Width = 80, DialogResult = DialogResult.OK };
            _cancelButton = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            _saveButton.Click += OnSaveClick;
            buttonsPanel.Controls.Add(_saveButton);
            buttonsPanel.Controls.Add(_cancelButton);
            mainPanel.Controls.Add(buttonsPanel, 1, 6);

            this.Controls.Add(mainPanel);
            this.AcceptButton = _saveButton;
            this.CancelButton = _cancelButton;
        }

        private void LoadCurrentSettings()
        {
            RefreshComPorts();

            if (!string.IsNullOrEmpty(_configManager.Config.ComPort))
            {
                _comPortCombo.SelectedItem = _configManager.Config.ComPort;
            }

            _baudRateCombo.SelectedItem = _configManager.Config.BaudRate;
            _intervalNumeric.Value = _configManager.Config.SendIntervalMs;
            _autoStartCheck.Checked = _configManager.Config.AutoStart;
            _startWithWindowsCheck.Checked = _configManager.Config.StartWithWindows;

            LoadFilters();
            LoadSensors();
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
            // Temporarily disable event handlers during loading
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

            // Re-enable event handlers
            _typeFilterCombo.SelectedIndexChanged += OnFilterChanged;
            _hardwareFilterCombo.SelectedIndexChanged += OnFilterChanged;
        }

        private void LoadSensors()
        {
            // Temporarily disable event handler during loading
            _sensorsListBox.ItemCheck -= OnSensorItemCheck;
            _sensorsListBox.Items.Clear();

            var searchQuery = _searchTextBox.Text;
            var typeFilter = _typeFilterCombo.SelectedIndex > 0 ? _typeFilterCombo.SelectedItem?.ToString() : null;
            var hardwareFilter = _hardwareFilterCombo.SelectedIndex > 0 ? _hardwareFilterCombo.SelectedItem?.ToString() : null;

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
                var displayText = $"[{sensor.Hardware}] {sensor.Type}:  {sensor.Name}";
                var isChecked = _configManager.Config.SelectedSensors.Contains(sensor.Id);
                _sensorsListBox.Items.Add(new SensorListItem(sensor.Id, displayText), isChecked);
            }

            // Re-enable event handler
            _sensorsListBox.ItemCheck += OnSensorItemCheck;

            UpdateSensorCount();
        }

        private void UpdateSensorCount()
        {
            var total = _sensorsListBox.Items.Count;
            var selected = _sensorsListBox.CheckedItems.Count;
            _sensorCountLabel.Text = $"{selected} selected / {total} visible";
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

            // Update count after the check state changes using a timer to avoid handle issues
            var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                timer.Dispose();
                UpdateSensorCount();
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

        private void SetVisibleSensors(bool isChecked)
        {
            _sensorsListBox.ItemCheck -= OnSensorItemCheck;

            for (int i = 0; i < _sensorsListBox.Items.Count; i++)
            {
                _sensorsListBox.SetItemChecked(i, isChecked);
            }

            _sensorsListBox.ItemCheck += OnSensorItemCheck;
            UpdateSensorCount();
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            _configManager.Config.ComPort = _comPortCombo.SelectedItem?.ToString() ?? "";
            _configManager.Config.BaudRate = (int)(_baudRateCombo.SelectedItem ?? 115200);
            _configManager.Config.SendIntervalMs = (int)_intervalNumeric.Value;
            _configManager.Config.AutoStart = _autoStartCheck.Checked;
            _configManager.Config.StartWithWindows = _startWithWindowsCheck.Checked;

            // Collect all checked sensors (need to reload full list to get all selections)
            var allSensors = _monitorService.GetAllSensors();
            var currentlyVisible = _sensorsListBox.Items.Cast<SensorListItem>().ToList();

            // Keep previously selected sensors that are not currently visible
            var selectedIds = _configManager.Config.SelectedSensors
                .Where(id => !currentlyVisible.Any(v => v.Id == id))
                .ToList();

            // Add currently checked visible sensors
            foreach (var item in _sensorsListBox.CheckedItems.Cast<SensorListItem>())
            {
                if (!selectedIds.Contains(item.Id))
                {
                    selectedIds.Add(item.Id);
                }
            }

            _configManager.Config.SelectedSensors = selectedIds;
            _configManager.SaveConfig();
        }

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
}