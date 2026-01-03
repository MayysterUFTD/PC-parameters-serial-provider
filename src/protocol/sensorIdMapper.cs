using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace HardwareMonitorTray.Protocol
{
    /// <summary>
    /// Persystentna mapa ID sensorów - zapisuje się automatycznie
    /// Używa 2-bajtowych ID z wykluczeniem bajtów kontrolnych protokołu
    /// </summary>
    public class SensorIdMapper
    {
        private readonly string _mapFilePath;
        private Dictionary<string, SensorMapEntry> _map = new();
        private ushort _nextId = 0x0001;

        // Zarezerwowane bajty - nie mogą być częścią ID
        private const byte RESERVED_START = 0xAA;
        private const byte RESERVED_END = 0x55;

        // Singleton
        private static SensorIdMapper _instance;
        public static SensorIdMapper Instance => _instance ??= new SensorIdMapper();

        public event Action OnMapChanged;

        public class SensorMapEntry
        {
            public ushort Id { get; set; }  // Zmiana z byte na ushort
            public string Name { get; set; } = "";
            public string Hardware { get; set; } = "";
            public string Type { get; set; } = "";
            public string Unit { get; set; } = "";
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
        }

        private SensorIdMapper()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HardwareMonitorTray"
            );
            Directory.CreateDirectory(appDataPath);
            _mapFilePath = Path.Combine(appDataPath, "sensor_map.json");

            Load();
        }

        /// <summary>
        /// Sprawdza czy ID zawiera zarezerwowane bajty (0xAA lub 0x55)
        /// </summary>
        private static bool IsReservedId(ushort id)
        {
            byte highByte = (byte)(id >> 8);
            byte lowByte = (byte)(id & 0xFF);

            return highByte == RESERVED_START || highByte == RESERVED_END ||
                   lowByte == RESERVED_START || lowByte == RESERVED_END;
        }

        /// <summary>
        /// Pobiera lub przypisuje stałe ID dla sensora
        /// </summary>
        public ushort GetOrAssignId(string sensorFullId, SensorInfo sensor)
        {
            if (_map.TryGetValue(sensorFullId, out var entry))
            {
                // Aktualizuj LastSeen
                entry.LastSeen = DateTime.Now;

                // Walidacja - sprawdź czy istniejące ID nie jest zarezerwowane
                if (IsReservedId(entry.Id))
                {
                    // Stare ID było nieprawidłowe - przypisz nowe
                    entry.Id = FindNextFreeId();
                    Save();
                    System.Diagnostics.Debug.WriteLine($"[SensorMap] Fixed reserved ID for:  {sensor.Name} -> 0x{entry.Id:X4}");
                }

                return entry.Id;
            }

            // Nowy sensor - przypisz ID
            ushort newId = FindNextFreeId();

            _map[sensorFullId] = new SensorMapEntry
            {
                Id = newId,
                Name = sensor.Name,
                Hardware = sensor.Hardware,
                Type = sensor.Type,
                Unit = sensor.Unit,
                FirstSeen = DateTime.Now,
                LastSeen = DateTime.Now
            };

            Save();
            OnMapChanged?.Invoke();

            System.Diagnostics.Debug.WriteLine($"[SensorMap] New sensor: 0x{newId:X4} = {sensor.Name}");

            return newId;
        }

        /// <summary>
        /// Pobiera ID jeśli istnieje
        /// </summary>
        public ushort GetId(string sensorFullId)
        {
            return _map.TryGetValue(sensorFullId, out var entry) ? entry.Id : (ushort)0xFFFF;
        }

        /// <summary>
        /// Sprawdza czy sensor jest w mapie
        /// </summary>
        public bool Contains(string sensorFullId)
        {
            return _map.ContainsKey(sensorFullId);
        }

        /// <summary>
        /// Zwraca wszystkie zmapowane sensory
        /// </summary>
        public IReadOnlyDictionary<string, SensorMapEntry> GetAll()
        {
            return _map;
        }

        /// <summary>
        /// Liczba zmapowanych sensorów
        /// </summary>
        public int Count => _map.Count;

        /// <summary>
        /// Znajduje następne wolne ID, pomijając zarezerwowane wartości
        /// </summary>
        private ushort FindNextFreeId()
        {
            var usedIds = new HashSet<ushort>(_map.Values.Select(e => e.Id));

            // Maksymalne ID - 0xFFFD (0xFFFE i 0xFFFF zarezerwowane)
            const ushort MAX_ID = 0xFFFD;

            // Szukaj od _nextId
            while (_nextId <= MAX_ID)
            {
                if (!usedIds.Contains(_nextId) && !IsReservedId(_nextId))
                {
                    return _nextId++;
                }
                _nextId++;
            }

            // Reset i szukaj od początku
            for (ushort i = 0x0001; i <= MAX_ID; i++)
            {
                if (!usedIds.Contains(i) && !IsReservedId(i))
                {
                    _nextId = (ushort)(i + 1);
                    return i;
                }
            }

            // Brak wolnych ID - nie powinno się zdarzyć przy 65000+ możliwych
            throw new InvalidOperationException("No free sensor IDs available");
        }

        /// <summary>
        /// Usuwa nieużywane sensory (starsze niż X dni)
        /// </summary>
        public int Cleanup(int daysOld = 30)
        {
            var cutoff = DateTime.Now.AddDays(-daysOld);
            var toRemove = _map.Where(kv => kv.Value.LastSeen < cutoff).Select(kv => kv.Key).ToList();

            foreach (var key in toRemove)
            {
                _map.Remove(key);
            }

            if (toRemove.Count > 0)
            {
                Save();
                OnMapChanged?.Invoke();
            }

            return toRemove.Count;
        }

        /// <summary>
        /// Naprawia wszystkie zarezerwowane ID w mapie
        /// </summary>
        public int FixReservedIds()
        {
            int fixed_count = 0;

            foreach (var entry in _map.Values)
            {
                if (IsReservedId(entry.Id))
                {
                    entry.Id = FindNextFreeId();
                    fixed_count++;
                }
            }

            if (fixed_count > 0)
            {
                Save();
                OnMapChanged?.Invoke();
                System.Diagnostics.Debug.WriteLine($"[SensorMap] Fixed {fixed_count} reserved IDs");
            }

            return fixed_count;
        }

        /// <summary>
        /// Resetuje całą mapę
        /// </summary>
        public void Reset()
        {
            _map.Clear();
            _nextId = 0x0001;
            Save();
            OnMapChanged?.Invoke();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_mapFilePath))
                {
                    var json = File.ReadAllText(_mapFilePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, SensorMapEntry>>(json);
                    if (data != null)
                    {
                        _map = data;
                        // Znajdź najwyższe używane ID
                        if (_map.Count > 0)
                        {
                            _nextId = (ushort)Math.Min(0xFFFD, _map.Values.Max(e => e.Id) + 1);
                        }

                        // Napraw ewentualne zarezerwowane ID z poprzednich wersji
                        FixReservedIds();

                        System.Diagnostics.Debug.WriteLine($"[SensorMap] Loaded {_map.Count} sensors");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SensorMap] Load error: {ex.Message}");
                _map = new Dictionary<string, SensorMapEntry>();
            }
        }

        private void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_map, options);
                File.WriteAllText(_mapFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SensorMap] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Eksportuje mapę jako C struct do pliku
        /// </summary>
        public void ExportStruct(string filePath, IEnumerable<string> selectedSensorIds = null)
        {
            var sensors = selectedSensorIds != null
                ? _map.Where(kv => selectedSensorIds.Contains(kv.Key)).ToList()
                : _map.ToList();

            if (sensors.Count == 0)
            {
                throw new InvalidOperationException("No sensors to export");
            }

            // Generuj unikalne nazwy
            var sensorNames = new Dictionary<string, string>();
            var usedNames = new Dictionary<string, int>();

            foreach (var kv in sensors.OrderBy(x => x.Value.Id))
            {
                string baseName = GenerateEnumName(kv.Value);

                if (usedNames.TryGetValue(baseName, out int count))
                {
                    usedNames[baseName] = count + 1;
                    sensorNames[kv.Key] = $"{baseName}_{count + 1}";
                }
                else
                {
                    usedNames[baseName] = 1;
                    sensorNames[kv.Key] = baseName;
                }
            }

            var sb = new StringBuilder();
            var baseName2 = Path.GetFileNameWithoutExtension(filePath).ToUpper().Replace("-", "_").Replace(" ", "_");

            sb.AppendLine("/**");
            sb.AppendLine($" * @file {Path.GetFileName(filePath)}");
            sb.AppendLine(" * @brief Hardware Monitor - Sensor ID Map (Protocol v2 - 16-bit IDs)");
            sb.AppendLine($" * @date {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(" * @note Auto-generated from sensor_map.json");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine($"#ifndef {baseName2}_H");
            sb.AppendLine($"#define {baseName2}_H");
            sb.AppendLine();
            sb.AppendLine("#include <stdint. h>");
            sb.AppendLine("#include <stdbool. h>");
            sb.AppendLine();
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine("/*   PROTOCOL CONSTANTS                                                */");
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine();
            sb.AppendLine("#define HW_PROTO_START   0xAA");
            sb.AppendLine("#define HW_PROTO_END     0x55");
            sb.AppendLine("#define HW_PROTO_VERSION 0x02  /* Protocol v2: 16-bit sensor IDs */");
            sb.AppendLine();
            sb.AppendLine("/* Reserved bytes - sensor IDs cannot contain these values */");
            sb.AppendLine("#define HW_RESERVED_START 0xAA");
            sb.AppendLine("#define HW_RESERVED_END   0x55");
            sb.AppendLine();
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine("/*   SENSOR IDs (16-bit)                                               */");
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine();

            // Grupuj po kategorii
            var grouped = sensors
                .GroupBy(kv => GetCategory(kv.Value))
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                sb.AppendLine($"/* {group.Key} */");
                foreach (var kv in group.OrderBy(x => x.Value.Id))
                {
                    var enumName = sensorNames[kv.Key];
                    var comment = kv.Value.Name;
                    if (comment.Length > 35) comment = comment.Substring(0, 32) + "...";
                    sb.AppendLine($"#define {enumName,-45} 0x{kv.Value.Id:X4}  /* {comment} */");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"#define HW_SENSOR_COUNT  {sensors.Count}");
            sb.AppendLine();
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine("/*   SENSOR INFO STRUCT                                                */");
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine();
            sb.AppendLine("typedef struct {");
            sb.AppendLine("    uint16_t    id;      /* 16-bit sensor ID */");
            sb.AppendLine("    float       value;");
            sb.AppendLine("    bool        valid;");
            sb.AppendLine("} hw_sensor_t;");
            sb.AppendLine();
            sb.AppendLine("/* Sensor info table */");
            sb.AppendLine("typedef struct {");
            sb.AppendLine("    uint16_t    id;      /* 16-bit sensor ID */");
            sb.AppendLine("    const char* name;");
            sb.AppendLine("    const char* unit;");
            sb.AppendLine("} hw_sensor_info_t;");
            sb.AppendLine();
            sb.AppendLine("static const hw_sensor_info_t HW_SENSOR_INFO[] = {");

            foreach (var kv in sensors.OrderBy(x => x.Value.Id))
            {
                var name = kv.Value.Name;
                if (name.Length > 30) name = name.Substring(0, 27) + "...";
                name = name.Replace("\"", "'");
                var enumName = sensorNames[kv.Key];
                sb.AppendLine($"    {{ {enumName}, \"{name}\", \"{kv.Value.Unit}\" }},");
            }

            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine("/*   HELPER FUNCTIONS                                                  */");
            sb.AppendLine("/* ═══════════════════════════════════════════════════════════════════ */");
            sb.AppendLine();
            sb.AppendLine("static inline float hw_get_value(const hw_sensor_t* sensors, int count, uint16_t id) {");
            sb.AppendLine("    for (int i = 0; i < count; i++) {");
            sb.AppendLine("        if (sensors[i].id == id && sensors[i].valid) return sensors[i].value;");
            sb.AppendLine("    }");
            sb.AppendLine("    return -999.0f;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static inline const char* hw_get_name(uint16_t id) {");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (HW_SENSOR_INFO[i].id == id) return HW_SENSOR_INFO[i].name;");
            sb.AppendLine("    }");
            sb.AppendLine("    return \"Unknown\";");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static inline const char* hw_get_unit(uint16_t id) {");
            sb.AppendLine("    for (int i = 0; i < HW_SENSOR_COUNT; i++) {");
            sb.AppendLine("        if (HW_SENSOR_INFO[i]. id == id) return HW_SENSOR_INFO[i]. unit;");
            sb.AppendLine("    }");
            sb.AppendLine("    return \"\";");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"#endif /* {baseName2}_H */");

            File.WriteAllText(filePath, sb.ToString());
        }

        private string GenerateEnumName(SensorMapEntry entry)
        {
            var cat = GetCategory(entry);
            var type = entry.Type.ToUpper();

            var name = entry.Name
                .Replace("°C", "")
                .Replace("°", "")
                .Replace("%", "PCT")
                .Replace("#", "N")
                .Trim();

            var cleanName = new StringBuilder();
            bool lastWasUnderscore = false;

            foreach (char c in name.ToUpper())
            {
                if (char.IsLetterOrDigit(c))
                {
                    cleanName.Append(c);
                    lastWasUnderscore = false;
                }
                else if (!lastWasUnderscore && cleanName.Length > 0)
                {
                    cleanName.Append('_');
                    lastWasUnderscore = true;
                }
            }

            var result = cleanName.ToString().TrimEnd('_');

            if (result.Length > 25)
            {
                result = result.Substring(0, 25).TrimEnd('_');
            }

            var hwShort = GetHardwareShort(entry.Hardware);
            if (!string.IsNullOrEmpty(hwShort) && !result.Contains(hwShort))
            {
                result = $"{hwShort}_{result}";
            }

            return $"SENSOR_{cat}_{type}_{result}";
        }

        private string GetHardwareShort(string hardware)
        {
            var hw = hardware.ToLower();

            if (hw.Contains("ryzen")) return "RYZEN";
            if (hw.Contains("intel")) return "INTEL";

            if (hw.Contains("nvidia") || hw.Contains("geforce") || hw.Contains("rtx") || hw.Contains("gtx"))
                return "NV";
            if (hw.Contains("radeon") || hw.Contains("amd"))
                return "AMD";

            if (hw.Contains("samsung")) return "SAM";
            if (hw.Contains("crucial")) return "CRU";
            if (hw.Contains("western") || hw.Contains("wd")) return "WD";
            if (hw.Contains("seagate")) return "SEA";
            if (hw.Contains("kingston")) return "KIN";

            if (hw.Contains("intel") && hw.Contains("ethernet")) return "INTEL";
            if (hw.Contains("realtek")) return "RTK";
            if (hw.Contains("wifi") || hw.Contains("wireless")) return "WIFI";

            return "";
        }

        public string GeneratePreview(IEnumerable<string> selectedSensorIds = null)
        {
            var sensors = selectedSensorIds != null
                ? _map.Where(kv => selectedSensorIds.Contains(kv.Key)).ToList()
                : _map.ToList();

            if (sensors.Count == 0)
                return "(no sensors mapped)";

            var sb = new StringBuilder();
            sb.AppendLine("╔═════════╦════════════════════════════════╦═════════╗");
            sb.AppendLine("║   ID    ║ Name                           ║ Type    ║");
            sb.AppendLine("╠═════════╬════════════════════════════════╬═════════╣");

            foreach (var kv in sensors.OrderBy(x => x.Value.Id).Take(20))
            {
                var name = kv.Value.Name.Length > 30 ? kv.Value.Name.Substring(0, 27) + "..." : kv.Value.Name;
                var type = kv.Value.Type.Length > 7 ? kv.Value.Type.Substring(0, 7) : kv.Value.Type;
                sb.AppendLine($"║ 0x{kv.Value.Id:X4} ║ {name,-30} ║ {type,-7} ║");
            }

            if (sensors.Count > 20)
            {
                sb.AppendLine($"║  ...     ║ ... and {sensors.Count - 20} more                ║   ...    ║");
            }

            sb.AppendLine("╚═════════╩════════════════════════════════╩═════════╝");
            sb.AppendLine($"Total: {sensors.Count} sensors");

            return sb.ToString();
        }

        private string GetCategory(SensorMapEntry entry)
        {
            var hw = entry.Hardware.ToLower();
            if (hw.Contains("cpu") || hw.Contains("ryzen") || hw.Contains("intel")) return "CPU";
            if (hw.Contains("gpu") || hw.Contains("nvidia") || hw.Contains("radeon")) return "GPU";
            if (hw.Contains("memory")) return "RAM";
            if (hw.Contains("ssd") || hw.Contains("nvme") || hw.Contains("hdd")) return "DISK";
            if (hw.Contains("network")) return "NET";
            if (hw.Contains("battery")) return "BAT";
            return "SYS";
        }
    }
}