using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace HardwareMonitorTray
{
    public class AppConfig
    {
        public string ComPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 115200;
        public int SendIntervalMs { get; set; } = 1000;
        public bool AutoStart { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public List<string> SelectedSensors { get; set; } = new List<string>();
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
                    Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
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
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(_configPath, json);

            UpdateStartWithWindows();
        }

        private void UpdateStartWithWindows()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key != null)
                    {
                        if (Config.StartWithWindows)
                        {
                            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                            // For . NET 5+ single file apps, use Environment.ProcessPath
                            if (string.IsNullOrEmpty(exePath) || exePath.EndsWith(".dll"))
                            {
                                exePath = Environment.ProcessPath;
                            }
                            key.SetValue(AppName, $"\"{exePath}\"");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
            }
            catch { }
        }
    }
}