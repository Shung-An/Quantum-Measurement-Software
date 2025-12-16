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

                // Initialize the experiment log
                await AsyncInitializeExperimentLog();



                // Start the delay stage program

                startDelayStageProgram();
                await Task.Delay(500); // Wait for 0.5 seconds to ensure the delay stage program is started
                await signal;
                window = new Mov_Avg(20);

                // Start the ESP position update task
                espPositionCancellationTokenSource = new CancellationTokenSource();
                _ = Task.Run(() => UpdateESPPosition(espPositionCancellationTokenSource.Token));
               
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

                await Task.Delay(1000);

              
                StartAutobalanceButton_Click(null, null); // Start the autobalancer
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
                espPositionCancellationTokenSource?.Cancel();    // Stop ESP position updates
                autoReadCts?.Cancel();

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
                autoReadCts.Dispose();                          // Stop auto read
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

                // === Run FFT after acquisition (only if enabled) ===
                // Use YOUR actual path; defaults to disabled unless checkbox is on.
                string fftExePath = @"C:\Quantum Squeezing\Andy test\GageStreamThruGPU-FFT\x64\Debug\GageStreamThruGPU-FFT.exe";

                if (EnableFFT) // <- checkbox gate
                {
                    bool ok = await RunFFTAndWaitAsync(fftExePath);
                    if (ok)
                    {
                        AppendMessage("✅ FFT completed after acquisition.");
                        LogExperimentEvent("FFT completed after acquisition.");
                        await Dispatcher.InvokeAsync(PlotSavedFFTResults);
                    }
                    else
                    {
                        AppendMessage("⚠️ FFT failed after acquisition.");
                        LogExperimentEvent("FFT failed after acquisition.");
                    }
                }

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
                /*RenameExperimentFolder();*/





                // Clear all charts/data on UI thread (null-safe)
                await Dispatcher.InvokeAsync(() =>
                {
                    ChannelAValues?.Clear();
                    ChannelBValues?.Clear();

                    heatValues?.Clear();

                    PixelValues?.Clear();
                    PixelCumulativeSum = 0;
                    PixelCount = 0;

                    autobalancer?.MotorPositionValues1?.Clear();
                    autobalancer?.MotorPositionValues2?.Clear();
                    autobalancer?.MetricValuesA?.Clear();
                    autobalancer?.MetricValuesB?.Clear();


                    SelectedPixelValue.Text = "0.00";
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

        private void SaveExperimentMetadata(string folderPath, string elapsedTime)
        {

            // Get lists from UI
            var samples = GetSelectedSamples();
            var tags = GetSelectedTags();

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
                                      

                // Machine Configuration (snapshot of current state)
                Configuration = new
                {
                    EnableFFT = this.EnableFFT,
                    ExternalClock = ExtClkStatusText.Text,
                    Motor1Position = CalibrationMotor1Pos.Text, // Assuming you have this
                    Motor2Position = CalibrationMotor2Pos.Text,
                    ExternalClockStatus = ExtClkStatusText.Text
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
