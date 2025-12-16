using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Controls;
using QuantumSqueezingUI;
using Windows.ApplicationModel.Activation;

namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window // Current file has logic for NiDaq and ESP Processes
    { 
        #region Configuration and Process Management Functions 

        /// <summary>
        /// Starts the GageStreamThruGPU process.
        /// </summary>
        public void StartGageStreamProcess()
        {
            try
            {
                
                gageStreamProcess = System.Diagnostics.Process.Start(exePath);
                AppendMessage("GageStreamThruGPU.exe started.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Failed to start GageStreamThruGPU.exe: {ex.Message}");
            }
        }



        /// <summary>
        /// Initializes the experiment log file and copies the streaming configuration from StreamThruGPU.ini.
        /// </summary>
        private async Task AsyncInitializeExperimentLog()
        {
            // 1. Start with the standard timestamp
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // 4. Create the directory with this descriptive name
            string resultDirectory = Path.Combine(resultsBaseDirectory, timestamp);
            Directory.CreateDirectory(resultDirectory);

            // Update the global variable so other functions know where to save
            experimentLogDirectory = timestamp;

            // --- (The rest of your existing logic stays the same) ---
            experimentLogFilePath = Path.Combine(resultDirectory, "exp.log");
            motorMetricLogFilePath = Path.Combine(resultDirectory, "motor_metric.log");
            sensitivityLogFilePath = Path.Combine(resultDirectory, "sensitivity.log");
            droppedWindowLogFilePath = Path.Combine(resultDirectory, "dropped_window.log");

            experimentLogWriter = new StreamWriter(experimentLogFilePath) { AutoFlush = true };
            motorMetricLogWriter = new StreamWriter(motorMetricLogFilePath) { AutoFlush = true };
            sensitivityLogWriter = new StreamWriter(sensitivityLogFilePath) { AutoFlush = true };
            droppedWindowLogWriter = new StreamWriter(droppedWindowLogFilePath) { AutoFlush = true };

            experimentLogWriter.WriteLine($"Experiment Log: {experimentLogFilePath}\n");

        }



        /// <summary>
        /// Renames the experiment folder based on UI Metadata.
        /// </summary>
        private void RenameExperimentFolder()
        {
            try
            {
                // 1. Get the current (old) folder path
                string oldFolderPath = Path.Combine(resultsBaseDirectory, experimentLogDirectory);
                if (!Directory.Exists(oldFolderPath)) return;

                // 2. Prepare the String Parts

                // --- Samples ---
                var samples = GetSelectedSamples();
                // Safety check: ensure list isn't empty, default to "Unknown"
                string sampleStr = samples.Count > 0 ? samples[0] : "Unknown";
                if (samples.Count > 1) sampleStr += "_mix";

                // --- Tags ---
                var tags = GetSelectedTags();
                string tagStr = tags.Count > 0 ? tags[0] : "";
                if (tags.Count > 1) tagStr += "_etc";

                // 3. Sanitize inputs (Prevent illegal characters like / \ : *)
                string cleanSample = SanitizePath(sampleStr);
                string cleanTag = SanitizePath(tagStr);

                // Check if we actually have anything to append. 
                // If both are empty/default, we might not want to rename (optional logic).
                if (string.IsNullOrWhiteSpace(cleanSample) && string.IsNullOrWhiteSpace(cleanTag)) return;

                // 4. Create New Name: Timestamp_Sample_Tag
                // Get the timestamp from the existing folder name
                string timestamp = new DirectoryInfo(oldFolderPath).Name;

                // Construct the new name using the CLEAN variables
                string newFolderName = $"{timestamp}_{cleanSample}_{cleanTag}";

                // Remove trailing underscore if tag was empty (e.g., "Time_Sample_")
                newFolderName = newFolderName.Trim('_');

                string newFolderPath = Path.Combine(resultsBaseDirectory, newFolderName);

                // 5. Rename (Move)
                Directory.Move(oldFolderPath, newFolderPath);

                // 6. Update the global variable so future logs go to the right place
                experimentLogDirectory = newFolderName;
                AppendMessage($"Folder renamed to: {newFolderName}");
            }
            catch (Exception ex)
            {
                AppendMessage($"Note: Could not rename folder (files might be open): {ex.Message}");
            }
        }


        // Helper to clean bad characters
        private string SanitizePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                // FIX: Add .ToString() so both arguments are strings
                name = name.Replace(c.ToString(), "");
            }

            return name.Replace(" ", "");
        }


        private void StartESPUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (espPositionCancellationTokenSource == null || espPositionCancellationTokenSource.IsCancellationRequested)
                {
                    espPositionCancellationTokenSource = new CancellationTokenSource();
                    Task.Run(() => UpdateESPPosition(espPositionCancellationTokenSource.Token));
                    AppendMessage("Started updating ESP position.");
                    LogExperimentEvent("Started updating ESP position.");
                }
                else
                {
                    AppendMessage("ESP position update is already running.");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error starting ESP update: {ex.Message}");
            }
        }

        private void StopESPUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (espPositionCancellationTokenSource != null)
                {
                    espPositionCancellationTokenSource.Cancel();
                    AppendMessage("Stopped updating ESP position.");
                    LogExperimentEvent("Stopped updating ESP position.");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error stopping ESP update: {ex.Message}");
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


        private CancellationTokenSource scanCts;

        private async void RunAutoCycle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string axisPrefix = esp300Controller.Axis.ToString();

                // Inputs
                double center = double.Parse(CenterPointInput.Text);
                double span = double.Parse(SpanInput.Text);
                double stepSize = double.Parse(StepSizeInput.Text);
                int dwellSec = int.Parse(DwellTimeInput.Text);
                int loopCount = int.Parse(LoopCountInput.Text);

                int steps = (int)Math.Round(span / stepSize);
                double start = center - span / 2.0;

                // Total steps includes forward + backward in each loop
                int totalSteps = Math.Max(0, steps * 2 * loopCount);

                scanCts = new CancellationTokenSource();
                var token = scanCts.Token;

                AppendMessage($"Starting step scan: center={center:F3}, span={span:F3}, step={stepSize:F3}, dwell={dwellSec}s, loops={loopCount}");
                LogExperimentEvent("Step scan started.");

                // Start time counter
                StartAutoScanTimer(totalSteps, dwellSec);

                // Move to start point once
                await SendESPCommandAsync($"{axisPrefix}PA{start:F3}");
                await Task.Delay(2000, token);  // settle

                // Loops
                for (int loop = 1; loop <= loopCount; loop++)
                {
                    // Forward sweep
                    for (int i = 0; i < steps; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        await SendESPCommandAsync($"{axisPrefix}PR{stepSize:F3}");
                        LogExperimentEvent($"Loop {loop} forward step {i + 1}/{steps}, move {stepSize:F3} mm");

                        await Task.Delay(TimeSpan.FromSeconds(dwellSec), token);

                        // one step completed
                        autoScanCompletedSteps++;
                    }

                    // Backward sweep
                    for (int i = 0; i < steps; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        await SendESPCommandAsync($"{axisPrefix}PR{-stepSize:F3}");
                        LogExperimentEvent($"Loop {loop} backward step {i + 1}/{steps}, move {-stepSize:F3} mm");

                        await Task.Delay(TimeSpan.FromSeconds(dwellSec), token);

                        // one step completed
                        autoScanCompletedSteps++;
                    }
                }

                // Return to center
                await SendESPCommandAsync($"{axisPrefix}PA{center:F3}");
                AppendMessage("Scan finished and returned to center.");
                LogExperimentEvent("Scan finished.");
            }
            catch (TaskCanceledException)
            {
                AppendMessage("Step scan terminated by user.");
                LogExperimentEvent("Step scan terminated.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error during step scan: {ex.Message}");
            }
            finally
            {
                StopAutoScanTimer();
            }
        }
        private void StartAutoScanTimer(int totalSteps, int dwellSec)
        {
            autoScanTotalSteps = Math.Max(0, totalSteps);
            autoScanCompletedSteps = 0;
            autoScanStepDwellSec = Math.Max(0, dwellSec);

            autoScanStopwatch = System.Diagnostics.Stopwatch.StartNew();

            if (autoScanUiTimer == null)
            {
                autoScanUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                autoScanUiTimer.Tick += AutoScanUiTimer_Tick;
            }
            autoScanUiTimer.Start();
        }

        private void StopAutoScanTimer()
        {
            try { autoScanUiTimer?.Stop(); } catch { /* ignore */ }
            try { autoScanStopwatch?.Stop(); } catch { /* ignore */ }

            // final UI refresh
            AutoScanUiTimer_Tick(null, EventArgs.Empty);
        }

        private void AutoScanUiTimer_Tick(object sender, EventArgs e)
        {
            var elapsed = autoScanStopwatch?.Elapsed ?? TimeSpan.Zero;

            // ETA = remainingSteps * dwellSec (simple, robust; ignores small move overhead)
            var remainingSteps = Math.Max(0, autoScanTotalSteps - autoScanCompletedSteps);
            var remaining = TimeSpan.FromSeconds(remainingSteps * autoScanStepDwellSec);

            // Update UI if those elements exist
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (AutoScanElapsedText != null)
                        AutoScanElapsedText.Text = elapsed.ToString(@"hh\:mm\:ss");

                    if (AutoScanRemainingText != null)
                        AutoScanRemainingText.Text = remaining.ToString(@"hh\:mm\:ss");
                });
            }
            catch { /* ignore cross-thread races during shutdown */ }
        }


        private void StopAutoCycle_Click(object sender, RoutedEventArgs e)
        {
            scanCts?.Cancel();
        }


        private async void EnableMotorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string axisPrefix = esp300Controller.Axis.ToString();

                await SendESPCommandAsync($"{axisPrefix}MO"); // 🔥 Turn Motor ON

                AppendMessage("Motor enabled (Motor ON).");
                LogExperimentEvent("Motor enabled (Motor ON).");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error enabling motor: {ex.Message}");
                LogExperimentEvent($"Error enabling motor: {ex.Message}");
            }
        }


        private async Task UpdateESPPosition(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Read the last sampled position; never talk to hardware here
                    double pos = System.Threading.Volatile.Read(ref currentESPPosition);

                    if (!double.IsNaN(pos))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ESPPositionValues.Add(pos);

                            // keep chart light
                            if (ESPPositionValues.Count > EspChartCapacity)
                                ESPPositionValues.RemoveAt(0);
                        });
                    }

                    // UI refresh cadence (no need to be faster than ~200–300 ms)
                    await Task.Delay(250, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                // normal
            }
            catch (Exception ex)
            {
                AppendMessage($"Exception in UpdateESPPosition: {ex.Message}");
            }
        }



        private CancellationTokenSource espPositionCancellationTokenSource;

        /// <summary>
        /// Updates a specific key in a specific section of the .ini file.
        /// </summary>
        private void UpdateIniFile(string section, string key, string value)
        {
            if (!File.Exists(IniFilePath))
            {
                AppendMessage("Configuration file not found.");
                return;
            }

            // Read all lines from the ini file
            var lines = File.ReadAllLines(IniFilePath);
            bool sectionFound = false;
            bool keyUpdated = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // Check if this line is the section we're looking for
                if (line.Equals($"[{section}]"))
                {
                    sectionFound = true;
                }
                // If we're in the correct section, look for the key
                else if (sectionFound && line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                {
                    // Update the key with the new value
                    lines[i] = $"{key}={value}";
                    keyUpdated = true;
                    break;
                }
                // If we encounter another section header, stop searching for the key
                else if (sectionFound && line.StartsWith("["))
                {
                    break;
                }
            }

            // If the section or key was not found, append it
            if (!sectionFound)
            {
                AppendMessage($"Section [{section}] not found, adding it.");
                using (StreamWriter writer = new StreamWriter(IniFilePath, true))
                {
                    writer.WriteLine($"\n[{section}]");
                    writer.WriteLine($"{key}={value}");
                }
            }
            else if (!keyUpdated)
            {
                AppendMessage($"Key {key} not found in section [{section}], adding it.");
                using (StreamWriter writer = new StreamWriter(IniFilePath, true))
                {
                    writer.WriteLine($"{key}={value}");
                }
            }
            else
            {
                // Write the updated lines back to the file
                File.WriteAllLines(IniFilePath, lines);
            }
        }

        private async void ConnectDAQButton_Click(object sender, RoutedEventArgs e)
        {
            bool condition = await Connection();
            if (condition) AppendMessage("Connected to QuantumDAQService!");
        }

        private async Task<bool> Connection()
        {
            try
            {
                EnsureDAQServiceRunning();

                if (daqPipe == null || !daqPipe.IsConnected) // Create a new Pipe Client if one is not already running
                {
                    daqPipe = new PipeClient();
                    await daqPipe.ConnectAsync();

                    // Start all the channels
                    string response = await daqPipe.SendCommandAsync($"StartAI ai0,ai1,ai2,ai3,ai4,ai5");
                    AppendMessage("Connected to QuantumDAQService!\n" + response);
                    return true;
                }
                else
                {
                    AppendMessage("DAQ already connected. Skipping re-connection.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendMessage("Failed to connect to QuantumDAQService: " + ex.Message);
                return false;
            }
        }

        private void EnsureDAQServiceRunning()
        {
            var processes = Process.GetProcessesByName("QuantumDAQService");
            if (processes.Length > 0)
                return; // Already running

            // Use absolute path
            string exePath = @"C:\Quantum Squeezing\Quantum-Measurement-Software\QuantumDAQService\bin\Debug\QuantumDAQService.exe";

            if (!File.Exists(exePath))
            {
                AppendMessage("QuantumDAQService.exe not found at expected location!\n" + exePath);
                return;
            }


            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            daqServiceProcess = Process.Start(psi);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            try
            {
                // If we started the DAQ service, close it
                if (daqServiceProcess != null && !daqServiceProcess.HasExited)
                {
                    daqServiceProcess.Kill();
                    daqServiceProcess.WaitForExit();
                    daqServiceProcess?.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppendMessage("Failed to close QuantumDAQService: " + ex.Message);
            }
        }

        private void StartMotorVsAI5Update()
        {
            motorVsAI5Cts = new CancellationTokenSource();
            var token = motorVsAI5Cts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateMotorVsAI5(); // Update motor curve every 100 ms
                        });
                        await Task.Delay(100, token); // 100ms = 10Hz
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppendMessage($"Error in MotorVsAI5 update loop: {ex.Message}");
                    }
                }
            });
        }

        
        private DateTime lastAiUpdateTime = DateTime.Now;
        private bool paused = false;
        private double timeElapsed = 0; // time elapsed measured in milliseconds
        private bool first = true; // used to determine the first point in the list
        private int WaitTicks = 0;

        /// <summary>
        /// Analyze the Daq Buffer, and records the voltage across each of the 6 channels into their 
        /// respective lists and onto the moniter
        /// </summary>
        private void UpdateAIMonitor()
        {
            //Temporary variables to split the data into the channles
            int samplesPerChannel = daqBuffer.Length / 6;
            double[,] buffers = new double[6, samplesPerChannel];

            int channel = 0;
            // Step 1: Accumulate samples into temporary buffers
            for (int i = 0; i < daqBuffer.Length; i++)
            {
                double value = daqBuffer[i]; // ai5 = channel 5
                buffers[channel, i / 6] = value;
                channel++;

                if (channel > 5) channel = 0;
            }

            // Step 2: Check if 100ms has passed
            double timeDelta = (DateTime.Now - lastAiUpdateTime).TotalMilliseconds;
            if (timeDelta >= 100 && WaitTicks <= 0)
            {
                if (!first) // do not record the time of the first data point, will be affected by delay of the system
                {
                    timeElapsed += timeDelta;
                }
                else
                {
                    first = false;
                }

                aiTimeTracker.Add(timeElapsed);

                for (int i = 0; i < 6; i++) // Loop through each buffer channel
                {
                    int localChannel = i;

                    double mean = 0; // Find the mean of each buffer
                    for (int j = 0; j < samplesPerChannel; j++)
                    {
                        mean += buffers[localChannel, j];
                    }
                    mean /= samplesPerChannel;

                    if (i == 0 && window.Dropped(mean)) // if the mean of channel 0 is below that 
                    {
                        WaitTicks = 80;
                        AppendMessage("NIDAQ Stream Paused");
                        return;
                    }
                    else if (i == 0)
                    {
                        window.Push(mean);
                    }

                    aiWindowData[localChannel].Add(mean);
                    UpdateAITimeSeriesChart(localChannel);

                    // Keep buffer only 1000 points (about 100 seconds history)
                    if (aiWindowData[localChannel].Count > 300)
                        aiWindowData[localChannel].RemoveAt(0);
                }

                lastAiUpdateTime = DateTime.Now;
            }
            else if (timeDelta >= 100) WaitTicks--;
        }

        private void ReleaseDAQPipeButton_Click(object sender, RoutedEventArgs e) // Wrapper Method to interact with the button
        {
            if(Release()) AppendMessage("DAQ Pipe released successfully.");
        }

        private bool Release()
        {
            try
            {
                if (daqPipe != null)
                {
                    daqPipe.Dispose();
                    daqPipe = null;
                    AppendMessage("DAQ Pipe released successfully.");

                    // If we started the DAQ service, close it
                    if (daqServiceProcess != null && !daqServiceProcess.HasExited)
                    {
                        daqServiceProcess.Kill();
                        daqServiceProcess.WaitForExit();
                        daqServiceProcess?.Dispose();
                    }
                    return true;
                }
                else
                {
                    AppendMessage("DAQ Pipe already null.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error releasing DAQ Pipe: {ex.Message}");
                MessageBox.Show($"Error releasing DAQ Pipe: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Displays data from the selected channel on to the respective Line Series in the chart
        /// </summary>
        /// <param name="channel"></param>
        private void UpdateAITimeSeriesChart(int channel) 
        {
            // Call one of the Series Values to be edited via the line variable
            ChartValues<ObservablePoint>? line = channel switch 
            {
                0 => AI0TimeSeriesValues,
                1 => AI1TimeSeriesValues,
                2 => AI2TimeSeriesValues,
                3 => AI3TimeSeriesValues,
                4 => AI4TimeSeriesValues,
                5 => AI5TimeSeriesValues,
                _ => null
            };

            if (line == null) return;

            if (line?.Count > 40) line.RemoveAt(0); // Beyond 60 points, program starts running really slowly trying to render everything

            double dt = 0.001; // Convert each point to seconds

            
            line?.Add(new ObservablePoint( // Add an observable point for the last updated value in the Window Data
                timeElapsed * dt,
                aiWindowData[channel][^1]));          
        }


        private void ResetHistogramAI5_Click(object sender, RoutedEventArgs e)
        {
            ai5CumulativeData.Clear();
            ai5CurrentWindowData.Clear();
            AI5HistogramValues.Clear();
            timeElapsed = 0;
        }
        private void ToggleMotorVsAI5AutoLog_Click(object sender, RoutedEventArgs e)
        {
            if (!isMotorVsAi5AutoLogging)
            {
                StartMotorVsAI5AutoLog();
            }
            else
            {
                StopMotorVsAI5AutoLog();
            }
        }

        private void StartMotorVsAI5AutoLog()
        {
            try
            {
                if (!Directory.Exists(motorVsAi5AutoLogDirectory))
                {
                    Directory.CreateDirectory(motorVsAi5AutoLogDirectory);
                }

                motorVsAi5AutoLogCts = new CancellationTokenSource();
                var token = motorVsAi5AutoLogCts.Token;

                Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            SaveMotorVsAI5ToFile();

                            await Task.Delay(30000, token); // every 30 seconds
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => AppendMessage($"Auto-Log Error: {ex.Message}"));
                        }
                    }
                });

                isMotorVsAi5AutoLogging = true;
                AppendMessage("Motor vs AI5 Auto-Logging Started.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error starting Auto-Logging: {ex.Message}");
            }
        }

        private void StopMotorVsAI5AutoLog()
        {
            try
            {
                motorVsAi5AutoLogCts?.Cancel();
                isMotorVsAi5AutoLogging = false;
                AppendMessage("Motor vs AI5 Auto-Logging Stopped.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error stopping Auto-Logging: {ex.Message}");
            }
        }

        private void SaveMotorVsAI5ToFile()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = Path.Combine(motorVsAi5AutoLogDirectory, $"MotorVsAI5_{timestamp}.csv");

            using (var writer = new StreamWriter(filename))
            {
                writer.WriteLine("MotorPosition,AI5Amplitude");
                foreach (var point in MotorVsAI5Values)
                {
                    writer.WriteLine($"{point.X:F5},{point.Y:F5}");
                }
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Cancel DAQ AutoRead loop
                if (autoReadCts != null)
                {
                    autoReadCts.Cancel();
                    autoReadCts.Dispose();
                    autoReadCts = null;
                }

                // Cancel Motor Update Loop
                if (motorVsAI5Cts != null)
                {
                    motorVsAI5Cts.Cancel();
                    motorVsAI5Cts.Dispose();
                    motorVsAI5Cts = null;
                }

                // Dispose Pipe Client
                if (daqPipe != null)
                {
                    daqPipe.Dispose();
                    daqPipe = null;
                }

                // Safely close ESP controller
                if (esp300Controller != null)
                {
                    // Only do this if esp300Controller has a Close() or Disconnect() method.
                    // If not, just set to null safely
                    // Example:
                    // esp300Controller.Disconnect();
                    esp300Controller = null;
                }

                AppendMessage("Resources cleaned up. Exiting.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error during closing: {ex.Message}");
            }
        }



        private async void RunStoredProgram1Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (esp300Controller != null)
                {
                    await SendESPCommandAsync("1EX"); // 🔥 Run program 1
                    AppendMessage("Started ESP Program 1 execution.");
                }
                else
                {
                    AppendMessage("ESP controller not connected.");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error running program 1: {ex.Message}");
            }
        }



        private async void RunStoredProgramButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (esp300Controller != null)
                {
                    await SendESPCommandAsync("10EX"); // 🔥 Run program 10
                    AppendMessage("Started ESP Program 10 execution.");
                }
                else
                {
                    AppendMessage("ESP controller not connected.");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error running program 10: {ex.Message}");
            }
        }



        //TODO! Change program to have Tables for all channels, or determine if unneccesary
        private void UpdateAIStats(int channel)
        {
            var data = aiWindowData[channel];
            if (data.Count > 0)
            {
                double mean = data.Average();
                double stddev = Math.Sqrt(data.Select(v => (v - mean) * (v - mean)).Average());
                double min = data.Min();
                double max = data.Max();
                double power = mean / 10; // Example scaling

                // Clear old entries
                AI5StatsTable.Items.Clear();

                // Insert manually
                var row = new object[]
                {
            "Ch5",
            mean.ToString("F4"),
            stddev.ToString("F4"),
            min.ToString("F4"),
            max.ToString("F4"),
            power.ToString("F4")
                };

                AI5StatsTable.Items.Add(row);
            }
        }


        /// <summary>
        /// Method that calls the for the NiDaq and ESP to run 
        /// </summary>
        private async Task StartAutoRead()
        {
            autoReadCts = new CancellationTokenSource();
            var token = autoReadCts.Token;

            int motorVsAi5Counter = 0; // Counter for slower MotorVsAI5 update

            for (int i = 0; i < aiWindowData.Length; i++) // initialize window
            {
                aiWindowData[i] = [];
            }

            window = new Mov_Avg(20); // Window for the moving average

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (daqPipe != null && daqPipe.IsConnected)
                    {
                        string response = await daqPipe.SendCommandAsync("ReadAI");

                        string[] tokens = response.Split(',');
                        for (int i = 0; i < tokens.Length && i < daqBuffer.Length; i++)
                        {
                            if (double.TryParse(tokens[i], out double value))
                                daqBuffer[i] = value;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                UpdateAIMonitor();        // 🔥 Update AI Power Checker functions
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("UI update error (UpdateAIMonitor): " + ex);
                            }
                            try
                            {
                            UpdateDAQChart();         // 🔥 Existing: Update 6-channel DAQ chart

                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("UI update error (UpdateDAQChart): " + ex);
                            }
                        });

                        motorVsAi5Counter++;
                        if (motorVsAi5Counter >= 5) // 🔥 Every 5 * 200ms = 1 second
                        {
                            Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    UpdateMotorVsAI5(); // 🔥 Existing: Update Motor vs AI5 slower
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("UI update error (MotorVsAI5): " + ex);
                                }
                            });
                            motorVsAi5Counter = 0;
                        }
                    }

                    await Task.Delay(200, token); // Regular fast cycle
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Auto read error: " + ex);
                }
            }
        }
        private void SaveAI5DataToCSV_Click(object sender, RoutedEventArgs e)
        {
            List<int> outliers = Outliers();
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    FileName = "AIData_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    using (var writer = new StreamWriter(dialog.FileName))
                    {
                        string line = "Index,Time(ms),";
                        for(int i = 0; i < 6; i++)
                        {
                            line += $"Voltage {i} (V),";
                        }
                        writer.WriteLine(line);

                        for (int i = 0; i < aiWindowData[0].Count; i++)
                        {
                            if(outliers.Contains(i))
                            {
                                continue;
                            }
                            line = $"{i},{aiTimeTracker[i]},";
                            for(int j = 0; j < 6; j++)
                            {
                                line += $"{aiWindowData[j][i]},";
                            }
                            writer.WriteLine(line);
                        }
                    }
                    AppendMessage("Saved successfully!");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error saving file: {ex.Message}");
            }
        }

        private List<int> Outliers() // remove any points beyond the standard deviation
        {
            List<int> output = [];

            var moniter = aiWindowData[0];

            for(var i = 1; i < moniter.Count-1; i++)
            {
                var delta1 = moniter[i] - moniter[i-1];
                var delta2 = moniter[i+1] - moniter[i];
                var secondDiv = Math.Abs(delta2 - delta1);

                if(secondDiv > 0.0025)
                {
                    output.Add(i);
                }
            }

            
            return output;
        }

        /// <summary>
        /// Takes Data From the DaqBuffer and Graphs the Voltage of Each Channel in Real Time
        /// </summary>
        private void UpdateDAQChart()
        {
            int samplesPerChannel = daqBuffer.Length / 6;
            int binSize = 10;  // Adjust if needed
            int binnedPoints = samplesPerChannel / binSize;

            ChartValues<double>[] allChannels = new[]
            {
        DAQChannel0Values,
        DAQChannel1Values,
        DAQChannel2Values,
        DAQChannel3Values,
        DAQChannel4Values,
        DAQChannel5Values
    };

            // Ensure all channels are sized
            foreach (var channel in allChannels)
            {
                if (channel.Count != binnedPoints)
                {
                    channel.Clear();
                    for (int i = 0; i < binnedPoints; i++)
                        channel.Add(0);
                }
            }

            // Fill data for all 6 channels
            for (int i = 0; i < binnedPoints; i++)
            {
                for (int ch = 0; ch < 6; ch++)
                {
                    double sum = 0;
                    for (int j = 0; j < binSize; j++)
                    {
                        int idx = (i * binSize + j) * 6 + ch;
                        if (idx < daqBuffer.Length)
                            sum += daqBuffer[idx];
                    }
                    allChannels[ch][i] = sum / binSize;
                }
            }
            daqUpdateCounter++;
            if (daqUpdateCounter >= 10)
            {
                daqUpdateCounter = 0;
                Dispatcher.Invoke(UpdateShotNoise_CorrectedCrossCorrelation);
            }
        }

        private void ChannelToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && int.TryParse(checkbox.Tag?.ToString(), out int index))
            {
                var series = DAQChart.Series[index] as LineSeries;
                if (series != null)
                {
                    series.StrokeThickness = 2;
                    series.Fill = Brushes.Transparent;
                    series.PointGeometry = null;
                }
            }
        }

        private void ChannelToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && int.TryParse(checkbox.Tag?.ToString(), out int index))
            {
                var series = DAQChart.Series[index] as LineSeries;
                if (series != null)
                {
                    series.StrokeThickness = 0;
                    series.Fill = Brushes.Transparent;
                    series.PointGeometry = null;
                }
            }
        }


        private void DiagonalIndexComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DiagonalIndexComboBox?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Content?.ToString(), out int i))
            {
                // i is 0..6; (7,7) is intentionally skipped
                SetDiagonalMode(true, i);
            }
        }


        private void UpdateMotorVsAI5()
        {
            try
            {
                int samplesPerChannel = daqBuffer.Length; // ai5 only

                // Accumulate AI5 samples
                for (int i = 0; i < samplesPerChannel; i++)
                    ai5AmplitudeBuffer.Add(daqBuffer[i]);

                if (ai5AmplitudeBuffer.Count >= 2000) // tune as needed
                {
                    double meanAI5 = ai5AmplitudeBuffer.Average();

                    // 🔽 use cached position, do NOT query device here
                    double position = System.Threading.Volatile.Read(ref currentESPPosition);

                    if (!double.IsNaN(position))
                    {
                        MotorVsAI5Values.Add(new ObservablePoint(position, meanAI5));
                        if (MotorVsAI5Values.Count > 100) MotorVsAI5Values.RemoveAt(0);
                    }

                    ai5AmplitudeBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error updating Motor vs AI chart: {ex.Message}");
            }
        }


        private void StopAutoRead()
        {
            if (autoReadCts != null)
            {
                autoReadCts.Cancel();
                autoReadCts.Dispose();
                autoReadCts = null;
            }
        }


        private void StartDAQButton_Click(object sender, RoutedEventArgs e)
        {
            if (daqPipe == null || !daqPipe.IsConnected)
            {
                AppendMessage("DAQ Service not connected.");
                return;
            }

            _ = StartAutoRead(); // 🔥 Start background auto-reading
            StartMotorVsAI5Update(); // 🔥 Start MotorVsAI5 live updating
        }

        private void StopDAQButton_Click(object sender, RoutedEventArgs e)
        {
            StopAutoRead(); // 🔥 Stop background loop

            if (motorVsAI5Cts != null)
            {
                motorVsAI5Cts.Cancel();
                motorVsAI5Cts.Dispose();
                motorVsAI5Cts = null;
            }
        }



        /// <summary>
        /// Gets the external clock value from the .ini file.
        /// </summary>
        private int GetExtClkValueFromIni()
        {
            if (!File.Exists(IniFilePath))
            {
                AppendMessage("Configuration file not found.");
                return 0; // Default to 0 if not found
            }

            foreach (var line in File.ReadAllLines(IniFilePath))
            {
                if (line.Trim().StartsWith("ExtClk=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.Split('=')[1], out int value))
                    {
                        return value;
                    }
                }
            }

            return 0; // Default to 0 if not found
        }

        #endregion

        #region Window Closing Handling

        /// <summary>
        /// Cleans up resources when the window is closed.
        /// </summary>
        protected override async void OnClosed(EventArgs e)
        {
            try
            {
                await TerminateExperimentAsync(); // Safely terminate the experiment
                motorController.Shutdown(); // Properly shut down the motor controller
                daqServiceProcess?.Kill(); // Ensure the DAQ service is stopped
                Release(); // Release the DAQ pipe if it exists


                // Close all log writers
                experimentLogWriter?.Close();
                motorMetricLogWriter?.Close();
                sensitivityLogWriter?.Close();
                droppedWindowLogWriter?.Close();

                AppendMessage("All resources cleaned up successfully.");    
            }
            catch (Exception ex)
            {
                // Log the exception or handle it appropriately
                AppendMessage($"Error during shutdown: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        #endregion
    }
}
