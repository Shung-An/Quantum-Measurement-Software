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
using Microsoft.UI.Xaml;

namespace Quantum_measurement_UI
{
    public partial class MainWindow : System.Windows.Window
    {
        #region Experiment Control Functions

        /// <summary>
        /// Starts the experiment.
        /// </summary>
        private async Task StartExperimentAsync()
        {
            if (isExperimentRunning)
            {
                await TerminateExperimentAsync(); // Ensure previous experiment is terminated
            }

            try
            {
                // Create the run folder and point StreamThruGPU.ini at it before the backend starts.
                await AsyncInitializeExperimentLog();

                StartGageStreamProcess();   // Start the GageStreamThruGPU program, which is in the directory of the executable

                Task signal = Task.Run(() => Connection());
               
                await AsyncInitializePipeClient();     // Initialize the pipe client for communication

                // Fetch external clock value from the ini file
                extClkValue = GetExtClkValueFromIni();
                ExtClkStatusText.Text = extClkValue == 1 ? "On" : "Off";
                ExtClkStatusIndicator.Fill = extClkValue == 1 ? Brushes.Green : Brushes.Red;

                // Start elapsed time tracking
                experimentStartTime = DateTime.Now;
                isExperimentRunning = true;
                elapsedTimer.Start();

                // Update experiment status indicators
                ExperimentStatusText.Text = "On";
                ExperimentStatusIndicator.Fill = Brushes.Green;

                SaveExperimentMetadata(Path.Combine(resultsBaseDirectory, experimentLogDirectory), "00:00:00");
                LogExperimentEvent("Initial experiment metadata saved at acquisition start.");



                // Start the delay stage program

                await startDelayStageProgram();
                await Task.Delay(500); // Wait for 0.5 seconds to ensure the delay stage program is started
                await signal;
                window = new Mov_Avg(20);


                // Start the Daq Signal Check
                _ = Task.Run(() => ReadSignal());


                // Send a request to the server to start data acquisition
                byte[] request = BitConverter.GetBytes((short)1);  // The request to start experiment is 1
                await pipeClient.WriteAsync(request, 0, request.Length);      // Send the request

                byte[] expDirBytes = System.Text.Encoding.ASCII.GetBytes(experimentLogDirectory);
                await pipeClient.WriteAsync(expDirBytes, 0, expDirBytes.Length); // Send the experiment directory

                StartAutobalanceButton_Click(null, null); // Start the autobalancer
                await Task.Delay(5000);




                isPaused = false; // Data updates for signal chart and cross correlation matrix visualization can start
                AppendMessage("Gage Digitizer Data Acquisition started.");
                LogExperimentEvent("Gage Digitizer Data Acquisition started.");
                StartDAQButton_Click(this, null); // Start the DAQ process (if not already started)
                // Start data updates
                StartDataUpdates();
                // Start motor position updates automatically
                StartMotorPositionUpdates();

            }
            catch (Exception ex)
            {
                AppendMessage($"Failed to start experiment: {ex.Message}");
            }
        }

        /// <summary>
        /// Terminates the experiment.
        /// </summary>
        private async Task TerminateExperimentAsync()
        {
            try
            {
                // Stop all active processes
                cancellationTokenSource?.Cancel();               // Stop data updates
                motorPositionCancellationTokenSource?.Cancel();  // Stop motor position updates
                motionCancellationTokenSource?.Cancel();         // Stop automatic motion
                autobalancer?.Stop();                            // Stop autobalancer
                autoReadCts?.Cancel();
                signalReadCts?.Cancel();

                esp300Controller?.AbortProgram();                      // Stop ESP300 controller

                stopDelayStageProgram();                         // Stop delay stage program

                Release();
                
                // Give background loops a moment to exit gracefully
                await Task.Delay(500);

                TerminateAutobalanceButton_Click(null, null); // Stop the autobalancer

                // Tell the DAQ process to terminate
                if (pipeClient?.IsConnected == true)
                {
                    byte[] request = BitConverter.GetBytes((short)3); // terminate acquisition
                    await pipeClient.WriteAsync(request, 0, request.Length);
                    await pipeClient.FlushAsync();
                }

                await Task.Delay(500);

                // Close pipe
                autoReadCts?.Dispose();                          // Stop auto read
                autoReadCts = null;
                signalReadCts?.Dispose();
                signalReadCts = null;
                pipeClient?.Dispose();
                pipeClient = null;

                // Wait for GageStreamThruGPU.exe to exit
                if (gageStreamProcess != null && !gageStreamProcess.HasExited)
                {
                    await Task.Run(() => gageStreamProcess.WaitForExit());
                    gageStreamProcess.Dispose();
                    gageStreamProcess = null;
                }

                AppendMessage("Experiment terminated and GageStreamThruGPU.exe has exited.");
                LogExperimentEvent("Experiment terminated and GageStreamThruGPU.exe has exited.");

                LogExperimentEvent(EnableFFT
                    ? "Raw stream files were saved for Quantum Measurement Software FFT analysis."
                    : "FFT disabled; raw stream file saving was skipped.");

                // Close the experiment log
                if (experimentLogWriter != null)
                {
                    experimentLogWriter.WriteLine("\n--- Experiment End ---\n");
                    experimentLogWriter.Flush();
                    experimentLogWriter.Close();
                    experimentLogWriter = null;
                    // Close all log writers
                    motorMetricLogWriter?.Close();
                    sensitivityLogWriter?.Close();
                    droppedWindowLogWriter?.Close();
                }

                // Reset experiment status indicators
                isExperimentRunning = false;
                elapsedTimer.Stop();
                ExperimentStatusText.Text = "Off";
                ExperimentStatusIndicator.Fill = Brushes.Red;
                DelayStageStatusText.Text = "Off";
                DelayStageStatusIndicator.Fill = Brushes.Red;

                isPaused = true; // Pause data updates
                // Combine base path with the new folder name to get full path
                string fullResultPath = System.IO.Path.Combine(resultsBaseDirectory, experimentLogDirectory);
                SaveExperimentMetadata(fullResultPath, ElapsedTimeText.Text);

                if (EnableFFT)
                {
                    await PromptAndRunRawInterleavedFftAsync(fullResultPath);
                }

                /*RenameExperimentFolder();*/





                // Clear all charts/data on UI thread (null-safe)
                await Dispatcher.InvokeAsync(() =>
                {
                    ChannelAValues?.Clear();
                    ChannelBValues?.Clear();

                    heatValues?.Clear();
                    _signalSeriesA?.Points.Clear();
                    _signalSeriesB?.Points.Clear();
                    SignalPlotModel?.InvalidatePlot(true);
                    if (_heatmapSeries != null)
                    {
                        _heatmapSeries.Data = new double[8, 8];
                    }
                    HeatmapPlotModel?.InvalidatePlot(true);

                    MatrixChartValues?.Clear();
                    for (int i = 0; i < 49; i++)
                        MatrixChartValues?.Add(0.0);
                    TotalFramesReceived = 0;
                    TotalFramesSkipped = 0;
                    ResetBackendFrameRateMetrics();
                    RmsValues?.Clear();
                    for (int i = 0; i < 64; i++)
                        RmsValues?.Add(0.0);
                    if (_rmsSeries != null)
                    {
                        for (int i = 0; i < _rmsSeries.Points.Count; i++)
                        {
                            _rmsSeries.Points[i] = new OxyPlot.DataPoint(i, 0.0);
                        }
                    }
                    RmsPlotModel?.InvalidatePlot(true);

                    autobalancer?.MotorPositionValues1?.Clear();
                    autobalancer?.MotorPositionValues2?.Clear();
                    autobalancer?.MetricValuesA?.Clear();
                    autobalancer?.MetricValuesB?.Clear();


                    ElapsedTimeText.Text = "00:00:00";
                });
                await RunMatlabAnalysisAsync(fullResultPath);

            }

            catch (Exception ex)
            {
                AppendMessage($"Error during termination: {ex.Message}");
                LogExperimentEvent($"Error during termination: {ex.Message}");
            }

        }


        // Define a separate flag for alignment if you haven't already
        // private bool isAlignmentRunning = false;

        private async Task StartAlignmentAsync()
        {
            // 1. Safety Check: Ensure Experiment is not running
            if (isExperimentRunning)
            {
                AppendMessage("Cannot start Alignment while Experiment is running.");
                return;
            }

            // 2. Ensure previous alignment is terminated if somehow stuck
            if (isAlignmentRunning)
            {
                await StopAlignmentAsync();
            }

            try
            {
                // --- A. Hardware & Connection Setup ---
                StartGageStreamProcessForAlignment();   // Start the external Gage executable

                Task signal = Task.Run(() => Connection()); // Start connection task
                await AsyncInitializePipeClient();          // Initialize Pipe

                // --- B. UI & State Setup ---
                // Fetch external clock (useful to know even during alignment)
                extClkValue = GetExtClkValueFromIni();
                ExtClkStatusText.Text = extClkValue == 1 ? "On" : "Off";
                ExtClkStatusIndicator.Fill = extClkValue == 1 ? Brushes.Green : Brushes.Red;

                isAlignmentRunning = true; // Set Flag

                // Visual Feedback
                ExperimentStatusText.Text = "Aligning";
                ExperimentStatusIndicator.Fill = Brushes.Yellow; // Use Yellow to differentiate from Green (Experiment)

                // --- C. Logging Setup (Simplified for Alignment) ---
                // Create a temporary or specific alignment directory so we don't pollute experiment data
                string dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                experimentLogDirectory = $"{dateStr}";

                // Ensure directory exists
                string fullAlignmentPath = System.IO.Path.Combine(resultsBaseDirectory, experimentLogDirectory);
                System.IO.Directory.CreateDirectory(fullAlignmentPath);

                // Initialize basic log just for errors/events
                await AsyncInitializeExperimentLog();
               
                await startDelayStageProgram();

                // --- D. Start Sub-Systems ---
                await Task.Delay(500);
                await signal; // Wait for connection
                window = new Mov_Avg(20);


                // Start reading signals
                _ = Task.Run(() => ReadSignal());

                // --- E. Send Commands to C++ Server ---
                // Send Request to Start (1)
                byte[] request = BitConverter.GetBytes((short)1);
                await pipeClient.WriteAsync(request, 0, request.Length);

                // Send Directory (Pass the alignment specific path)
                byte[] expDirBytes = System.Text.Encoding.ASCII.GetBytes(experimentLogDirectory);
                await pipeClient.WriteAsync(expDirBytes, 0, expDirBytes.Length);

                // --- F. Start Control Loops ---
                // Depending on your needs, you might NOT want auto-balancing during alignment
                // if you are trying to manually tune it. I have commented it out by default.
                // StartAutobalanceButton_Click(null, null); 

                await Task.Delay(2000); // Short delay to let hardware settle

                isPaused = false; // Enable UI Chart Updates
                AppendMessage("Alignment Started: Real-time data active.");
                StartDAQButton_Click(this, null); // Start the DAQ process (if not already started)
                // Start Visualization Updates
                StartDataUpdates();
                StartMotorPositionUpdates();
            }
            catch (Exception ex)
            {
                isAlignmentRunning = false;
                AppendMessage($"Failed to start alignment: {ex.Message}");
            }
        }

        private async Task StopAlignmentAsync()
        {
            try
            {
                // --- A. Stop Processes & Tokens ---
                cancellationTokenSource?.Cancel();            // Stop data updates
                motorPositionCancellationTokenSource?.Cancel(); // Stop motor updates
                motionCancellationTokenSource?.Cancel();      // Stop any auto-motion

                // Stop Autobalancer if it was running
                autobalancer?.Stop();
                autoReadCts?.Cancel();
                signalReadCts?.Cancel();

                // Stop ESP300 and Delay Stage
                esp300Controller?.AbortProgram();
                stopDelayStageProgram();

                Release(); // Release resources

                await Task.Delay(500); // Grace period

                // --- B. Send Terminate Command to C++ Server ---
                if (pipeClient?.IsConnected == true)
                {
                    byte[] request = BitConverter.GetBytes((short)3); // Terminate acquisition (3)
                    await pipeClient.WriteAsync(request, 0, request.Length);
                    await pipeClient.FlushAsync();
                }

                await Task.Delay(500);

                // --- C. Cleanup Pipe & Process ---
                autoReadCts?.Dispose();
                autoReadCts = null;
                signalReadCts?.Dispose();
                signalReadCts = null;
                pipeClient?.Dispose();
                pipeClient = null;

                if (gageStreamProcess != null && !gageStreamProcess.HasExited)
                {
                    await Task.Run(() => gageStreamProcess.WaitForExit());
                    gageStreamProcess.Dispose();
                    gageStreamProcess = null;
                }

                AppendMessage("Alignment stopped.");

                // --- D. Close Logs ---
                if (experimentLogWriter != null)
                {
                    experimentLogWriter.WriteLine("\n--- Alignment End ---\n");
                    experimentLogWriter.Flush();
                    experimentLogWriter.Close();
                    experimentLogWriter = null;
                }
                // Close other specific logs
                motorMetricLogWriter?.Close();
                sensitivityLogWriter?.Close();
                droppedWindowLogWriter?.Close();

                // --- E. UI Reset ---
                isAlignmentRunning = false;

                ExperimentStatusText.Text = "Off";
                ExperimentStatusIndicator.Fill = Brushes.Red;
                DelayStageStatusText.Text = "Off";
                DelayStageStatusIndicator.Fill = Brushes.Red;

                isPaused = true;

                // --- F. Clear Charts (UI Thread) ---
                await Dispatcher.InvokeAsync(() =>
                {
                    ChannelAValues?.Clear();
                    ChannelBValues?.Clear();
                    heatValues?.Clear();
                    _signalSeriesA?.Points.Clear();
                    _signalSeriesB?.Points.Clear();
                    SignalPlotModel?.InvalidatePlot(true);
                    if (_heatmapSeries != null)
                    {
                        _heatmapSeries.Data = new double[8, 8];
                    }
                    HeatmapPlotModel?.InvalidatePlot(true);
                    PixelValues?.Clear();

                    // Reset metrics
                    PixelCumulativeSum = 0;
                    PixelCount = 0;
                });

                // NOTE: Skipped FFT and MATLAB analysis here because 
                // alignment is usually about visual confirmation, not post-processing.
            }
            catch (Exception ex)
            {
                AppendMessage($"Error during alignment termination: {ex.Message}");
            }
        }

        private double? TryReadESPScanVelocityMmPerS()
        {
            try
            {
                if (esp300Controller == null || !esp300Controller.IsConnected)
                {
                    return null;
                }

                string axisPrefix = esp300Controller.Axis.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string response = esp300Controller.Query($"{axisPrefix}VA?").Trim();
                if (double.TryParse(
                        response,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double velocity))
                {
                    return velocity;
                }

                if (double.TryParse(response, out velocity))
                {
                    return velocity;
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Could not read ESP scan velocity: {ex.Message}");
                LogExperimentEvent($"Could not read ESP scan velocity: {ex.Message}");
            }

            return null;
        }

        private void SaveExperimentMetadata(string folderPath, string elapsedTime)
        {
            double? temperatureK = null;
            if (double.TryParse(TemperatureInput.Text, out double parsedTemperature))
            {
                temperatureK = parsedTemperature;
            }

            double? onSamplePowerMw = null;
            if (double.TryParse(OnSamplePowerInput.Text, out double parsedOnSamplePower))
            {
                onSamplePowerMw = parsedOnSamplePower;
            }

            // Get lists from UI
            var samples = GetSelectedSamples();
            var tags = GetSelectedTags();
            bool powerDetectorAttenuatorApplied = PowerDetectorAttenuatorAppliedCheckBox.IsChecked == true;
            double powerDetectorAttenuatorTotalDb = powerDetectorAttenuatorApplied ? PowerDetectorAttenuatorTotalDb : 0.0;
            double powerDetectorAttenuatorCorrectionFactor = powerDetectorAttenuatorApplied ? PowerDetectorAttenuatorCorrectionFactor : 1.0;
            string detector = GetSelectedDetector();
            double? laserWavelengthNm = ParseOptionalDouble(LaserWavelengthInput.Text);
            double detectorResponsivity = DetectorTypes.GetResponsivity(detector, laserWavelengthNm);
            double? scanVelocityMmPerS = TryReadESPScanVelocityMmPerS();
            string rawDataFilePrefix = CurrentRawDataFilePrefix;
            bool rawStreamSaveToFile = this.EnableFFT && !string.IsNullOrWhiteSpace(rawDataFilePrefix);

            // Create the metadata object with all fields
            var meta = new
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Duration = elapsedTime,
                Filename = FileNameInput.Text,
                Description = DescriptionInput.Text,

                // Save as a comma-joined string (easier to read in Excel/History Grid)
                Sample = string.Join(", ", samples),
                Tags = tags,
                UsedOPO = UsedOpoCheckBox.IsChecked == true,
                LaserWavelength_nm = laserWavelengthNm,
                Detector = detector,
                DetectorResponsivity_A_per_W = detectorResponsivity,
                FFTEnabled = this.EnableFFT,
                RawStreamSaveToFile = rawStreamSaveToFile,
                RawDataFilePrefix = rawDataFilePrefix,
                RawDataFilePattern = rawStreamSaveToFile ? $"{rawDataFilePrefix}_*.bin" : null,
                                      

                // Machine Configuration (snapshot of current state)
                Configuration = new
                {
                    EnableFFT = this.EnableFFT,
                    FFTEnabled = this.EnableFFT,
                    FFTProvider = "Quantum Measurement Software",
                    ExternalFftExecutableUsed = false,
                    RawStreamSaveToFile = rawStreamSaveToFile,
                    RawDataFilePrefix = rawDataFilePrefix,
                    RawDataFilePattern = rawStreamSaveToFile ? $"{rawDataFilePrefix}_*.bin" : null,
                    StreamIniPath = RuntimeStreamIniPath,
                    ExternalClock = ExtClkStatusText.Text,
                    Motor1Position = CalibrationMotor1Pos.Text, // Assuming you have this
                    Motor2Position = CalibrationMotor2Pos.Text,
                    ExternalClockStatus = ExtClkStatusText.Text
                },
                FFTAnalysis = new
                {
                    Enabled = this.EnableFFT,
                    Applied = false,
                    Provider = "Quantum Measurement Software",
                    InputData = rawStreamSaveToFile ? "Raw interleaved Data_*.bin files from the measurement folder" : null,
                    RawDataLayout = rawStreamSaveToFile ? "Int16 raw samples interleaved by channel" : null,
                    InterleavedChannels = rawStreamSaveToFile ? 2 : (int?)null,
                    PhysicalChannels = rawStreamSaveToFile ? 4 : (int?)null,
                    DescriptionLabel = DescriptionInput.Text,
                    AvailableModes = Array.Empty<string>()
                },
                PhysicsData = new
                {
                    Temperature_K = temperatureK,
                    OnSamplePower_mW = onSamplePowerMw,
                    UsedOPO = UsedOpoCheckBox.IsChecked == true,
                    LaserWavelength_nm = laserWavelengthNm,
                    Detector = detector,
                    DetectorResponsivity_A_per_W = detectorResponsivity,
                    ScanVelocity_mm_s = scanVelocityMmPerS,
                    PowerDetectorAttenuatorApplied = powerDetectorAttenuatorApplied,
                    PowerDetectorAttenuatorCount = powerDetectorAttenuatorApplied ? PowerDetectorAttenuatorCount : 0,
                    PowerDetectorAttenuatorEach_dB = powerDetectorAttenuatorApplied ? PowerDetectorAttenuatorEachDb : 0.0,
                    PowerDetectorAttenuatorTotal_dB = powerDetectorAttenuatorTotalDb,
                    PowerDetectorAttenuatorCorrectionFactor = powerDetectorAttenuatorCorrectionFactor
                }
            };

            // Serialize to JSON and write to file
            string jsonString = System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(System.IO.Path.Combine(folderPath, "metadata.json"), jsonString);
        }

        #endregion

        #region Motor Control Functions

        /// <summary>
        /// Gets the selected motor number from the ComboBox.
        /// </summary>
        private int GetSelectedMotor()
        {
            return MotorSelection.SelectedIndex + 1; // Assuming the ComboBox for motor selection is 0-indexed
        }

        /// <summary>
        /// Starts the automatic continuous motion task.
        /// </summary>
        private void StartAutomaticMotion(int motorNumber, int timePerMoveMs, int stepsPerMove, int totalNumberOfMoves)
        {
            // Cancel any existing motion
            StopContinuousMotion();

            // Create a new CancellationTokenSource
            motionCancellationTokenSource = new CancellationTokenSource();

            // Start the motion task
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < totalNumberOfMoves; i++)
                    {
                        // Check for cancellation
                        if (motionCancellationTokenSource.Token.IsCancellationRequested)
                        {
                            break;
                        }

                        // Handle pause
                        lock (pauseLock)
                        {
                            while (isPaused)
                            {
                                Monitor.Wait(pauseLock);
                            }
                        }

                        bool moveStatus = false;

                        // Move the motor on the UI thread since motorController can only be accessed by one thread (UI Thread) at a time
                        await Dispatcher.InvokeAsync(() =>
                        {
                            moveStatus = motorController.MoveRelative(motorNumber, stepsPerMove);
                        });

                        if (!moveStatus)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                AppendMessage("Failed to move the motor.");
                            });
                            break;
                        }

                        // Wait until motion is done
                        bool isMotionDone = false;
                        while (!isMotionDone)
                        {
                            // Check for errors and motion status on the UI thread
                            await Dispatcher.InvokeAsync(() =>
                            {
                                motorController.CheckForErrors();
                                motorController.IsMotionDone(motorNumber, out isMotionDone);
                            });

                            await Task.Delay(50, motionCancellationTokenSource.Token);

                            // Handle pause
                            lock (pauseLock)
                            {
                                while (isPaused)
                                {
                                    Monitor.Wait(pauseLock);
                                }
                            }
                        }

                        // Wait for the specified time interval
                        await Task.Delay(timePerMoveMs, motionCancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Motion was canceled
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendMessage($"Error during motion: {ex.Message}"));
                }
            });
        }

        /// <summary>
        /// Stops the automatic continuous motion.
        /// </summary>
        private void StopContinuousMotion()
        {
            if (motionCancellationTokenSource != null)
            {
                motionCancellationTokenSource.Cancel();
                motionCancellationTokenSource = null;
            }
        }

        #endregion
    }
}
