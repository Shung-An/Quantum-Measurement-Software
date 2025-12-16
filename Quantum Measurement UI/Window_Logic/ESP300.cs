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
using System.Globalization;
using System.Windows.Input;

namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window
    {
        #region ESP300 Controller Delaye Stage

        private void ApplyMotionSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string axisPrefix = esp300Controller.Axis.ToString(); // Axis number (usually "1")

                if (double.TryParse(VAInput.Text, out double va))
                {
                    esp300Controller.SendCommand($"{axisPrefix}VA{va}");
                    AppendMessage($"Set VA (Velocity) = {va}");
                }

                if (double.TryParse(VUInput.Text, out double vu))
                {
                    esp300Controller.SendCommand($"{axisPrefix}VU{vu}");
                    AppendMessage($"Set VU (Velocity Limit) = {vu}");
                }

                if (double.TryParse(ACInput.Text, out double ac))
                {
                    esp300Controller.SendCommand($"{axisPrefix}AC{ac}");
                    AppendMessage($"Set AC (Acceleration) = {ac}");
                }

                if (double.TryParse(AUInput.Text, out double au))
                {
                    esp300Controller.SendCommand($"{axisPrefix}AU{au}");
                    AppendMessage($"Set AU (Max Acc/Dec) = {au}");
                }

                if (double.TryParse(AGInput.Text, out double ag))
                {
                    esp300Controller.SendCommand($"{axisPrefix}AG{ag}");
                    AppendMessage($"Set AG (Deceleration) = {ag}");
                }

                LogExperimentEvent("Motion settings updated successfully.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error applying motion settings: {ex.Message}");
                LogExperimentEvent($"Error applying motion settings: {ex.Message}");
            }
        }

        private void TimeZeroPositionInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                GoToTimeZero_Click(sender, new RoutedEventArgs());
        }

        private void GoToTimeZero_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TimeZeroPositionInput.Text.Trim(),
                                 NumberStyles.Float,
                                 CultureInfo.InvariantCulture,
                                 out var pos))
            {
                MessageBox.Show("Please enter a valid number for the Time 0 position.",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (esp300Controller is null)
                    throw new InvalidOperationException("ESP controller is not initialized.");

                string axisPrefix = esp300Controller.Axis.ToString();
                esp300Controller.SendCommand($"{axisPrefix}PA{pos.ToString(CultureInfo.InvariantCulture)}");

                AppendMessage($"Commanded ESP to move to Time 0 position: {pos:F3} mm.");
                LogExperimentEvent($"Commanded ESP to move to Time 0 position: {pos:F3} mm.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to move to Time 0 ({pos}): {ex.Message}",
                                "ESP Move Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void ESP_StopMotion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                esp300Controller.SendCommand("ST"); // Stop Motion
                AppendMessage("ESP motion stopped.");
                LogExperimentEvent("ESP motion stopped.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error stopping ESP: {ex.Message}");
            }
        }

        private void ESP_AbortProgram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                esp300Controller.AbortProgram(); // Abort program already implemented
                AppendMessage("ESP program aborted.");
                LogExperimentEvent("ESP program aborted.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error aborting ESP program: {ex.Message}");
            }
        }

        private async void ESP_ResetController_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendMessage("Resetting ESP controller...");
                LogExperimentEvent("Resetting ESP controller...");

                ResetDelayStageButton.IsEnabled = false;
                await Task.Run(() => esp300Controller.Reset());

                AppendMessage("ESP controller reset completed.");
                LogExperimentEvent("ESP controller reset completed.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error resetting ESP: {ex.Message}");
            }
            finally
            {
                ResetDelayStageButton.IsEnabled = true;
            }
        }


        private void ReadMotionSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string axisPrefix = esp300Controller.Axis.ToString();

                esp300Controller.SendCommand($"{axisPrefix}VA?");
                VAInput.Text = esp300Controller.ReadResponse().Trim();

                esp300Controller.SendCommand($"{axisPrefix}VU?");
                VUInput.Text = esp300Controller.ReadResponse().Trim();

                esp300Controller.SendCommand($"{axisPrefix}AC?");
                ACInput.Text = esp300Controller.ReadResponse().Trim();

                esp300Controller.SendCommand($"{axisPrefix}AU?");
                AUInput.Text = esp300Controller.ReadResponse().Trim();

                esp300Controller.SendCommand($"{axisPrefix}AG?");
                AGInput.Text = esp300Controller.ReadResponse().Trim();

                AppendMessage("Read current motion settings successfully.");
                LogExperimentEvent("Read current motion settings successfully.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error reading motion settings: {ex.Message}");
            }
        }
        private void ESP_MoveToPosition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string axisPrefix = esp300Controller.Axis.ToString();

                if (double.TryParse(PAInput.Text, out double targetPosition))
                {
                    esp300Controller.SendCommand($"{axisPrefix}PA{targetPosition}");
                    AppendMessage($"Commanded ESP to move to absolute position {targetPosition:F3} mm.");
                    LogExperimentEvent($"Commanded ESP to move to absolute position {targetPosition:F3} mm.");
                }
                else
                {
                    AppendMessage("Invalid position entered. Please enter a numeric value.");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error commanding ESP move: {ex.Message}");
            }
        }



        private void startDelayStageProgram()
        {
            try
            {
                // --- 1) Read program name from UI ---
                string programName = "Motion";
                Dispatcher.Invoke(() =>
                {
                    var name = DelayStageProgram.Text?.Trim();
                    if (!string.IsNullOrEmpty(name)) programName = name;
                    else
                    {
                        AppendMessage("Delay stage program name is empty.");
                        LogExperimentEvent("Delay stage program name is empty.");
                    }
                });

                // --- 2) Prepare controller / info ---
                esp300Controller.setPositionDisplayResolution(5);
                string stageInfo = esp300Controller.GetDelayStageInfo();
                AppendMessage($"Delay Stage Info: {stageInfo}");
                LogExperimentEvent($"Delay Stage Info: {stageInfo}");

                // --- 3) Parse time-zero and move there (no UI thread IO) ---
                if (!double.TryParse(TimeZeroPositionInput.Text?.Trim(),
                                     NumberStyles.Float,
                                     CultureInfo.InvariantCulture,
                                     out var timeZeroPosition))
                {
                    AppendMessage("Invalid Time 0 position input.");
                    LogExperimentEvent("Invalid Time 0 position input.");
                    return;
                }
                // Allow brief settle
                Thread.Sleep(100);

                string axisPrefix = esp300Controller.Axis.ToString(CultureInfo.InvariantCulture);
                esp300Controller.SendCommand($"{axisPrefix}PA{timeZeroPosition.ToString("G17", CultureInfo.InvariantCulture)}");
                AppendMessage($"Commanded ESP to move to Time 0 position: {timeZeroPosition:F3} mm.");
                LogExperimentEvent($"Commanded ESP to move to Time 0 position: {timeZeroPosition:F3} mm.");
                // Allow brief settle
                Thread.Sleep(100);
                string cleared = esp300Controller.ClearAllErrors();
                AppendMessage($"Cleared ESP300 errors:\n{cleared}");
                LogExperimentEvent($"Cleared ESP300 errors:\n{cleared}");


                // Allow brief settle
                Thread.Sleep(300);

                // --- 4) Execute program ---
                esp300Controller.ExecuteProgram(programName);
                AppendMessage($"Started delay stage program: {programName}");
                LogExperimentEvent($"Started delay stage program: {programName}");

                // Allow brief settle
                Thread.Sleep(500);

                // TB?/ER? integrated checker you added earlier
                string controllerError = esp300Controller.CheckForErrors();
                if (controllerError.Contains("Timeout"))
                {
                    AppendMessage("Delay stage busy at startup, skipping initial error check.");
                    LogExperimentEvent("Delay stage busy at startup, skipping initial error check.");
                }
                else if (controllerError != "No delay stage errors detected")
                {
                    AppendMessage($"Error in delay stage: {controllerError}");
                    LogExperimentEvent($"Error in delay stage: {controllerError}");
                }

                // --- 5) Initial motion status -> UI ---
                int motorStatus = esp300Controller.getMotionStatus();
                if (motorStatus == 1)
                {
                    AppendMessage("Delay stage is not moving.");
                    LogExperimentEvent("Delay stage is not moving.");
                    Dispatcher.Invoke(() =>
                    {
                        DelayStageStatusText.Text = "Not Moving";
                        DelayStageStatusIndicator.Fill = Brushes.Yellow;
                    });
                }
                else
                {
                    AppendMessage("Delay stage is moving.");
                    LogExperimentEvent("Delay stage is moving.");
                    Dispatcher.Invoke(() =>
                    {
                        DelayStageStatusText.Text = "Moving";
                        DelayStageStatusIndicator.Fill = Brushes.Green;
                    });
                }

                // --- 6) Open log file (append) ---
                string delayStageLogPath = Path.Combine(
                    resultsBaseDirectory,
                    experimentLogDirectory,
                    "delay_stage_positions.log");
                delayStageLogWriter = new StreamWriter(delayStageLogPath, append: true);
                delayStageLogWriter.WriteLine("Timestamp,Position");

                // --- 7) Start lightweight position sampler/monitor ---
                delayStagePositionCancellationTokenSource?.Cancel();
                delayStagePositionCancellationTokenSource = new CancellationTokenSource();
                var token = delayStagePositionCancellationTokenSource.Token;

                const int pollMs = 150;                   // device poll cadence
                const int uiErrRateLimitSec = 10;         // UI spam limiter
                const int recoverAfterErrors = 20;        // ~3s of failures at 150ms cadence




                Task.Run(async () =>
                {
                    int consecutiveErrors = 0;
                    DateTime lastUiError = DateTime.MinValue;
                    try
                    {
                        while (!token.IsCancellationRequested && isExperimentRunning)
                        {
                            string ts = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                            bool ok = false;
                            string errMsg = null;


                            double pos = double.NaN;

                            try
                            {
                                // Read once per loop; cache for everyone else
                                pos = esp300Controller.GetCurrentPosition();
                                System.Threading.Volatile.Write(ref currentESPPosition, pos);
                                delayStageCurrentPosition = pos;
                                currentESPPosition = pos; // shared cached position
                                ok = true;
                                consecutiveErrors = 0;

                                // UI update (position & status)
                                Dispatcher.Invoke(() =>
                                {
                                    DelayStagePositionText.Text = pos.ToString("F5", CultureInfo.InvariantCulture);
                                    if (DelayStageStatusText.Text != "Moving" && DelayStageStatusText.Text != "Running")
                                    {
                                        DelayStageStatusText.Text = "Running";
                                        DelayStageStatusIndicator.Fill = Brushes.Green;
                                    }
                                });
                            }
                            catch (TimeoutException tex)
                            {
                                errMsg = $"Timeout: {tex.Message}";
                                consecutiveErrors++;
                            }
                            catch (IOException ioex)
                            {
                                errMsg = $"IO error: {ioex.Message}";
                                consecutiveErrors++;
                            }
                            catch (Exception ex)
                            {
                                errMsg = $"Error: {ex.Message}";
                                consecutiveErrors++;
                            }

                            // Log every tick, even on failure
                            if (delayStageLogWriter != null)
                            {
                                if (ok)
                                    delayStageLogWriter.WriteLine($"{ts},{pos.ToString("G17", CultureInfo.InvariantCulture)}");
                                else
                                    delayStageLogWriter.WriteLine($"{ts},NaN   # {errMsg}");
                                delayStageLogWriter.Flush();
                            }

                            // Rate‑limit UI error messages
                            if (!ok)
                            {
                                if ((DateTime.Now - lastUiError).TotalSeconds >= uiErrRateLimitSec)
                                {
                                    lastUiError = DateTime.Now;
                                    Dispatcher.Invoke(() =>
                                    {
                                        AppendMessage($"Delay stage read issue (x{consecutiveErrors}): {errMsg}");
                                        LogExperimentEvent($"Delay stage read issue: {errMsg}");
                                        DelayStageStatusText.Text = "Degraded (reading...)";
                                        DelayStageStatusIndicator.Fill = Brushes.Yellow;
                                    });
                                }

                                // Soft recovery after many consecutive failures
                                if (consecutiveErrors >= recoverAfterErrors)
                                {
                                    try
                                    {
                                        var ce = esp300Controller.CheckForErrors();
                                        if (ce != "No delay stage errors detected")
                                        {
                                            Dispatcher.Invoke(() =>
                                            {
                                                AppendMessage($"Delay stage reports error: {ce}");
                                                LogExperimentEvent($"Delay stage reports error: {ce}");
                                            });
                                        }

                                        try { esp300Controller.Disconnect(); } catch { }
                                        try { esp300Controller.Connect(); } catch { }
                                        await Task.Delay(300, token);
                                    }
                                    catch { /* swallow */ }
                                    finally
                                    {
                                        consecutiveErrors = 0;
                                    }
                                }
                            }

                            await Task.Delay(pollMs, token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AppendMessage("Delay stage position monitoring stopped.");
                            LogExperimentEvent("Delay stage position monitoring stopped.");
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AppendMessage($"Delay stage monitoring fatal error: {ex.Message}");
                            LogExperimentEvent($"Delay stage monitoring fatal error: {ex.Message}");
                            DelayStageStatusText.Text = "Error";
                            DelayStageStatusIndicator.Fill = Brushes.Red;
                        });
                    }
                    finally
                    {
                        // Do NOT close writer here; stopDelayStageProgram() owns lifecycle
                        Dispatcher.Invoke(() =>
                        {
                            AppendMessage("Delay stage position monitoring task exited.");
                            LogExperimentEvent("Delay stage position monitoring task exited.");
                        });
                    }
                }, token);

                // --- 8) Final UI state ---
                Dispatcher.Invoke(() =>
                {
                    DelayStageStatusText.Text = "Running";
                    DelayStageStatusIndicator.Fill = Brushes.Green;
                });

                AppendMessage("Delay stage position monitoring started.");
                LogExperimentEvent("Delay stage position monitoring started.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error initializing delay stage: {ex.Message}");
                LogExperimentEvent($"Error initializing delay stage: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    DelayStageStatusText.Text = "Error";
                    DelayStageStatusIndicator.Fill = Brushes.Red;
                });
            }
        }



        private void stopDelayStageProgram()
        {
            // Stop the delay stage program
            esp300Controller?.AbortProgram();

     
            // Stop the delay stage position monitoring task
            if (delayStagePositionCancellationTokenSource != null)
            {
                delayStagePositionCancellationTokenSource.Cancel();
                Task.Delay(100); // Give it time to stop gracefully
                delayStagePositionCancellationTokenSource = null;
            }

            // Close the delay stage log file
            if (delayStageLogWriter != null)
            {
                try
                {
                    delayStageLogWriter.WriteLine("\n--- Delay Stage Logging End ---");
                    delayStageLogWriter.Flush();
                    delayStageLogWriter.Close();
                    delayStageLogWriter = null;
                }
                catch (Exception ex)
                {
                    AppendMessage($"Error closing delay stage log: {ex.Message}");
                    LogExperimentEvent($"Error closing delay stage log: {ex.Message}");
                }
            }
        }


        #endregion
    }
}
