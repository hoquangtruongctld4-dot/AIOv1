using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace subphimv1.Subphim
{
    public static class IniConfig
    {
        public static Dictionary<string, Dictionary<string, string>> Load(string filePath)
        {
            var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(filePath)) return data;

            string currentSection = null;
            string currentKey = null;
            StringBuilder currentValue = new StringBuilder();

            Action storePreviousEntry = () =>
            {
                if (currentSection != null && currentKey != null)
                {
                    if (!data.ContainsKey(currentSection))
                    {
                        data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    data[currentSection][currentKey] = currentValue.ToString().Trim();
                }
                currentValue.Clear();
            };

            var lines = File.ReadLines(filePath, Encoding.UTF8);
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    storePreviousEntry();
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    currentKey = null;
                }
                else if (currentSection != null && !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith(";") && !trimmedLine.StartsWith("#"))
                {
                    var parts = trimmedLine.Split(new[] { '=' }, 2);
                    if (parts.Length == 2) // New key-value pair
                    {
                        storePreviousEntry();
                        currentKey = parts[0].Trim();
                        currentValue.Append(parts[1].Trim());
                    }
                    else if (currentKey != null) // This is a continuation of the previous key's value
                    {
                        currentValue.Append(Environment.NewLine).Append(trimmedLine);
                    }
                }
            }
            storePreviousEntry(); // Store the last entry in the file

            return data;
        }


        public static void Save(string filePath, Dictionary<string, Dictionary<string, string>> data)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                foreach (var section in data)
                {
                    writer.WriteLine($"[{section.Key}]");
                    foreach (var kvp in section.Value)
                    {
                        // For multi-line values, write the key=value on the first line,
                        // then the rest of the lines.
                        var lines = kvp.Value?.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None) ?? new string[] { "" };
                        writer.WriteLine($"{kvp.Key} = {lines[0]}");
                        for (int i = 1; i < lines.Length; i++)
                        {
                            writer.WriteLine(lines[i]);
                        }
                    }
                    writer.WriteLine(); // Add a blank line between sections
                }
            }
        }


        public static string GetValue(this Dictionary<string, Dictionary<string, string>> data, string section, string key, string defaultValue = "")
        {
            if (data.TryGetValue(section, out var sectionData) && sectionData.TryGetValue(key, out var value))
            {
                return value ?? defaultValue;
            }
            return defaultValue;
        }

        public static bool GetBooleanValue(this Dictionary<string, Dictionary<string, string>> data, string section, string key, bool defaultValue = false)
        {
            string value = data.GetValue(section, key);
            if (bool.TryParse(value, out bool result))
            {
                return result;
            }
            return defaultValue;
        }
    }
}