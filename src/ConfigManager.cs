using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using HardwareMonitorTray.Protocol;

namespace HardwareMonitorTray
{
    public class AppConfig
    {
        public string ComPort { get; set; } = "";
        public int BaudRate { get; set; } = 115200;
        public int SendIntervalMs { get; set; } = 500;
        public int RefreshIntervalMs { get; set; } = 250;  // NOWE - odświeżanie danych z hardware
        public ProtocolMode ProtocolMode { get; set; } = ProtocolMode.Binary;
        public IconStyle IconStyle { get; set; } = IconStyle.Modern;
        public bool AutoStart { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public List<string> SelectedSensors { get; set; } = new();
    }

    public class ConfigManager
    {
        private readonly string _configPath;
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "HardwareMonitorTray";

        public AppConfig Config { get; private set; }

        public ConfigManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HardwareMonitorTray"
            );
            Directory.CreateDirectory(appDataPath);
            _configPath = Path.Combine(appDataPath, "config.json");

            LoadConfig();
        }

        public void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    var options = new JsonSerializerOptions
                    {
                        Converters = { new JsonStringEnumConverter() }
                    };
                    Config = JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
                }
                catch
                {
                    Config = new AppConfig();
                }
            }
            else
            {
                Config = new AppConfig();
            }
        }

        public void SaveConfig()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(_configPath, json);

            UpdateStartWithWindows();
        }

        private void UpdateStartWithWindows()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key != null)
                {
                    if (Config.StartWithWindows)
                    {
                        var exePath = Environment.ProcessPath ??
                            System.Reflection.Assembly.GetExecutingAssembly().Location;

                        if (!string.IsNullOrEmpty(exePath))
                            key.SetValue(AppName, $"\"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }
        }
    }
}