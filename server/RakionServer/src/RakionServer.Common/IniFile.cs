using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RakionServer.Common
{
    /// <summary>
    /// Parser de arquivos .ini (cross-platform), equivalente ao Systems.Ini do broker
    /// (que usava GetPrivateProfileString do Windows). Secoes e chaves sao
    /// case-insensitive, como na API nativa.
    /// </summary>
    public sealed class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new(StringComparer.OrdinalIgnoreCase);

        public IniFile(string path)
        {
            if (!File.Exists(path))
                return;

            string current = "";
            _sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    if (!_sections.ContainsKey(current))
                        _sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                _sections[current][key] = val;
            }
        }

        public string GetValue(string section, string key, string def)
        {
            if (_sections.TryGetValue(section, out var s) && s.TryGetValue(key, out var v))
                return v;
            return def;
        }

        public int GetValue(string section, string key, int def)
        {
            string v = GetValue(section, key, def.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
        }

        public bool GetBool(string section, string key, bool def)
        {
            string v = GetValue(section, key, def ? "1" : "0").Trim();
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Nomes de todas as chaves de uma secao (equivalente ao GetEntryNames do broker).</summary>
        public string[] GetEntryNames(string section)
        {
            if (_sections.TryGetValue(section, out var s))
            {
                var keys = new string[s.Count];
                s.Keys.CopyTo(keys, 0);
                return keys;
            }
            return Array.Empty<string>();
        }

        public bool HasSection(string section) => _sections.ContainsKey(section);
    }
}
