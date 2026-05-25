using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Quantum_measurement_UI
{
    public partial class ExperimentMetadataDialog : Window
    {
        public ExperimentMetadataDraft Metadata { get; private set; }

        public ExperimentMetadataDialog(
            ExperimentMetadataDraft metadata,
            IEnumerable<string> sampleOptions,
            IEnumerable<string> tagOptions)
        {
            InitializeComponent();

            Metadata = metadata;
            DetectorComboBox.ItemsSource = DetectorTypes.All;

            FileNameBox.Text = metadata.Filename;
            DescriptionBox.Text = metadata.Description;
            TemperatureBox.Text = FormatNullable(metadata.Temperature_K);
            OnSamplePowerBox.Text = FormatNullable(metadata.OnSamplePower_mW);
            LaserWavelengthBox.Text = FormatNullable(metadata.LaserWavelength_nm);
            DetectorComboBox.SelectedItem = DetectorTypes.NormalizeDetector(metadata.Detector);
            UsedOpoCheckBox.IsChecked = metadata.UsedOpo;
            AttenuatorCheckBox.IsChecked = metadata.PowerDetectorAttenuatorApplied;
            EnableFftCheckBox.IsChecked = metadata.EnableFFT;

            PopulateChecks(SampleList, sampleOptions, metadata.Samples);
            PopulateChecks(TagList, tagOptions, metadata.Tags);
        }

        private static string FormatNullable(double? value)
        {
            return value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static void PopulateChecks(Panel target, IEnumerable<string> options, IEnumerable<string> selected)
        {
            var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string option in options)
            {
                target.Children.Add(new CheckBox
                {
                    Content = option,
                    IsChecked = selectedSet.Contains(option),
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }
        }

        private static List<string> ReadCheckedItems(Panel source)
        {
            return source.Children
                .OfType<CheckBox>()
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => checkBox.Content?.ToString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        private static bool TryReadOptionalDouble(TextBox source, string label, out double? value)
        {
            value = null;
            string text = source.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                MessageBox.Show($"{label} must be a number.", "Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
                source.Focus();
                return false;
            }

            value = parsed;
            return true;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadOptionalDouble(TemperatureBox, "Temperature", out double? temperatureK) ||
                !TryReadOptionalDouble(OnSamplePowerBox, "On-sample power", out double? onSamplePowerMw) ||
                !TryReadOptionalDouble(LaserWavelengthBox, "Laser wavelength", out double? laserWavelengthNm))
            {
                return;
            }

            if (laserWavelengthNm <= 0)
            {
                MessageBox.Show("Laser wavelength must be greater than zero.", "Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
                LaserWavelengthBox.Focus();
                return;
            }

            var samples = ReadCheckedItems(SampleList);
            if (samples.Count == 0)
            {
                samples.Add("Unknown");
            }

            Metadata = new ExperimentMetadataDraft
            {
                Filename = FileNameBox.Text.Trim(),
                Description = DescriptionBox.Text.Trim(),
                Temperature_K = temperatureK,
                OnSamplePower_mW = onSamplePowerMw,
                PowerDetectorAttenuatorApplied = AttenuatorCheckBox.IsChecked == true,
                EnableFFT = EnableFftCheckBox.IsChecked == true,
                Samples = samples,
                Tags = ReadCheckedItems(TagList),
                UsedOpo = UsedOpoCheckBox.IsChecked == true,
                LaserWavelength_nm = laserWavelengthNm,
                Detector = DetectorComboBox.SelectedItem?.ToString() ?? DetectorTypes.Si
            };

            DialogResult = true;
        }
    }
}
