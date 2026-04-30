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
using System.Collections.Concurrent;

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

        // The master list of data
        private List<ExperimentRecord> _allExperiments = new List<ExperimentRecord>();

        private async Task LoadHistoryAsync()
        {
            string targetFolder = @"D:\Quantum Squeezing Project\DataFiles";

            if (!Directory.Exists(targetFolder))
            {
                MessageBox.Show($"Folder not found: {targetFolder}");
                return;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var tempCollection = new ConcurrentBag<ExperimentRecord>();

            await Task.Run(() =>
            {
                var files = Directory.GetFiles(targetFolder, "metadata.json", SearchOption.AllDirectories);

                Parallel.ForEach(files, (file) =>
                {
                    try
                    {
                        var jsonBytes = File.ReadAllBytes(file);
                        var data = JsonSerializer.Deserialize<ExperimentRecord>(jsonBytes, options);

                        if (data != null && IsExperimentMetadataRecord(data))
                        {
                            data.FullPath = file;
                            string folderName = new DirectoryInfo(Path.GetDirectoryName(file)).Name;

                            // --- STEP A: Try parsing the JSON string date ---
                            bool dateFound = false;
                            if (!string.IsNullOrEmpty(data.TimestampString))
                            {
                                // Handles "2025-12-16 12:49:18" correctly now
                                if (DateTime.TryParse(data.TimestampString, out DateTime jsonDate))
                                {
                                    data.SortableDate = jsonDate;
                                    dateFound = true;
                                }
                            }

                            // --- STEP B: If JSON date failed/missing, try Folder Name ---
                            if (!dateFound)
                            {
                                if (DateTime.TryParseExact(folderName, "yyyyMMdd_HHmmss",
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None,
                                    out DateTime folderDate))
                                {
                                    data.SortableDate = folderDate;
                                }
                                else
                                {
                                    // --- STEP C: Last Resort - File Creation Time ---
                                    data.SortableDate = File.GetCreationTime(file);
                                }
                            }

                            // --- Fix Name for Display ---
                            if (Path.GetFileName(file).ToLower().Contains("metadata"))
                            {
                                data.Filename = folderName;
                            }
                            else if (string.IsNullOrEmpty(data.Filename))
                            {
                                data.Filename = Path.GetFileName(file);
                            }

                            if (string.IsNullOrEmpty(data.Sample)) data.Sample = "Unknown";

                            tempCollection.Add(data);
                        }
                    }
                    catch { /* Corruption or lock */ }
                });
            });

            _allExperiments = tempCollection.OrderByDescending(x => x.SortableDate).ToList();
            HistoryGrid.ItemsSource = _allExperiments;
        }

        private static bool IsExperimentMetadataRecord(ExperimentRecord record)
        {
            return !string.IsNullOrWhiteSpace(record.TimestampString)
                && record.Configuration != null
                && record.PhysicsData != null;
        }
        // ---------------------------------------------------------
        // COPY THIS INTO UI_and_Logging.cs
        // ---------------------------------------------------------

        /// <summary>
        /// Opens the folder containing the selected experiment file.
        /// </summary>
        private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
        {
            // Check if a row is actually selected
            if (HistoryGrid.SelectedItem is ExperimentRecord record)
            {
                // Ensure we have a valid path
                if (!string.IsNullOrEmpty(record.FullPath) && File.Exists(record.FullPath))
                {
                    try
                    {
                        // Open Windows Explorer with the file selected
                        string argument = "/select, \"" + record.FullPath + "\"";
                        System.Diagnostics.Process.Start("explorer.exe", argument);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not open folder: {ex.Message}");
                    }
                }
                else
                {
                    MessageBox.Show("File path not found. Try refreshing the list.");
                }
            }
        }

        private string? GetSelectedExperimentFolder()
        {
            if (HistoryGrid.SelectedItem is not ExperimentRecord record)
            {
                MessageBox.Show("Select an experiment first.");
                return null;
            }

            if (string.IsNullOrEmpty(record.FullPath) || !File.Exists(record.FullPath))
            {
                MessageBox.Show("Experiment path not found. Try refreshing the list.");
                return null;
            }

            return Path.GetDirectoryName(record.FullPath);
        }

        private static bool TryOpenFirstExistingAsset(string folderPath, params string[] fileNames)
        {
            foreach (string fileName in fileNames)
            {
                string candidate = Path.Combine(folderPath, fileName);
                if (!File.Exists(candidate))
                {
                    continue;
                }

                Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = true });
                return true;
            }

            return false;
        }

        private void OpenSelectedLoglogEval_Click(object sender, RoutedEventArgs e)
        {
            string? folderPath = GetSelectedExperimentFolder();
            if (folderPath == null)
            {
                return;
            }

            if (!TryOpenFirstExistingAsset(folderPath, "loglog_eval.png"))
            {
                MessageBox.Show("Could not find loglog_eval.png in the selected experiment folder.");
            }
        }

        private void OpenSelectedDiagonalOffset_Click(object sender, RoutedEventArgs e)
        {
            string? folderPath = GetSelectedExperimentFolder();
            if (folderPath == null)
            {
                return;
            }

            if (!TryOpenFirstExistingAsset(folderPath, "diagonal_offset_matrix_urad2.png", "diagonal_offset_matrix_V2.png"))
            {
                MessageBox.Show("Could not find a diagonal offset image in the selected experiment folder.");
            }
        }

        private void OpenSelectedFftSpectrum_Click(object sender, RoutedEventArgs e)
        {
            string? folderPath = GetSelectedExperimentFolder();
            if (folderPath == null)
            {
                return;
            }

            if (!TryOpenFirstExistingAsset(folderPath, "fft_result.png"))
            {
                MessageBox.Show("Could not find fft_result.png in the selected experiment folder.");
            }
        }

        private void OpenSelectedRawStd_Click(object sender, RoutedEventArgs e)
        {
            string? folderPath = GetSelectedExperimentFolder();
            if (folderPath == null)
            {
                return;
            }

            if (!TryOpenFirstExistingAsset(folderPath, "raw_std_over_time.png"))
            {
                MessageBox.Show("Could not find raw_std_over_time.png in the selected experiment folder.");
            }
        }

        private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = HistorySearchBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(query))
            {
                HistoryGrid.ItemsSource = _allExperiments;
                return;
            }

            var filtered = _allExperiments.Where(x =>
                (x.Sample != null && x.Sample.ToLower().Contains(query)) ||
                (x.TagsDisplay.ToLower().Contains(query)) ||
                (x.Description != null && x.Description.ToLower().Contains(query))
            ).ToList();

            HistoryGrid.ItemsSource = filtered;
        }

        private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistoryAsync();
        }

        #endregion
    }
}
