using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window
    {
        private static readonly string RecentMetadataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QuantumMeasurementUI",
            "recent_metadata.json");

        private void LoadRecentMetadataIntoUi()
        {
            try
            {
                if (!File.Exists(RecentMetadataPath))
                {
                    return;
                }

                string json = File.ReadAllText(RecentMetadataPath);
                var metadata = JsonSerializer.Deserialize<ExperimentMetadataDraft>(json);
                if (metadata != null)
                {
                    ApplyMetadataToUi(metadata);
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Could not load recent metadata: {ex.Message}");
            }
        }

        private bool RequestMetadataBeforeExperimentStart()
        {
            MetadataInputExpander.IsExpanded = true;
            MetadataInputExpander.BringIntoView();

            var dialog = new ExperimentMetadataDialog(
                CreateMetadataDraftFromUi(),
                GetCheckBoxOptions(SampleCheckBoxList),
                GetCheckBoxOptions(MetadataCheckBoxList))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            ApplyMetadataToUi(dialog.Metadata);
            SaveRecentMetadata(dialog.Metadata);
            AppendMessage("Experiment metadata captured.");
            return true;
        }

        private ExperimentMetadataDraft CreateMetadataDraftFromUi()
        {
            return new ExperimentMetadataDraft
            {
                Filename = FileNameInput.Text,
                Description = DescriptionInput.Text,
                Temperature_K = ParseOptionalDouble(TemperatureInput.Text),
                OnSamplePower_mW = ParseOptionalDouble(OnSamplePowerInput.Text),
                PowerDetectorAttenuatorApplied = PowerDetectorAttenuatorAppliedCheckBox.IsChecked == true,
                EnableFFT = EnableFFT,
                Samples = GetSelectedSamples(),
                Tags = GetSelectedTags(),
                UsedOpo = UsedOpoCheckBox.IsChecked == true,
                LaserWavelength_nm = ParseOptionalDouble(LaserWavelengthInput.Text),
                Detector = (DetectorSelection.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DetectorTypes.Si
            };
        }

        private void ApplyMetadataToUi(ExperimentMetadataDraft metadata)
        {
            FileNameInput.Text = metadata.Filename;
            DescriptionInput.Text = metadata.Description;
            TemperatureInput.Text = FormatOptionalDouble(metadata.Temperature_K);
            OnSamplePowerInput.Text = FormatOptionalDouble(metadata.OnSamplePower_mW);
            PowerDetectorAttenuatorAppliedCheckBox.IsChecked = metadata.PowerDetectorAttenuatorApplied;
            EnableFFT = metadata.EnableFFT;
            EnsureIniValue(RuntimeStreamIniPath, "StmConfig", "SaveToFile", EnableFFT ? "1" : "0");
            UsedOpoCheckBox.IsChecked = metadata.UsedOpo;
            LaserWavelengthInput.Text = FormatOptionalDouble(metadata.LaserWavelength_nm);
            SetComboBoxSelection(DetectorSelection, DetectorTypes.NormalizeDetector(metadata.Detector));
            SetCheckedItems(SampleCheckBoxList, metadata.Samples);
            SetCheckedItems(MetadataCheckBoxList, metadata.Tags);
        }

        private void SaveRecentMetadata(ExperimentMetadataDraft metadata)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentMetadataPath)!);
                string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(RecentMetadataPath, json);
            }
            catch (Exception ex)
            {
                AppendMessage($"Could not save recent metadata: {ex.Message}");
            }
        }

        private double GetSelectedDetectorResponsivity()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(GetSelectedDetectorResponsivity);
            }

            string detector = (DetectorSelection.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DetectorTypes.Si;
            return DetectorTypes.GetResponsivity(detector, GetLaserWavelengthNm());
        }

        private double GetLaserWavelengthNm()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(GetLaserWavelengthNm);
            }

            double? wavelength = ParseOptionalDouble(LaserWavelengthInput.Text);
            if (wavelength > 0)
            {
                return wavelength.Value;
            }

            return DetectorTypes.GetDefaultWavelength(GetSelectedDetector());
        }

        private string GetSelectedDetector()
        {
            string detector = (DetectorSelection.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DetectorTypes.Si;
            return DetectorTypes.NormalizeDetector(detector);
        }

        private static double? ParseOptionalDouble(string text)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : null;
        }

        private static string FormatOptionalDouble(double? value)
        {
            return value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static List<string> GetCheckBoxOptions(Panel source)
        {
            return source.Children
                .OfType<CheckBox>()
                .Select(checkBox => checkBox.Content?.ToString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        private static void SetCheckedItems(Panel source, IEnumerable<string> selected)
        {
            var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var checkBox in source.Children.OfType<CheckBox>())
            {
                string content = checkBox.Content?.ToString() ?? string.Empty;
                checkBox.IsChecked = selectedSet.Contains(content);
            }
        }

        private static void SetComboBoxSelection(ComboBox comboBox, string selected)
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Content?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }
    }
}
