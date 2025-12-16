using LiveCharts;
using LiveCharts.Defaults;
using System.IO.Pipes;
using System.Windows;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;
using QuantumSqueezingUI;
using Quantum_measurement_UI;
using System.Windows.Media;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Controls;

namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window
    {
        #region UI Update and Logging Functions

        /// <summary>
        /// Updates the elapsed time display.
        /// </summary>
        private void UpdateElapsedTime(object sender, EventArgs e)
        {
            if (isExperimentRunning)
            {
                TimeSpan elapsed = DateTime.Now - experimentStartTime;
                ElapsedTimeText.Text = elapsed.ToString(@"hh\:mm\:ss");
            }
        }

        /// <summary>
        /// Retrieves a list of checked items from the Sample list.
        /// </summary>
        private List<string> GetSelectedSamples()
        {
            List<string> selected = new List<string>();

            // Scan the new StackPanel we created in XAML
            foreach (var child in SampleCheckBoxList.Children)
            {
                if (child is CheckBox checkBox && checkBox.IsChecked == true)
                {
                    selected.Add(checkBox.Content.ToString());
                }
            }

            // Fallback: If nothing is checked, return "Unknown"
            if (selected.Count == 0) selected.Add("Unknown");

            return selected;
        }

        /// <summary>
        /// Logs events to the experiment log file with a timestamp.
        /// </summary>
        public void LogExperimentEvent(string message)
        {
            if (experimentLogWriter != null)
            {
                string logEntry = $"{DateTime.Now:HH:mm:ss.fff}: {message}";
                experimentLogWriter.WriteLine(logEntry);
                experimentLogWriter.Flush(); // Ensure immediate write to the file
            }
        }

        /// </summary>
        public void LogMotorNMetric(string message)
        {
            if (experimentLogWriter != null)
            {
                string logEntry = $"{DateTime.Now:HH:mm:ss.fff}: {message}";
                motorMetricLogWriter.WriteLine(logEntry);
                motorMetricLogWriter.Flush(); // Ensure immediate write to the file
            }
        }
        /// </summary>
        public void LogSensitivity(string message)
        {
            if (experimentLogWriter != null)
            {
                string logEntry = $"{DateTime.Now:HH:mm:ss.fff}: {message}";
                sensitivityLogWriter.WriteLine(logEntry);
                sensitivityLogWriter.Flush(); // Ensure immediate write to the file
            }
        }
        /// </summary>
        public void LogDroppedWindow(string message)
        {
            if (experimentLogWriter != null)
            {
                string logEntry = $"{DateTime.Now:HH:mm:ss.fff}: {message}";
                droppedWindowLogWriter.WriteLine(logEntry);
                droppedWindowLogWriter.Flush(); // Ensure immediate write to the file
            }
        }


        /// <summary>
        /// Appends messages to the shared message log with a timestamp.
        /// </summary>
        public void AppendMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                SharedMessageLog.AppendText($"{DateTime.Now:HH:mm:ss.fff}: {message}\n");
                SharedMessageLog.ScrollToEnd();
            });
        }
        // Store the full list so we can filter it later
        private List<ExperimentRecord> allExperiments = new List<ExperimentRecord>();

        private void RefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            LoadExperimentHistory();
        }

        private void LoadExperimentHistory()
        {
            allExperiments = new List<ExperimentRecord>(); // GOOD: Creates a fresh list

            // 1. Check if the base directory exists
            if (!Directory.Exists(resultsBaseDirectory)) return;

            // 2. Get all subdirectories (each represents one experiment)
            string[] directories = Directory.GetDirectories(resultsBaseDirectory);

            // 3. Loop through them backwards (newest first)
            foreach (var dir in directories.Reverse())
            {
                string jsonPath = Path.Combine(dir, "metadata.json");

                // Only list folders that have our metadata file
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(jsonPath);

                        // Flexible parsing using JsonElement to handle missing fields gracefully
                        using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                        {
                            JsonElement root = doc.RootElement;

                            // 1. Existing Helpers
                            string GetStr(string name) => root.TryGetProperty(name, out var p) ? p.ToString() : "";

                            string GetPhys(string name)
                            {
                                if (root.TryGetProperty("PhysicsData", out var phys) &&
                                    phys.TryGetProperty(name, out var val))
                                {
                                    if (val.ValueKind == JsonValueKind.Number)
                                        return val.GetDouble().ToString("0.###");
                                    return val.ToString();
                                }
                                return "-";
                            }

                            // --- FIX START: Declare variable outside the if block ---
                            string tagStr = "";

                            if (root.TryGetProperty("Tags", out var tagsArray) && tagsArray.ValueKind == JsonValueKind.Array)
                            {
                                List<string> tList = new List<string>();
                                foreach (var t in tagsArray.EnumerateArray()) tList.Add(t.ToString());
                                tagStr = string.Join(", ", tList);
                            }
                            // --- FIX END ---

                            // Logic to sum power
                            double p1 = 0, p2 = 0;
                            if (root.TryGetProperty("PhysicsData", out var pData))
                            {
                                if (pData.TryGetProperty("Power_mW_1", out var vp1)) p1 = vp1.GetDouble();
                                if (pData.TryGetProperty("Power_mW_2", out var vp2)) p2 = vp2.GetDouble();
                            }
                            string totalPower = (p1 + p2).ToString("0.##");

                            var record = new ExperimentRecord
                            {
                                Timestamp = GetStr("Timestamp"),
                                Duration = GetStr("Duration"),
                                Sample = GetStr("Sample"),
                                Description = GetStr("Description"),
                                Tags = tagStr, // Now this variable exists regardless of the if-check above
                                FullPath = dir,

                                // Map new fields
                                ShotNoiseResult = GetPhys("ShotNoiseResult_urad2_rtHz"),
                                Sensitivity = GetPhys("Sensitivity_V_photon"),
                                ScanRange = GetPhys("ScanRange_mm"),
                                TotalPower = totalPower
                            };

                            allExperiments.Add(record);
                        }
                    }
                    catch
                    {
                        // If a JSON file is corrupt, just skip it or log error
                    }
                }
            }

            // 4. Update the UI
            HistoryGrid.ItemsSource = allExperiments;
        }

        // Search Filter
        private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = HistorySearchBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(query))
            {
                HistoryGrid.ItemsSource = allExperiments;
            }
            else
            {
                // Filter: Match Sample OR Tags OR Description
                var filtered = allExperiments.Where(r =>
                    (r.Sample != null && r.Sample.ToLower().Contains(query)) ||
                    (r.Tags != null && r.Tags.ToLower().Contains(query)) ||
                    (r.Description != null && r.Description.ToLower().Contains(query))
                ).ToList();

                HistoryGrid.ItemsSource = filtered;
            }
        }

        // Open Folder Button
        private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryGrid.SelectedItem is ExperimentRecord record)
            {
                Process.Start("explorer.exe", record.FullPath);
            }
        }

        // Double click row to open folder
        private void HistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedFolder_Click(null, null);
        }
        #endregion
    }
}
