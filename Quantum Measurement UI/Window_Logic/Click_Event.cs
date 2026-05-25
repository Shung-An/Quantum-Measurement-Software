using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Controls;
using QuantumSqueezingUI;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using OxyPlot.SkiaSharp;
using OxyPlot.Annotations;
using OxyPlot.Legends;
using MathNet.Numerics.IntegralTransforms;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;




namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window
    {
        #region Click Event Handlers

        /// <summary>
        /// Event handler for the Start button click.
        /// </summary>
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (isExperimentRunning)
            {
                AppendMessage("Experiment is already running.");
            }
            // 2. SAFETY CHECK: Check if Alignment is currently running
            if (isAlignmentRunning)
            {
                AppendMessage("Cannot start Experiment: Alignment is currently active. Please turn off Alignment first.");
                return;
            }
            if (!RequestMetadataBeforeExperimentStart())
            {
                AppendMessage("Experiment start canceled before metadata was submitted.");
                return;
            }
            else
            {
                await StartExperimentAsync();
            }
        }

        /// <summary>
        /// Event handler for the Terminate button click.
        /// </summary>
        private async void TerminateButton_Click(object sender, RoutedEventArgs e)
        {
            await TerminateExperimentAsync();
        }

        private async void StartAlignmentButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Check if Alignment is already running
            if (isAlignmentRunning)
            {
                AppendMessage("Alignment is already active.");
                return;
            }

            // 2. SAFETY CHECK: Check if Experiment is currently running
            if (isExperimentRunning)
            {
                AppendMessage("Cannot start Alignment: Experiment is currently active. Please turn off the Experiment first.");
                return;
            }

            // 3. Start Alignment
            try
            {
                isAlignmentRunning = true;

                // Define this method similarly to StartExperimentAsync
                await StartAlignmentAsync();

                AppendMessage("Alignment started (40 MHz).");
            }
            catch (Exception ex)
            {
                isAlignmentRunning = false;
                AppendMessage($"Error starting alignment: {ex.Message}");
            }
        }

        private async void StopAlignmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isAlignmentRunning)
            {
                AppendMessage("Alignment is not currently active.");
                return;
            }

            // Define this method similarly to TerminateExperimentAsync
            await StopAlignmentAsync();

            isAlignmentRunning = false;
            AppendMessage("Alignment stopped.");
        }

        /// <summary>
        /// Event handler for the Pause button click.
        /// </summary>
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            isPaused = true;
            AppendMessage("Visualization paused.");
        }

        /// <summary>
        /// Event handler for the Resume button click.
        /// </summary>
        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            isPaused = false;
            AppendMessage("Visualization resumed.");
        }


        /// <summary>
        /// Retrieves a list of all checked tags from the UI.
        /// </summary>
        private List<string> GetSelectedTags()
        {
            List<string> selectedTags = new List<string>();

            // Iterate through all items in the StackPanel defined in XAML
            foreach (var child in MetadataCheckBoxList.Children)
            {
                if (child is CheckBox checkBox && checkBox.IsChecked == true)
                {
                    selectedTags.Add(checkBox.Content.ToString());
                }
            }

            return selectedTags;
        }

        private async Task RunMatlabAnalysisAsync(string resultFolderPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    string scriptDirectory = @"C:\Quantum Squeezing\prototype and postprocessing\post processing";
                    string scriptPath = System.IO.Path.Combine(scriptDirectory, "cm_pipeline_all_in_one.py");

                    if (!System.IO.File.Exists(scriptPath))
                    {
                        throw new FileNotFoundException($"Python post-processing script not found: {scriptPath}");
                    }

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{scriptPath}\" \"{resultFolderPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = scriptDirectory
                    };

                    AppendMessage("Launching Python post-processing...");
                    using (Process python = Process.Start(startInfo))
                    {
                        // Fire-and-forget to match the old MATLAB behavior.
                    }

                    AppendMessage("Python post-processing command sent.");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendMessage($"Failed to launch Python post-processing: {ex.Message}"));
                }
            });
        }

        private void AnalyzeRunButton_Click()
        {
            // 1. Get the path
            // Assuming 'resultsBaseDirectory' and 'experimentLogDirectory' are your global variables
            string fullPath = System.IO.Path.Combine(resultsBaseDirectory, experimentLogDirectory);

            // 2. Run Analysis
            var analyzer = new QuantumAnalysisService();

            // Run in background so UI doesn't freeze
            Task.Run(() =>
            {
                var result = analyzer.RunPipeline(fullPath);

                Dispatcher.Invoke(() =>
                {
                    if (result.Success)
                        AppendMessage("Analysis Generated: check folder for PNGs.");
                    else
                        AppendMessage(result.Message);
                });
            });
        }

        /// <summary>
        /// Event handler for the Move Relative button click.
        /// </summary>
        private async void MoveRelativeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int motorNumber = GetSelectedMotor();
                int relativeSteps = int.Parse(RelativeSteps.Text); // Get relative steps from TextBox

                // Perform the relative move on the UI thread
                bool moveStatus = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    moveStatus = motorController.MoveRelative(motorNumber, relativeSteps);
                });

                if (!moveStatus)
                {
                    AppendMessage("Failed to move the motor.");
                    return;
                }

                // Wait until motion is done
                bool isMotionDone = false;
                while (!isMotionDone)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                         motorController.CheckForErrors();
                        motorController.IsMotionDone(motorNumber, out isMotionDone);
                    });
                    await Task.Delay(50);
                }

                int currentPosition = 0;
                await Dispatcher.InvokeAsync(() =>
                {
                    motorController.GetCurrentPosition(motorNumber, out currentPosition);
                });

                AppendMessage($"Moved motor {motorNumber} by {relativeSteps} steps to position {currentPosition}.");
                LogExperimentEvent($"Moved motor {motorNumber} by {relativeSteps} steps to position {currentPosition}.");

                // Current position will be updated automatically
            }
            catch (Exception ex)
            {
                AppendMessage($"Error: {ex.Message}");
            }
        }



        private void MovePlus10_Click(object sender, RoutedEventArgs e)
        {
            motorController.MovePlus10(esp300Controller.Axis);
        }

        private void MoveMinus10_Click(object sender, RoutedEventArgs e)
        {
            motorController.MoveMinus10(esp300Controller.Axis);
        }

        private void MovePlus1_Click(object sender, RoutedEventArgs e)
        {
            motorController.MovePlus1(esp300Controller.Axis);
        }

        private void MoveMinus1_Click(object sender, RoutedEventArgs e)
        {
            motorController.MoveMinus1(esp300Controller.Axis);
        }

        private void MovePlus10_Click_2(object sender, RoutedEventArgs e)
        {
            motorController.MovePlus10(2);
        }

        private void MoveMinus10_Click_2(object sender, RoutedEventArgs e)
        {
            motorController.MoveMinus10(2);
        }

        private void MovePlus1_Click_2(object sender, RoutedEventArgs e)
        {
            motorController.MovePlus1(2);
        }

        private void MoveMinus1_Click_2(object sender, RoutedEventArgs e)
        {
            motorController.MoveMinus1(2);
        }




        #region FFT Event Handlers
        /// <summary>
        /// Event handler for running the FFT executable.
        /// </summary>
        private async void PlotFFTResult_Click(object sender, RoutedEventArgs e)
        {
            string runFolder = CurrentExperimentFolderPath;
            if (string.IsNullOrWhiteSpace(runFolder) || !Directory.Exists(runFolder))
            {
                AppendMessage("No active experiment folder was found for FFT analysis.");
                return;
            }

            await PromptAndRunRawInterleavedFftAsync(runFolder);
        }

        private void EnableFFTCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            EnableFFT = true;
            AppendMessage("FFT enabled.");
        }

        private void EnableFFTCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            EnableFFT = false;
            AppendMessage("FFT disabled.");
        }


        // Example: call this from wherever you used to run FFT automatically
        private async Task MaybeRunFFTAsync(string fftExePath)
        {
            if (!EnableFFT) return;              // <-- gate by checkbox
            await RunFFTAndWaitAsync(fftExePath);
        }


        private async Task<bool> RunFFTAndWaitAsync(string fftExePath)
        {
            try
            {
                if (!File.Exists(fftExePath))
                {
                    AppendMessage("⚠️ FFT executable not found: " + fftExePath);
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = fftExePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var fftProcess = Process.Start(startInfo);

                AppendMessage("⏳ Waiting for FFT to finish...");
                LogExperimentEvent("⏳ Waiting for FFT to finish...");

                await fftProcess.WaitForExitAsync();  // ⚠️ .NET 6+ required

                AppendMessage("✅ FFT completed.");
                LogExperimentEvent("✅ FFT completed.");
                return true;
            }
            catch (Exception ex)
            {
                AppendMessage("❌ FFT process failed: " + ex.Message);
                LogExperimentEvent("❌ FFT process failed: " + ex.Message);
                return false;
            }
        }


        private enum RawInterleavedFftMode
        {
            LowFrequencyNoise,
            HighFrequencySpectrum
        }

        private sealed record RawInterleavedFftSeries(string Name, double[] FrequencyHz, double[] Psd);

        private sealed record RawInterleavedFftResult(
            string RunFolder,
            RawInterleavedFftMode Mode,
            string PngPath,
            string CsvPath,
            int FftLength,
            int InterleavedChannels,
            int MaxFramesPerFile,
            double SampleRateHz,
            int SeriesCount,
            List<string> SeriesNames);

        private async Task PromptAndRunRawInterleavedFftAsync(string runFolder)
        {
            RawInterleavedFftMode? selectedMode = await Dispatcher.InvokeAsync(AskRawInterleavedFftMode);
            if (selectedMode == null)
            {
                AppendMessage("FFT analysis skipped.");
                return;
            }

            try
            {
                AppendMessage($"Starting {GetRawInterleavedFftModeLabel(selectedMode.Value)} FFT analysis...");
                RawInterleavedFftResult result = await Task.Run(() => AnalyzeRawInterleavedFft(runFolder, selectedMode.Value));
                UpdateRawInterleavedFftMetadata(result);

                AppendMessage($"FFT analysis saved: {result.PngPath}");
                AppendMessage($"FFT data saved: {result.CsvPath}");
            }
            catch (Exception ex)
            {
                AppendMessage("FFT analysis failed: " + ex.Message);
                LogExperimentEvent("FFT analysis failed: " + ex.Message);
            }
        }

        private RawInterleavedFftMode? AskRawInterleavedFftMode()
        {
            MessageBoxResult result = MessageBox.Show(
                "Choose the raw interleaved FFT analysis to run.\n\nYes: Low frequency noise, log-log plot\nNo: High frequency spectrum, semilog plot\nCancel: Skip FFT analysis",
                "FFT Analysis",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            return result switch
            {
                MessageBoxResult.Yes => RawInterleavedFftMode.LowFrequencyNoise,
                MessageBoxResult.No => RawInterleavedFftMode.HighFrequencySpectrum,
                _ => null
            };
        }

        private RawInterleavedFftResult AnalyzeRawInterleavedFft(string runFolder, RawInterleavedFftMode mode)
        {
            const int interleavedChannels = 2;
            const int maxFramesPerFile = 512;

            List<string> rawFiles = Directory.EnumerateFiles(runFolder, "Data_*.bin")
                .Where(path => new FileInfo(path).Length >= interleavedChannels * sizeof(short) * 4096L)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rawFiles.Count == 0)
            {
                throw new InvalidOperationException("No raw Data_*.bin files were found in the run folder.");
            }

            double sampleRateHz = ReadConfiguredSampleRateHz();
            int desiredFftLength = mode == RawInterleavedFftMode.LowFrequencyNoise ? 262144 : 65536;
            int fftLength = ChooseFftLength(rawFiles, desiredFftLength, interleavedChannels);

            List<RawInterleavedFftSeries> series = new();
            foreach (string rawFile in rawFiles)
            {
                series.AddRange(ComputeRawInterleavedSpectra(rawFile, fftLength, interleavedChannels, sampleRateHz, maxFramesPerFile));
            }

            if (series.Count == 0)
            {
                throw new InvalidOperationException("The raw files were too short for FFT analysis.");
            }

            string outputDir = Path.Combine(runFolder, "fft_analysis");
            Directory.CreateDirectory(outputDir);

            string fileStem = mode == RawInterleavedFftMode.LowFrequencyNoise
                ? "interleaved_fft_low_frequency_loglog"
                : "interleaved_fft_high_frequency_semilog";
            string pngPath = Path.Combine(outputDir, fileStem + ".png");
            string csvPath = Path.Combine(outputDir, fileStem + ".csv");

            SaveRawInterleavedFftCsv(csvPath, series, mode, sampleRateHz);
            SaveRawInterleavedFftPlot(pngPath, series, mode, fftLength, sampleRateHz);

            return new RawInterleavedFftResult(
                runFolder,
                mode,
                pngPath,
                csvPath,
                fftLength,
                interleavedChannels,
                maxFramesPerFile,
                sampleRateHz,
                series.Count,
                series.Select(item => item.Name).ToList());
        }

        private List<RawInterleavedFftSeries> ComputeRawInterleavedSpectra(
            string rawFile,
            int fftLength,
            int interleavedChannels,
            double sampleRateHz,
            int maxFramesPerFile)
        {
            int usefulBins = fftLength / 2;
            long frameBytes = (long)fftLength * interleavedChannels * sizeof(short);
            int frameByteCount = checked((int)frameBytes);
            long totalFrames = new FileInfo(rawFile).Length / frameBytes;
            if (totalFrames <= 0)
            {
                return new List<RawInterleavedFftSeries>();
            }

            int framesToUse = (int)Math.Min(totalFrames, maxFramesPerFile);
            long frameStep = Math.Max(1, totalFrames / framesToUse);
            double[] window = CreateHannWindow(fftLength);
            double windowPower = window.Sum(value => value * value);

            double[][] accumulatedPsd = Enumerable.Range(0, interleavedChannels)
                .Select(_ => new double[usefulBins])
                .ToArray();

            byte[] byteBuffer = new byte[frameByteCount];
            short[] sampleBuffer = new short[fftLength * interleavedChannels];
            Complex[] fftBuffer = new Complex[fftLength];

            using FileStream stream = new(rawFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int framesProcessed = 0;
            for (int frameNumber = 0; frameNumber < framesToUse; frameNumber++)
            {
                long frameIndex = Math.Min(frameNumber * frameStep, totalFrames - 1);
                stream.Position = frameIndex * frameBytes;
                int bytesRead = ReadFullBuffer(stream, byteBuffer);
                if (bytesRead < frameByteCount)
                {
                    continue;
                }

                Buffer.BlockCopy(byteBuffer, 0, sampleBuffer, 0, byteBuffer.Length);

                for (int channel = 0; channel < interleavedChannels; channel++)
                {
                    double mean = 0.0;
                    for (int i = 0; i < fftLength; i++)
                    {
                        mean += sampleBuffer[i * interleavedChannels + channel];
                    }
                    mean /= fftLength;

                    for (int i = 0; i < fftLength; i++)
                    {
                        double centeredSample = sampleBuffer[i * interleavedChannels + channel] - mean;
                        fftBuffer[i] = new Complex(centeredSample * window[i], 0.0);
                    }

                    Fourier.Forward(fftBuffer, FourierOptions.Matlab);
                    for (int bin = 1; bin < usefulBins; bin++)
                    {
                        double magnitudeSquared = fftBuffer[bin].Real * fftBuffer[bin].Real
                            + fftBuffer[bin].Imaginary * fftBuffer[bin].Imaginary;
                        accumulatedPsd[channel][bin] += 2.0 * magnitudeSquared / (sampleRateHz * windowPower);
                    }
                }

                framesProcessed++;
            }

            if (framesProcessed == 0)
            {
                return new List<RawInterleavedFftSeries>();
            }

            double[] frequencyHz = Enumerable.Range(0, usefulBins)
                .Select(bin => bin * sampleRateHz / fftLength)
                .ToArray();

            string fileName = Path.GetFileNameWithoutExtension(rawFile);
            List<RawInterleavedFftSeries> spectra = new();
            for (int channel = 0; channel < interleavedChannels; channel++)
            {
                for (int bin = 1; bin < usefulBins; bin++)
                {
                    accumulatedPsd[channel][bin] = Math.Max(accumulatedPsd[channel][bin] / framesProcessed, 1e-30);
                }

                spectra.Add(new RawInterleavedFftSeries(
                    $"{fileName} interleaved ch{channel + 1}",
                    frequencyHz,
                    accumulatedPsd[channel]));
            }

            return spectra;
        }

        private static int ReadFullBuffer(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    break;
                }
                offset += read;
            }
            return offset;
        }

        private static double[] CreateHannWindow(int length)
        {
            double[] window = new double[length];
            for (int i = 0; i < length; i++)
            {
                window[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (length - 1));
            }
            return window;
        }

        private static int ChooseFftLength(List<string> rawFiles, int desiredFftLength, int interleavedChannels)
        {
            long maxPerChannelSamples = rawFiles
                .Select(path => new FileInfo(path).Length / (sizeof(short) * interleavedChannels))
                .DefaultIfEmpty(0)
                .Max();

            int fftLength = desiredFftLength;
            while (fftLength > 4096 && maxPerChannelSamples < fftLength)
            {
                fftLength /= 2;
            }

            if (maxPerChannelSamples < fftLength)
            {
                throw new InvalidOperationException("The raw files do not contain enough samples for a 4096-point FFT.");
            }

            return fftLength;
        }

        private double ReadConfiguredSampleRateHz()
        {
            try
            {
                if (File.Exists(RuntimeStreamIniPath))
                {
                    foreach (string line in File.ReadLines(RuntimeStreamIniPath))
                    {
                        string trimmed = line.Trim();
                        if (!trimmed.StartsWith("SampleRate=", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string valueText = trimmed.Split('=', 2)[1].Trim();
                        if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double sampleRate)
                            && sampleRate > 0)
                        {
                            return sampleRate;
                        }
                    }
                }
            }
            catch
            {
                // Fall back below; FFT analysis can still run with the default hardware rate.
            }

            return 608e6;
        }

        private void SaveRawInterleavedFftPlot(
            string pngPath,
            List<RawInterleavedFftSeries> spectra,
            RawInterleavedFftMode mode,
            int fftLength,
            double sampleRateHz)
        {
            bool lowFrequencyMode = mode == RawInterleavedFftMode.LowFrequencyNoise;
            double minFrequencyHz = lowFrequencyMode ? 1e3 : 1e6;
            double maxFrequencyHz = sampleRateHz / 2.0;

            var model = new PlotModel
            {
                Title = $"{GetRawInterleavedFftModeLabel(mode)} - raw interleaved FFT, bin width {sampleRateHz / fftLength:F1} Hz",
                Background = OxyColors.White
            };

            model.Legends.Add(new Legend
            {
                LegendPlacement = LegendPlacement.Inside,
                LegendPosition = LegendPosition.TopRight,
                LegendOrientation = LegendOrientation.Vertical,
                LegendFontSize = 11
            });

            if (lowFrequencyMode)
            {
                model.Axes.Add(new OxyPlot.Axes.LogarithmicAxis
                {
                    Position = OxyPlot.Axes.AxisPosition.Bottom,
                    Title = "Frequency (Hz)",
                    Minimum = minFrequencyHz,
                    Maximum = maxFrequencyHz,
                    MajorGridlineStyle = LineStyle.Solid,
                    MinorGridlineStyle = LineStyle.Dot
                });
            }
            else
            {
                model.Axes.Add(new OxyPlot.Axes.LinearAxis
                {
                    Position = OxyPlot.Axes.AxisPosition.Bottom,
                    Title = "Frequency (MHz)",
                    Minimum = minFrequencyHz / 1e6,
                    Maximum = maxFrequencyHz / 1e6,
                    MajorGridlineStyle = LineStyle.Solid,
                    MinorGridlineStyle = LineStyle.Dot
                });
            }

            model.Axes.Add(new OxyPlot.Axes.LogarithmicAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Left,
                Title = "PSD (counts^2/Hz)",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            });

            OxyColor[] colors =
            {
                OxyColors.SkyBlue,
                OxyColors.OrangeRed,
                OxyColors.SeaGreen,
                OxyColors.MediumPurple,
                OxyColors.Goldenrod,
                OxyColors.Teal
            };

            for (int seriesIndex = 0; seriesIndex < spectra.Count; seriesIndex++)
            {
                RawInterleavedFftSeries spectrum = spectra[seriesIndex];
                var lineSeries = new OxyPlot.Series.LineSeries
                {
                    Title = spectrum.Name,
                    StrokeThickness = 1.2,
                    Color = colors[seriesIndex % colors.Length]
                };

                for (int i = 1; i < spectrum.FrequencyHz.Length; i++)
                {
                    double frequencyHz = spectrum.FrequencyHz[i];
                    if (frequencyHz < minFrequencyHz || frequencyHz > maxFrequencyHz)
                    {
                        continue;
                    }

                    double x = lowFrequencyMode ? frequencyHz : frequencyHz / 1e6;
                    lineSeries.Points.Add(new DataPoint(x, Math.Max(spectrum.Psd[i], 1e-30)));
                }

                model.Series.Add(lineSeries);
            }

            using FileStream stream = File.Create(pngPath);
            var exporter = new OxyPlot.SkiaSharp.PngExporter
            {
                Width = 1920,
                Height = 1080,
                Dpi = 200
            };
            exporter.Export(model, stream);
        }

        private static void SaveRawInterleavedFftCsv(
            string csvPath,
            List<RawInterleavedFftSeries> spectra,
            RawInterleavedFftMode mode,
            double sampleRateHz)
        {
            bool lowFrequencyMode = mode == RawInterleavedFftMode.LowFrequencyNoise;
            double minFrequencyHz = lowFrequencyMode ? 1e3 : 1e6;
            double maxFrequencyHz = lowFrequencyMode ? sampleRateHz / 2.0 : double.PositiveInfinity;

            var builder = new StringBuilder();
            builder.Append("Frequency_Hz");
            foreach (RawInterleavedFftSeries spectrum in spectra)
            {
                builder.Append(',');
                builder.Append(SanitizeCsvHeader(spectrum.Name));
            }
            builder.AppendLine();

            double[] frequencies = spectra[0].FrequencyHz;
            for (int i = 1; i < frequencies.Length; i++)
            {
                double frequencyHz = frequencies[i];
                if (frequencyHz < minFrequencyHz || frequencyHz > maxFrequencyHz)
                {
                    continue;
                }

                builder.Append(frequencyHz.ToString("G17", CultureInfo.InvariantCulture));
                foreach (RawInterleavedFftSeries spectrum in spectra)
                {
                    builder.Append(',');
                    builder.Append(Math.Max(spectrum.Psd[i], 1e-30).ToString("G17", CultureInfo.InvariantCulture));
                }
                builder.AppendLine();
            }

            File.WriteAllText(csvPath, builder.ToString());
        }

        private static string SanitizeCsvHeader(string header)
        {
            return header.Replace(",", "_");
        }

        private static string GetRawInterleavedFftModeLabel(RawInterleavedFftMode mode)
        {
            return mode == RawInterleavedFftMode.LowFrequencyNoise
                ? "Low frequency noise (log-log)"
                : "High frequency spectrum (semilog)";
        }

        private static string GetRawInterleavedFftModeKey(RawInterleavedFftMode mode)
        {
            return mode == RawInterleavedFftMode.LowFrequencyNoise
                ? "LowFrequencyNoise"
                : "HighFrequencySpectrum";
        }

        private static JsonArray CreateFftChannelMetadata(IEnumerable<string> seriesNames)
        {
            JsonArray channels = new();
            foreach (string seriesName in seriesNames)
            {
                int channelIndex = InferPhysicalFftChannelIndex(seriesName);
                channels.Add(new JsonObject
                {
                    ["ChannelIndex"] = channelIndex,
                    ["ChannelLabel"] = channelIndex > 0 ? $"Ch{channelIndex}" : "Ch?",
                    ["SeriesName"] = seriesName
                });
            }

            return channels;
        }

        private static int InferPhysicalFftChannelIndex(string seriesName)
        {
            Match dataMatch = Regex.Match(seriesName, @"\bData_(\d+)(?:_\d+)?\b", RegexOptions.IgnoreCase);
            Match channelMatch = Regex.Match(seriesName, @"\bch(?:annel)?\s*(\d+)\b", RegexOptions.IgnoreCase);
            if (!channelMatch.Success || !int.TryParse(channelMatch.Groups[1].Value, out int interleavedChannel))
            {
                return 0;
            }

            if (dataMatch.Success
                && int.TryParse(dataMatch.Groups[1].Value, out int dataIndex)
                && dataIndex is 1 or 2
                && interleavedChannel is 1 or 2)
            {
                return (dataIndex - 1) * 2 + interleavedChannel;
            }

            return interleavedChannel;
        }

        private void UpdateRawInterleavedFftMetadata(RawInterleavedFftResult result)
        {
            try
            {
                string metadataPath = Path.Combine(result.RunFolder, "metadata.json");
                JsonObject root = File.Exists(metadataPath)
                    ? JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject() ?? new JsonObject()
                    : new JsonObject();

                JsonObject configuration = root["Configuration"] as JsonObject ?? new JsonObject();
                root["Configuration"] = configuration;
                configuration["FFTEnabled"] = true;
                configuration["FFTProvider"] = "Quantum Measurement Software";
                configuration["FFTAnalysisApplied"] = true;
                configuration["FFTAnalysisMode"] = GetRawInterleavedFftModeLabel(result.Mode);
                configuration["FFTRawDataLayout"] = "Int16 raw samples interleaved by channel";
                configuration["FFTOutputPng"] = result.PngPath;
                configuration["FFTOutputCsv"] = result.CsvPath;

                string modeKey = GetRawInterleavedFftModeKey(result.Mode);
                string modeLabel = GetRawInterleavedFftModeLabel(result.Mode);
                JsonObject analysis = root["FFTAnalysis"] as JsonObject ?? new JsonObject();
                JsonObject modes = analysis["Modes"] as JsonObject ?? new JsonObject();
                JsonArray channels = CreateFftChannelMetadata(result.SeriesNames);
                int physicalChannels = channels
                    .Select(node => node?["ChannelIndex"]?.GetValue<int>() ?? 0)
                    .DefaultIfEmpty(0)
                    .Max();

                modes[modeKey] = new JsonObject
                {
                    ["Mode"] = modeLabel,
                    ["ModeKey"] = modeKey,
                    ["Provider"] = "Quantum Measurement Software",
                    ["OutputPng"] = result.PngPath,
                    ["OutputCsv"] = result.CsvPath,
                    ["FftLength"] = result.FftLength,
                    ["SampleRateHz"] = result.SampleRateHz,
                    ["MaxFramesPerFile"] = result.MaxFramesPerFile,
                    ["SeriesCount"] = result.SeriesCount,
                    ["Channels"] = channels,
                    ["AppliedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                JsonArray availableModes = new();
                foreach (KeyValuePair<string, JsonNode?> mode in modes)
                {
                    availableModes.Add(mode.Key);
                }

                analysis["Enabled"] = true;
                analysis["Applied"] = true;
                analysis["Provider"] = "Quantum Measurement Software";
                analysis["InputData"] = "Raw interleaved Data_*.bin files from the measurement folder";
                analysis["Mode"] = modeLabel;
                analysis["RawDataLayout"] = "Int16 raw samples interleaved by channel";
                analysis["InterleavedChannels"] = result.InterleavedChannels;
                analysis["PhysicalChannels"] = physicalChannels > 0 ? physicalChannels : null;
                analysis["SampleRateHz"] = result.SampleRateHz;
                analysis["FftLength"] = result.FftLength;
                analysis["MaxFramesPerFile"] = result.MaxFramesPerFile;
                analysis["SeriesCount"] = result.SeriesCount;
                analysis["OutputPng"] = result.PngPath;
                analysis["OutputCsv"] = result.CsvPath;
                analysis["DescriptionLabel"] = root["Description"]?.GetValue<string>() ?? "";
                analysis["Modes"] = modes;
                analysis["AvailableModes"] = availableModes;
                analysis["LastSyncedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                root["FFTAnalysis"] = analysis;

                File.WriteAllText(metadataPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                AppendMessage("FFT metadata update failed: " + ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// Event handler for the Move to Target button click.
        /// </summary>
        private async void MoveToTargetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int motorNumber = GetSelectedMotor();
                int targetPosition = int.Parse(PositionTarget.Text); // Get target position from TextBox

                // Move the motor to the target position on the UI thread
                bool moveStatus = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    moveStatus = motorController.MoveToPosition(motorNumber, targetPosition);
                });

                if (!moveStatus)
                {
                    AppendMessage("Failed to move the motor.");
                    return;
                }

                // Wait until motion is done
                bool isMotionDone = false;
                while (!isMotionDone)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        motorController.CheckForErrors();
                        motorController.IsMotionDone(motorNumber, out isMotionDone);
                    });
                    await Task.Delay(50);
                }

                AppendMessage($"Moved motor {motorNumber} to position {targetPosition}.");
                LogExperimentEvent($"Moved motor {motorNumber} to position {targetPosition}.");

                // Current position will be updated automatically
            }
            catch (Exception ex)
            {
                AppendMessage($"Error: {ex.Message}");
            }
        }



        /// <summary>
        /// Event handler for the Set Zero Position button click.
        /// </summary>
        private async void SetZeroPositionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int motorNumber = GetSelectedMotor();

                // Set the zero position on the UI thread
                bool status = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    status = motorController.SetZeroPosition(motorNumber);
                });

                if (!status)
                {
                    AppendMessage($"Failed to set zero position for motor {motorNumber}.");
                }
                else
                {
                    AppendMessage($"Set motor {motorNumber} position to zero.");
                    LogExperimentEvent($"Set motor {motorNumber} position to zero.");
                }

                // Current position will be updated automatically
            }
            catch (Exception ex)
            {
                AppendMessage($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Event handler for the Confirm Pixel Selection button click.
        /// </summary>
     /*   private void ConfirmPixelSelection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int row = int.Parse(RowInput.Text);
                int col = int.Parse(ColumnInput.Text);

                if (row < 0 || row > 7 || col < 0 || col > 7)
                {
                    AppendMessage("Row and Column values must be between 0 and 7.");
                    return;
                }

                selectedRow = row;
                selectedColumn = col;

                int index = selectedRow * 8 + selectedColumn; // Corrected: row-major order
                double selectedValue = corrMatrixBuffer[index];

                // Display the selected value
                SelectedPixelValue.Text = selectedValue.ToString("F2"); // Format with 2 decimal places

                // Clear the PixelValues series when a new pixel is selected
                PixelValues.Clear();
            }
            catch (Exception ex)
            {
                AppendMessage($"Error: {ex.Message}");
            }
        }
*/

        /// <summary>
        /// Event handler for the Start Motion button click.
        /// </summary>
        private void StartMotionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int timePerMoveMs = int.Parse(TimePerMove.Text); // Time between moves in milliseconds
                int stepsPerMove = int.Parse(StepsPerMove.Text); // Steps to move each time
                int totalNumberOfMoves = int.Parse(TotalNumberOfMoves.Text); // Total number of moves

                int motorNumber = GetSelectedMotor();

                int expectedposition = 0;
                motorController.GetCurrentPosition(motorNumber, out expectedposition);
                expectedposition += stepsPerMove * totalNumberOfMoves;
                // Start the automatic continuous motion
                StartAutomaticMotion(motorNumber, timePerMoveMs, stepsPerMove, totalNumberOfMoves);
                AppendMessage($"Started automatic motion for motor {motorNumber} with {totalNumberOfMoves} moves to expected position {expectedposition}.");
                LogExperimentEvent($"Started automatic motion for motor {motorNumber} with {totalNumberOfMoves} moves to expected position {expectedposition}.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Event handler for the Stop Motion button click.
        /// </summary>
        private async void StopMotionButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel the motion
            StopContinuousMotion();

            // Wait asynchronously for the motion to stop (non-blocking)
            await Task.Delay(1000);

            int currentPosition1 = 0;
            int currentPosition2 = 0;
            motorController.GetCurrentPosition(1, out currentPosition1);
            motorController.GetCurrentPosition(2, out currentPosition2);

            // Append messages and log the event
            AppendMessage("Automatic motion stopped.");
            AppendMessage($"Motor 1 current position: {currentPosition1}");
            AppendMessage($"Motor 2 current position: {currentPosition2}");
            LogExperimentEvent("Automatic motion stopped.");
            LogExperimentEvent($"Motor 1 current position: {currentPosition1}");
            LogExperimentEvent($"Motor 2 current position: {currentPosition2}");
        }

        /// <summary>
        /// Event handler for the Start Autobalance button click.
        /// </summary>
        private void StartAutobalanceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double threshold = double.Parse(ThresholdInput.Text);
                int numberOfSegments = int.Parse(NumSegments.Text);

                AppendMessage("Autobalance started.");
                LogExperimentEvent("Autobalance started.");
                autobalancer.Start(threshold, numberOfSegments);
            }
            catch (Exception ex)
            {
                AppendMessage($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Event handler for the Terminate Autobalance button click.
        /// </summary>
        private void TerminateAutobalanceButton_Click(object sender, RoutedEventArgs e)
        {
            autobalancer.Stop();
        }

        /// <summary>
        /// Event handler for the Apply button click (configuration settings).
        /// </summary>
        private void ApplyConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get values from UI
                int extClkValue = int.Parse(ExtClkTextBox.Text);
                int timeCounterValue = int.Parse(TimeCounterTextBox.Text);

                // Update the .ini file with the new values
                UpdateIniFile("Acquisition", "ExtClk", extClkValue.ToString());
                UpdateIniFile("StmConfig", "TimeCounter", timeCounterValue.ToString());

                // Provide feedback to the user
                AppendMessage("Configuration applied successfully.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error applying configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Event handler for the Reset Delay Stage button click.
        /// </summary>
        private async void ResetDelayStageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable the button during reset
                ResetDelayStageButton.IsEnabled = false;

                // Update status
                DelayStageStatusText.Text = "Resetting...";
                DelayStageStatusIndicator.Fill = Brushes.Yellow;

                // Log the reset action
                AppendMessage("Resetting ESP300 controller...");
                LogExperimentEvent("Resetting ESP300 controller...");

                // Perform the reset on a background thread to avoid UI freezing
                await Task.Run(() =>
                {
                    // Call the Reset method
                    esp300Controller.Reset();

                    // Reset takes about 20 seconds to complete according to comments in ESP300Controller.cs
                    // Log completion message
                    Dispatcher.Invoke(() =>
                    {
                        AppendMessage("ESP300 controller reset completed.");
                        LogExperimentEvent("ESP300 controller reset completed.");

                        // Update UI status
                        DelayStageStatusText.Text = "Ready";
                        DelayStageStatusIndicator.Fill = Brushes.Green;
                    });
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions
                AppendMessage($"Error during ESP300 controller reset: {ex.Message}");
                LogExperimentEvent($"Error during ESP300 controller reset: {ex.Message}");

                // Update UI to indicate error
                DelayStageStatusText.Text = "Error";
                DelayStageStatusIndicator.Fill = Brushes.Red;
            }
            finally
            {
                // Re-enable the button
                ResetDelayStageButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Resets the Matrix Balance Chart data, cumulative sums, frame counters, and UI text.
        /// </summary>
        private void ResetMatrixBalanceChart_Click(object sender, RoutedEventArgs e)
        {
            // Run on UI thread to safely update collections and UI
            Dispatcher.Invoke(() =>
            {
                // 1. Reset the raw bar chart values
                if (MatrixChartValues != null)
                {
                    MatrixChartValues.Clear();
                    for (int i = 0; i < 49; i++)
                    {
                        MatrixChartValues.Add(0.0);
                    }
                }

                // 2. Reset the Table Data and the History for the Plot
                if (MatrixTableData != null)
                {
                    foreach (var item in MatrixTableData)
                    {
                        item.Value = 0;
                        item.PhysicalValue = 0;
                        item.History.Clear(); // This clears the 100-point plot
                        item.HistoryBinCounts.Clear();
                        item.CurrentPositionCumulativeHistory.Clear();
                        item.CurrentPositionTrackedBin = double.NaN;
                        item.CurrentPositionAcceptedCount = 0;
                        item.CurrentPositionRunningAverage = 0.0;
                    }
                }

                // 3. Clear the active trend plots
                SelectedTrendPlotModel?.Series.Clear();
                SelectedTrendPlotModel?.InvalidatePlot(true);
                SelectedPositionAveragePlotModel?.Series.Clear();
                SelectedPositionAveragePlotModel?.InvalidatePlot(true);

                // 4. Reset internal cumulative sums and counters
                if (Cumulative49Channels != null)
                {
                    Array.Clear(Cumulative49Channels, 0, Cumulative49Channels.Length);
                }

                TotalFramesReceived = 0;
                TotalFramesSkipped = 0;
                ResetBackendFrameRateMetrics();

                // 5. Reset UI text
                SkippedFramesText.Text = "Processed: 0 | Backend FPS: 0.00 | Display FPS: 0.00";
                SelectedAccumulationText.Text = "Selected accumulation: none";
                SelectedPositionAverageText.Text = "Single-position cumulative average: none";

                AppendMessage("Matrix Balance and History Plot reset.");
            });
        }
        /// <summary>
        /// Resets the Integrated Column chart and the >9000 rejection counters.
        /// </summary>
        private void ResetIntegralChart_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                lastProcessedFrameCount = 0; // Reset the sync tracker
                TotalIntegralFrames = 0;

                IntegratedDataHistory?.Clear();
                _integratedPlotSeries?.Points.Clear();
                IntegratedPlotModel?.InvalidatePlot(true);
                IntegralRejectionStatsText.Text = "System Skip Rate: 0 / 0 (0.00%)";

                AppendMessage("Integral Chart synchronized and reset.");
            });
        }

        #endregion
    }
}
