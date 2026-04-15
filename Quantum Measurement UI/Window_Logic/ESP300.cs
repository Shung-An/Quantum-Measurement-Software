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

        private async Task SendESPCommandAsync(string command)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(command))
                    return;

                esp300Controller.SendCommand(command.Trim());
                await Task.Delay(100); // Small delay between commands for ESP300 to catch up
            }
            catch (Exception ex)
            {
                AppendMessage($"Error sending ESP command '{command}': {ex.Message}");
            }
        }

        // --- Helper for Thread-Safe Plot Updates ---
        private void UpdateDelayStagePlot(double position)
        {
            System.Threading.Volatile.Write(ref currentESPPosition, position);
            delayStageCurrentPosition = position;

            // Updates the chart on the UI thread without blocking the motor logic
            Dispatcher.InvokeAsync(() =>
            {
                ESPPositionValues.Add(position);

                // Keep chart light (using your defined capacity)
                if (ESPPositionValues.Count > EspChartCapacity)
                {
                    ESPPositionValues.RemoveAt(0);
                }
            });
        }

        // --- Main Function ---
        private async Task startDelayStageProgram()
        {
            if (esp300Controller == null || !esp300Controller.IsConnected)
            {
                AppendMessage("ESP300 controller not connected.");
                return;
            }

            try
            {
                // 1. Validate Inputs
                string programName = "";
                string timeZeroInput = "";

                // Read UI elements on the UI thread
                Dispatcher.Invoke(() => {
                    programName = DelayStageProgram.Text?.Trim();
                    timeZeroInput = TimeZeroPositionInput.Text?.Trim();
                });

                if (string.IsNullOrEmpty(programName))
                {
                    AppendMessage("Please enter a valid program name.");
                    return;
                }

                if (!double.TryParse(timeZeroInput, out var timeZeroPosition))
                {
                    AppendMessage("Invalid Time 0 position input.");
                    return;
                }

                // 2. Move to Time Zero (Safe Move with Plotting)
                // ---------------------------------------------------------
                string axisPrefix = esp300Controller.Axis.ToString(CultureInfo.InvariantCulture);

                // A) Send the move command
                esp300Controller.SendCommand($"{axisPrefix}PA{timeZeroPosition.ToString("G17", CultureInfo.InvariantCulture)}");
                AppendMessage($"Moving to Time 0: {timeZeroPosition:F3} mm...");

                // B) ACTIVE WAIT LOOP: Wait for motor to stop while updating plot
                bool isMoving = true;
                int timeoutCounter = 0;

                // Wait 200ms for the controller to register the "Busy" state
                await Task.Delay(200);

                while (isMoving && timeoutCounter < 100) // 10 second timeout
                {
                    // Read hardware
                    double currentPos = esp300Controller.GetCurrentPosition();
                    int status = esp300Controller.getMotionStatus(); // 1 usually means "Stopped"

                    // Update Plot & UI
                    UpdateDelayStagePlot(currentPos);
                    Dispatcher.Invoke(() => DelayStagePositionText.Text = currentPos.ToString("F4"));

                    // Check if settled
                    if (status == 1)
                    {
                        isMoving = false;
                    }
                    else
                    {
                        await Task.Delay(100); // 10 Hz refresh rate
                        timeoutCounter++;
                    }
                }

                if (isMoving)
                {
                    AppendMessage("Error: Timed out waiting for Time 0 move. Program aborted.");
                    return;
                }

                AppendMessage("Time 0 reached. Starting stored program...");
                // ---------------------------------------------------------

                // 3. Start the Stored Program
                esp300Controller.ClearAllErrors();
                esp300Controller.ExecuteProgram(programName);
                AppendMessage($"Started delay stage program: {programName}");

                // 4. Setup Logging
                string logFileName = $"delay_stage_positions.log";
                string logPath = Path.Combine(resultsBaseDirectory, experimentLogDirectory, logFileName);

                try
                {
                    delayStageLogWriter = new StreamWriter(logPath, append: true);
                    await delayStageLogWriter.WriteLineAsync("Timestamp,Position");
                }
                catch (Exception ex)
                {
                    AppendMessage($"Error creating log file: {ex.Message}");
                }

                // 5. Start Continuous Monitoring (Plot + Log)
                delayStagePositionCancellationTokenSource = new CancellationTokenSource();
                var token = delayStagePositionCancellationTokenSource.Token;

                // Run this in the background so it doesn't block the UI
                _ = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // A. Read Position
                            double position = esp300Controller.GetCurrentPosition();

                            // B. Update Plot (Using the same helper)
                            UpdateDelayStagePlot(position);

                            // C. Update Text UI
                            Dispatcher.Invoke(() =>
                            {
                                DelayStagePositionText.Text = position.ToString("F4");
                                DelayStageStatusText.Text = "Running";
                                DelayStageStatusIndicator.Fill = Brushes.Green;
                            });

                            // D. Log to file
                            if (delayStageLogWriter != null)
                            {
                                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                                await delayStageLogWriter.WriteLineAsync($"{timestamp},{position}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Polling error: {ex.Message}");
                        }

                        // Throttle the loop (e.g. 250ms)
                        try { await Task.Delay(250, token); }
                        catch (TaskCanceledException) { break; }
                    }
                }, token);
            }
            catch (Exception ex)
            {
                AppendMessage($"Error starting delay stage program: {ex.Message}");
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
