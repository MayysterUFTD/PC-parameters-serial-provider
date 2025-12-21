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

        private ComboBox _comPortCombo;
        private ComboBox _baudRateCombo;
        private NumericUpDown _intervalNumeric;
        private CheckBox _autoStartCheck;
        private CheckBox _startWithWindowsCheck;
        private CheckedListBox _sensorsListBox;
        private Button _refreshButton;
        private Button _saveButton;
        private Button _cancelButton;
        private Button _selectAllButton;
        private Button _deselectAllButton;

        public SettingsForm(ConfigManager configManager, HardwareMonitorService monitorService)
        {
            _configManager = configManager;
            _monitorService = monitorService;

            InitializeComponents();
            LoadCurrentSettings();
        }

        private void InitializeComponents()
        {
            this.Text = "Ustawienia Hardware Monitor";
            this.Size = new Size(600, 550);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                RowCount = 7,
                ColumnCount = 2
            };

            // Port COM
            mainPanel.Controls.Add(new Label { Text = "Port COM:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var comPanel = new FlowLayoutPanel { AutoSize = true };
            _comPortCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _refreshButton = new Button { Text = "Odśwież", Width = 70 };
            _refreshButton.Click += (s, e) => RefreshComPorts();
            comPanel.Controls.Add(_comPortCombo);
            comPanel.Controls.Add(_refreshButton);
            mainPanel.Controls.Add(comPanel, 1, 0);

            // Baud Rate
            mainPanel.Controls.Add(new Label { Text = "Prędkość (baud):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            _baudRateCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _baudRateCombo.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 });
            mainPanel.Controls.Add(_baudRateCombo, 1, 1);

            // Interwał
            mainPanel.Controls.Add(new Label { Text = "Interwał wysyłania (ms):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            _intervalNumeric = new NumericUpDown { Minimum = 100, Maximum = 60000, Width = 100 };
            mainPanel.Controls.Add(_intervalNumeric, 1, 2);

            // Checkboxy
            _autoStartCheck = new CheckBox { Text = "Automatycznie rozpocznij wysyłanie przy starcie programu", AutoSize = true };
            mainPanel.Controls.Add(_autoStartCheck, 0, 3);
            mainPanel.SetColumnSpan(_autoStartCheck, 2);

            _startWithWindowsCheck = new CheckBox { Text = "Uruchamiaj program razem z Windows", AutoSize = true };
            mainPanel.Controls.Add(_startWithWindowsCheck, 0, 4);
            mainPanel.SetColumnSpan(_startWithWindowsCheck, 2);

            // Sensory
            var sensorsGroup = new GroupBox { Text = "Wybierz sensory do wysyłania", Dock = DockStyle.Fill };
            var sensorsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };

            var selectButtonsPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            _selectAllButton = new Button { Text = "Zaznacz wszystkie", AutoSize = true };
            _deselectAllButton = new Button { Text = "Odznacz wszystkie", AutoSize = true };
            _selectAllButton.Click += (s, e) => SetAllSensors(true);
            _deselectAllButton.Click += (s, e) => SetAllSensors(false);
            selectButtonsPanel.Controls.Add(_selectAllButton);
            selectButtonsPanel.Controls.Add(_deselectAllButton);
            sensorsPanel.Controls.Add(selectButtonsPanel, 0, 0);

            _sensorsListBox = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            sensorsPanel.Controls.Add(_sensorsListBox, 0, 1);
            sensorsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sensorsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            sensorsGroup.Controls.Add(sensorsPanel);
            mainPanel.Controls.Add(sensorsGroup, 0, 5);
            mainPanel.SetColumnSpan(sensorsGroup, 2);
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Przyciski
            var buttonsPanel = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right };
            _saveButton = new Button { Text = "Zapisz", Width = 80, DialogResult = DialogResult.OK };
            _cancelButton = new Button { Text = "Anuluj", Width = 80, DialogResult = DialogResult.Cancel };
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

        private void LoadSensors()
        {
            _sensorsListBox.Items.Clear();
            var sensors = _monitorService.GetAllSensors();

            foreach (var sensor in sensors.OrderBy(s => s.Hardware).ThenBy(s => s.Type).ThenBy(s => s.Name))
            {
                var displayText = $"[{sensor.Hardware}] {sensor.Type}:  {sensor.Name}";
                var isChecked = _configManager.Config.SelectedSensors.Contains(sensor.Id);
                _sensorsListBox.Items.Add(new SensorListItem(sensor.Id, displayText), isChecked);
            }
        }

        private void SetAllSensors(bool isChecked)
        {
            for (int i = 0; i < _sensorsListBox.Items.Count; i++)
            {
                _sensorsListBox.SetItemChecked(i, isChecked);
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            _configManager.Config.ComPort = _comPortCombo.SelectedItem?.ToString() ?? "";
            _configManager.Config.BaudRate = (int)(_baudRateCombo.SelectedItem ?? 115200);
            _configManager.Config.SendIntervalMs = (int)_intervalNumeric.Value;
            _configManager.Config.AutoStart = _autoStartCheck.Checked;
            _configManager.Config.StartWithWindows = _startWithWindowsCheck.Checked;

            _configManager.Config.SelectedSensors.Clear();
            foreach (var item in _sensorsListBox.CheckedItems.Cast<SensorListItem>())
            {
                _configManager.Config.SelectedSensors.Add(item.Id);
            }

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