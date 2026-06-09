// Copyright (c) 2021-2024, JamXi JSG-LLC.
// All rights reserved.

// This file is part of WaveTools.

// WaveTools is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// WaveTools is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with WaveTools.  If not, see <http://www.gnu.org/licenses/>.

// For more information, please refer to <https://www.gnu.org/licenses/gpl-3.0.html>

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WaveTools.Depend;

namespace WaveTools.Views.ToolViews
{
    public sealed partial class AdvancedGraphicSettingsView : Page
    {
        public AdvancedGraphicSettingsView()
        {
            this.InitializeComponent();
            this.Unloaded += AdvancedGraphicSettingsView_Unloaded;
            LoadData();
        }

        private void AdvancedGraphicSettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            SaveData(); // 在页面被销毁前保存数据
        }

        private void LoadData()
        {
            var gamePath = AppDataController.GetGamePathWithoutGameName();
            var engineConfigPath = Path.Combine(gamePath, "Client\\Saved\\Config\\WindowsNoEditor\\Engine.ini");

            if (File.Exists(engineConfigPath))
            {
                var lines = File.ReadAllLines(engineConfigPath);
                var systemSettingsFound = false;
                var systemSettings = new Dictionary<string, string>();

                foreach (var line in lines)
                {
                    if (line.Trim() == "[SystemSettings]")
                    {
                        systemSettingsFound = true;
                        continue;
                    }

                    if (systemSettingsFound)
                    {
                        if (line.StartsWith("[") && line.EndsWith("]"))
                        {
                            break;
                        }

                        var keyValue = line.Split(new[] { '=' }, 2);
                        if (keyValue.Length == 2)
                        {
                            systemSettings[keyValue[0].Trim()] = keyValue[1].Trim();
                        }
                    }
                }

                if (systemSettingsFound && systemSettings.Count > 0)
                {
                    FillTextBoxes(systemSettings);
                }
                else
                {
                    ClearAllTextBoxes();
                }
            }
            else
            {
                ClearAllTextBoxes();
            }
        }

        private void FillTextBoxes(Dictionary<string, string> systemSettings)
        {
            foreach (var control in settingsStackPanel.Children)
            {
                if (control is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBox textBox)
                        {
                            var key = textBox.Tag?.ToString();
                            if (key != null && systemSettings.TryGetValue(key, out var value))
                            {
                                textBox.Text = value;
                            }
                            else
                            {
                                textBox.Text = string.Empty;
                            }
                        }
                    }
                }
            }
        }

        private void ClearAllTextBoxes()
        {
            foreach (var control in settingsStackPanel.Children)
            {
                if (control is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBox textBox)
                        {
                            textBox.Text = string.Empty;
                        }
                    }
                }
            }
        }

        private void SaveData()
        {
            var gamePath = AppDataController.GetGamePathWithoutGameName();
            var engineConfigPath = Path.Combine(gamePath, "Client\\Saved\\Config\\WindowsNoEditor\\Engine.ini");

            if (!File.Exists(engineConfigPath))
            {
                File.Create(engineConfigPath).Dispose();
            }

            var lines = new List<string>(File.ReadAllLines(engineConfigPath));
            int systemSettingsIndex = lines.FindIndex(line => line.Trim() == "[SystemSettings]");

            if (systemSettingsIndex == -1)
            {
                // If the [SystemSettings] block is not found, add a new block
                lines.Add("[SystemSettings]");
                systemSettingsIndex = lines.Count - 1;
            }

            // Find the end of the [SystemSettings] block
            int endIndex = lines.FindIndex(systemSettingsIndex + 1, line => line.StartsWith("[") && line.EndsWith("]"));
            if (endIndex == -1)
            {
                endIndex = lines.Count;
            }

            var updatedSettings = new HashSet<string>();

            // Update existing fields, add new fields, or remove fields with empty values
            for (int i = systemSettingsIndex + 1; i < endIndex; i++)
            {
                var line = lines[i];
                var key = line.Split('=')[0].Trim();

                // Skip if the key has already been updated
                if (updatedSettings.Contains(key))
                {
                    continue;
                }

                // Check if there's a corresponding TextBox with this key
                var found = false;
                foreach (var control in settingsStackPanel.Children)
                {
                    if (control is Grid grid)
                    {
                        foreach (var child in grid.Children)
                        {
                            if (child is TextBox textBox)
                            {
                                var tagKey = textBox.Tag?.ToString();
                                if (tagKey == key)
                                {
                                    var value = textBox.Text;
                                    if (string.IsNullOrEmpty(value))
                                    {
                                        // Remove the field from the file if the value is empty
                                        lines.RemoveAt(i);
                                        endIndex--; // Adjust endIndex after removal
                                        i--; // Adjust i to account for the removed line
                                    }
                                    else
                                    {
                                        // Update the existing field
                                        lines[i] = $"{key}={value}";
                                    }

                                    updatedSettings.Add(key);
                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (found)
                    {
                        break;
                    }
                }

                if (!found && !updatedSettings.Contains(key))
                {
                    // If no TextBox corresponds to this key, remove the line
                    lines.RemoveAt(i);
                    endIndex--; // Adjust endIndex after removal
                    i--; // Adjust i to account for the removed line
                }
            }

            // Add new fields that were not in the file
            foreach (var control in settingsStackPanel.Children)
            {
                if (control is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBox textBox)
                        {
                            var key = textBox.Tag?.ToString();
                            if (!string.IsNullOrEmpty(key) && !updatedSettings.Contains(key))
                            {
                                var value = textBox.Text;
                                if (!string.IsNullOrEmpty(value))
                                {
                                    lines.Insert(endIndex, $"{key}={value}");
                                    endIndex++; // Move endIndex forward after insertion
                                }

                                updatedSettings.Add(key);
                            }
                        }
                    }
                }
            }

            File.WriteAllLines(engineConfigPath, lines);
        }

    }
}
