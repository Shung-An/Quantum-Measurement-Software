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
        private void PlotFFTResult_Click(object sender, RoutedEventArgs e)
        {
            PlotSavedFFTResults();
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


        private double Interpolate(double[] array, double index)
        {
            int i = (int)Math.Floor(index);
            if (i < 0) return array[0];
            if (i >= array.Length - 1) return array[^1];
            double frac = index - i;
            return array[i] * (1 - frac) + array[i + 1] * frac;
        }


        private List<(double fMHz, double dB)> FindPeaks(double[] freqsMHz, double[] linearPower, int minSeparationMHz = 10, double minProminenceDb = 6)
        {
            List<(double fMHz, double dB)> peaks = new();

            int N = linearPower.Length;
            for (int i = 1; i < N - 1; i++)
            {
                double y0 = linearPower[i - 1];
                double y1 = linearPower[i];
                double y2 = linearPower[i + 1];

                // Check for local max
                if (y1 <= y0 || y1 <= y2) continue;

                double dB = 10 * Math.Log10(y1 + 1e-12);

                // Check prominence
                double baseline = 10 * Math.Log10(Math.Max(y0, y2) + 1e-12);
                if ((dB - baseline) < minProminenceDb) continue;

                // Parabolic interpolation for sub-bin accuracy
                double delta = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2 + 1e-12);
                double refinedIndex = i + delta;

                if (refinedIndex < 0 || refinedIndex > N - 1) continue;

                double fMHz = Interpolate(freqsMHz, refinedIndex);
                double refinedPower = Interpolate(linearPower, refinedIndex);
                double refinedDb = 10 * Math.Log10(refinedPower + 1e-12);

                peaks.Add((fMHz, refinedDb));
            }

            // Remove peaks that are too close to each other
            List<(double fMHz, double dB)> filtered = new();
            foreach (var peak in peaks.OrderByDescending(p => p.dB))
            {
                if (filtered.All(p => Math.Abs(p.fMHz - peak.fMHz) > minSeparationMHz))
                    filtered.Add(peak);
            }

            return filtered;
        }




        private async void PlotSavedFFTResults()
        {
            string fileA = @"C:\Quantum Squeezing\Quantum-Measurement-Software\Quantum Measurement UI\bin\Debug\net8.0-windows7.0\Data_1_1.bin";
            string fileB = @"C:\Quantum Squeezing\Quantum-Measurement-Software\Quantum Measurement UI\bin\Debug\net8.0-windows7.0\Data_1_2.bin";
            int fftLength = 8192;
            int fftResultSize = fftLength / 2 + 1;
            double Fs = 608e6;

            AppendMessage("⏳ Processing FFT and saving PNG...");
            LogExperimentEvent("⏳ Processing FFT and saving PNG...");

            (double[] freqs, double[] dbA, double[] dbB,
   (double fA, double dBA) peakA, (double fB, double dBB) peakB,
   List<(double f, double dB)> peaksA, List<(double f, double dB)> peaksB) = await Task.Run(() =>
   {
       double[] avg1 = LoadAndAverage(fileA, fftResultSize);
       double[] avg2 = LoadAndAverage(fileB, fftResultSize);

       double[] freqsMHz = Enumerable.Range(0, fftResultSize)
                           .Select(i => i * Fs / fftLength / 1e6)
                           .ToArray();

       var allPeaksA = FindPeaks(freqsMHz, avg1, minSeparationMHz: 20, minProminenceDb: 6)
                           .OrderByDescending(p => p.dB)
                           .Take(5)
                           .ToList();

       var allPeaksB = FindPeaks(freqsMHz, avg2, minSeparationMHz: 20, minProminenceDb: 6)
                           .OrderByDescending(p => p.dB)
                           .Take(5)
                           .ToList();



       var peak1 = allPeaksA.OrderByDescending(p => p.dB).FirstOrDefault();
       var peak2 = allPeaksB.OrderByDescending(p => p.dB).FirstOrDefault();

       double[] dB1 = avg1.Select(x => 10 * Math.Log10(x + 1e-12)).ToArray();
       double[] dB2 = avg2.Select(x => 10 * Math.Log10(x + 1e-12)).ToArray();

       return (freqsMHz, dB1, dB2, peak1, peak2, allPeaksA, allPeaksB);
   });


            string title1 = $"Ch1 Peak @ {peakA.fA:F1} MHz ({peakA.dBA:F1} dB)";
            string title2 = $"Ch2 Peak @ {peakB.fB:F1} MHz ({peakB.dBB:F1} dB)";

            var model = new PlotModel
            {
                Title = "FFT Spectrum",
                Background = OxyColors.White
            };

            var legend = new Legend
            {
                LegendPlacement = LegendPlacement.Inside,
                LegendPosition = LegendPosition.TopRight,
                LegendOrientation = LegendOrientation.Vertical,
                LegendFontSize = 12,
            };

            model.Legends.Add(legend);  // ✅ Add legend object to the model


            model.Axes.Add(new LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Bottom,
                Title = "Frequency (MHz)",
                Minimum = 0,
                Maximum = Fs / 2e6,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            });

            model.Axes.Add(new LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Left,
                Title = "Magnitude (dB)",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            });

            model.Series.Add(new OxyPlot.Series.LineSeries
            {
                Title = "Channel 1",  // ✅ Will show in legend
                Color = OxyColors.SkyBlue,
                StrokeThickness = 1,
                ItemsSource = freqs.Select((f, i) => new DataPoint(f, dbA[i]))
            });

            model.Series.Add(new OxyPlot.Series.LineSeries
            {
                Title = "Channel 2",  // ✅ Will show in legend
                Color = OxyColor.FromAColor(180, OxyColors.OrangeRed),  // 180/255 alpha
                StrokeThickness = 1,
                ItemsSource = freqs.Select((f, i) => new DataPoint(f, dbB[i]))
            });




            // === Export to PNG ===
            string outputDir = Path.GetDirectoryName(experimentLogFilePath);
            string outputPath = Path.Combine(outputDir, "fft_result.png");

            using (var stream = File.Create(outputPath))
            {
                var exporter = new OxyPlot.SkiaSharp.PngExporter
                {
                    Width = 1920,
                    Height = 1080,
                    Dpi = 200
                };
                exporter.Export(model, stream);
            }

            AppendMessage($"✅ FFT chart saved to {outputPath}");
            LogExperimentEvent($"✅ FFT chart saved to {outputPath}");
        }



        private double[] LoadAndAverage(string path, int fftResultSize, int chunkSize = 1000)
        {
            long totalPoints = new FileInfo(path).Length / 8;
            long totalFrames = totalPoints / fftResultSize;

            double[] avg = new double[fftResultSize];
            double[] buffer = new double[fftResultSize * chunkSize];

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            int framesProcessed = 0;

            while (framesProcessed < totalFrames)
            {
                int framesToRead = (int)Math.Min(chunkSize, totalFrames - framesProcessed);
                int count = framesToRead * fftResultSize;

                byte[] bytes = br.ReadBytes(count * sizeof(double));
                if (bytes.Length < count * sizeof(double)) break;

                Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);

                for (int i = 0; i < framesToRead; i++)
                    for (int j = 0; j < fftResultSize; j++)
                        avg[j] += buffer[i * fftResultSize + j];

                framesProcessed += framesToRead;
            }

            for (int j = 0; j < fftResultSize; j++)
                avg[j] /= totalFrames;

            return avg;
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
