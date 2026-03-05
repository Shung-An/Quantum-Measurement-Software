using LiveCharts;
using LiveCharts.Defaults;
using System.IO.Pipes;
using System.Windows;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;
using QuantumSqueezingUI;
using Quantum_measurement_UI;
using System.Threading;
using Microsoft.UI.Xaml.Input;
using System.Windows.Media;

namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window
    {
        #region Data Update Functions

        /// <summary>
        /// Initializes the named pipe client for data communication.
        /// </summary>
        private async Task AsyncInitializePipeClient()
        {
            pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);

            // CHANGE: Connect() -> await ConnectAsync()
            // This allows the UI to stay responsive while waiting
            AppendMessage("Waiting for GageStreamThruGPU to initialize...");
            await pipeClient.ConnectAsync(10000); // 10 second timeout

            AppendMessage("Data Pipe Connected to server.");
        }

        /// <summary>
        /// Starts the task to update data periodically.
        /// </summary>
        private void StartDataUpdates()
        {
            cancellationTokenSource = new CancellationTokenSource();
            updateTask = Task.Run(() => UpdateData(cancellationTokenSource.Token));         // Start an asynchronous task to update data, running on a separate thread
        }

        /// <summary>
        /// Starts the task to update motor positions periodically.
        /// </summary>
        private void StartMotorPositionUpdates()
        {
            motorPositionCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => UpdateMotorPosition(motorPositionCancellationTokenSource.Token));
        }

        /// <summary>
        /// Periodically updates the motor position on UI.
        /// </summary>
        private async Task UpdateMotorPosition(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Update the motor position on the UI thread, motor controller can only be accessed by one thread at a time, so we need to update it on the UI thread
                    Dispatcher.Invoke(() =>
                    {

                        bool status1 = motorController.GetCurrentPosition(1, out int currentPosition1);
                        bool status2 = motorController.GetCurrentPosition(2, out int currentPosition2);

                        if (status1&&status2)
                        {
                            CalibrationMotor1Pos.Text = currentPosition1.ToString();
                            CalibrationMotor2Pos.Text = currentPosition2.ToString();
                        }
                        else
                        {
                            CalibrationMotor1Pos.Text = "Error";
                            CalibrationMotor2Pos.Text = "Error";
                        }
                    });

                    await Task.Delay(200, cancellationToken); // Wait for 200 ms
                }
            }
            catch (TaskCanceledException)
            {
                // Task was canceled
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendMessage($"Error updating motor position: {ex.Message}"));
            }
        }

        /// <summary>
        /// Periodically requests and receives data from the server.
        /// </summary>
        private async Task UpdateData(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)      // Loop until cancellation is requested
                {
                    // Wait if paused
                    if (isPaused)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    bool success = await RequestAndReceiveDataAsync(); // Request and receive data from the server

                    if (success)
                    {
                        // Update the charts with new data
                        Dispatcher.Invoke(() => UpdateChart());         // update the SignalChart in the UI thread
                        Dispatcher.Invoke(() => UpdateHeatmap());       // update the Heatmap in the UI thread
                        Dispatcher.Invoke(() => Update49ChannelBarChart());    // update the PixelChart in the UI thread
                        Dispatcher.Invoke(() => UpdateAllChannelsMSE()); // update the motor position in the UI thread
                    }

                    await Task.Delay((int)UpdateInterval, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Task was canceled
            }
            catch (Exception ex)
            {
                AppendMessage($"Exception: {ex.Message}");
            }
        }

        private double GetSignalMean(int channel)
        {
            int samplesPerChannel = daqBuffer.Length / 6;
            double sum = 0;

            for (int i = channel; i < daqBuffer.Length; i += 6)
            {
                sum += daqBuffer[i];
            }

            return sum / samplesPerChannel;
        }

        public void UpdateShotNoise_CorrectedCrossCorrelation()
        {
            try
            {
                if (DAQChannel1Values.Count == 0 || DAQChannel2Values.Count == 0 ||
                    DAQChannel3Values.Count == 0 || DAQChannel4Values.Count == 0)
                {
                    CurrentSensitivityTextBlock.Text = "Shot Noise: N/A";
                    return;
                }

                // === Constants ===
                const double eCharge = 1.6e-19;            // J/eV
                const double photonEnergy_eV = 1.6;        // 780 nm
                const double repRate = 7.6e7;                // 80 MHz
                const double gain = 24500;                 // V/A
                const double responseTime = 3.5e-9;        // 3.5 ns
                const double responsivity = 0.53;           // A/W
                const double VtoW = 0.0001;                 // 10 mV = 1 µW

                double photonEnergy_J = photonEnergy_eV * eCharge;

                // === Step 1: Average voltages for each detector ===
                double Vdet1 = (DAQChannel1Values[^1] + DAQChannel2Values[^1]) / 2.0;
                double Vdet2 = (DAQChannel3Values[^1] + DAQChannel4Values[^1]) / 2.0;

                // === Step 2: Convert to optical power (W) ===
                double P1 = Vdet1 * VtoW;
                double P2 = Vdet2 * VtoW;

                // === Step 3: Photon number per pulse ===
                double N1 = P1 / (photonEnergy_J * repRate);
                double N2 = P2 / (photonEnergy_J * repRate);

                if (N1 <= 0 || N2 <= 0)
                {
                    CurrentSensitivityTextBlock.Text = "Shot Noise: Power too low";
                    return;
                }

                // === Step 4: Sensitivity (V/photon) for both detectors ===
                double sensitivity = responsivity * photonEnergy_J / responseTime * gain;

                // === Step 5: Shot noise per pulse pair (V) for each detector ===
                double shotNoise1 = Math.Sqrt(2) * Math.Sqrt(N1) * sensitivity;
                double shotNoise2 = Math.Sqrt(2) * Math.Sqrt(N2) * sensitivity;

                // === Step 6: Shot noise signal level (V²/√Hz) ===
                double shotNoiseSignal_V2_sqrtHz = shotNoise1 * shotNoise2/ Math.Sqrt(repRate);

                // === Step 7: Conversion factor (V²/rad²) ===
                double conversion1 = 2 * N1 * sensitivity;
                double conversion2 = 2 * N2 * sensitivity;
                double conversionFactor_V2_per_rad2 = conversion1 * conversion2;
                if (this.conversionFactor_V2_per_rad2 == 1)
                {
                    this.conversionFactor_V2_per_rad2 = conversion1 * conversion2;
                    AppendMessage($"Conversion factor LOCKED at: {this.conversionFactor_V2_per_rad2:E2} V²/rad²");
                }
                // === Step 8: Convert to rad²/√Hz ===
                double noise_rad2_sqrtHz = shotNoiseSignal_V2_sqrtHz / conversionFactor_V2_per_rad2;
                double noise_μrad2_sqrtHz = noise_rad2_sqrtHz * 1e12;

                // === Display ===
                CurrentSensitivityTextBlock.Text = $"Shot Noise: {noise_μrad2_sqrtHz:F2} μrad²/√Hz";

                // === Optional debug logs ===

                LogExperimentEvent($"Vdet1 = {Vdet1:F3} V, P1 = {P1 * 1e3:F2} mW, N1 = {N1:E2}");
                LogExperimentEvent($"Vdet2 = {Vdet2:F3} V, P2 = {P2 * 1e3:F2} mW, N2 = {N2:E2}");
                LogExperimentEvent($"Sensitivity = {sensitivity:E2} V/photon");
                LogExperimentEvent($"Shot Noise1 = {shotNoise1:E2} V, Shot Noise2 = {shotNoise2:E2}");
                LogExperimentEvent($"Signal Level = {shotNoiseSignal_V2_sqrtHz:E2} V²/√Hz");
                LogExperimentEvent($"Conversion Factor = {conversionFactor_V2_per_rad2:E2} V²/rad²");
                LogExperimentEvent($"Shot Noise Result = {noise_μrad2_sqrtHz:F2} μrad²/√Hz");

                LogSensitivity($"Vdet1 = {Vdet1:F3} V, P1 = {P1 * 1e3:F2} mW, N1 = {N1:E2}");
                LogSensitivity($"Vdet2 = {Vdet2:F3} V, P2 = {P2 * 1e3:F2} mW, N2 = {N2:E2}");
                LogSensitivity($"Sensitivity = {sensitivity:E2} V/photon");
                LogSensitivity($"Shot Noise1 = {shotNoise1:E2} V, Shot Noise2 = {shotNoise2:E2}");
                LogSensitivity($"Signal Level = {shotNoiseSignal_V2_sqrtHz:E2} V²/√Hz");
                LogSensitivity($"Conversion Factor = {conversionFactor_V2_per_rad2:E2} V²/rad²");
                LogSensitivity($"Shot Noise Result = {noise_μrad2_sqrtHz:F2} μrad²/√Hz");

                Dispatcher.Invoke(() =>
                {
                    sensitivityEval.Text =                  $"{noise_μrad2_sqrtHz:F2} μrad²/√Hz";
                });

            }
            catch (Exception ex)
            {
                CurrentSensitivityTextBlock.Text = "Shot Noise: Error";
                AppendMessage($"[Corrected Shot Noise Calc Error] {ex.Message}");
            }
        }




        /// <summary>
        /// Requests data from the server and receives it.
        /// </summary>
        private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, int total)
        {
            int off = 0;
            while (off < total)
            {
                int n = await s.ReadAsync(buf, off, total - off).ConfigureAwait(false);
                if (n == 0) return false; // peer closed
                off += n;
            }
            return true;
        }

        private async Task<bool> RequestAndReceiveDataAsync()
        {
            try
            {
                // 1) Request: short 2 (unchanged)
                byte[] request = BitConverter.GetBytes((short)2);
                await pipeClient.WriteAsync(request, 0, request.Length).ConfigureAwait(false);
                await pipeClient.FlushAsync().ConfigureAwait(false);

                // 2) Sizes
                int interleavedBytes = 2 * DataPoints * sizeof(short); // A+B interleaved
                int matrixBytes = 64 * sizeof(double);            // 512

                // 3) Buffers
                byte[] interleaved = new byte[interleavedBytes];
                byte[] mBytes = new byte[matrixBytes];

                // 4) Read interleaved
                if (!await ReadExactAsync(pipeClient, interleaved, interleavedBytes).ConfigureAwait(false))
                {
                    AppendMessage("Error: Incomplete data received (interleaved).");
                    return false;
                }

                // 5) Read matrix
                if (!await ReadExactAsync(pipeClient, mBytes, matrixBytes).ConfigureAwait(false))
                {
                    AppendMessage("Error: Incomplete data received (matrix).");
                    return false;
                }

                // Ensure app buffers
                if (dataBuffer == null || dataBuffer.Length < 2 * DataPoints)
                    dataBuffer = new short[2 * DataPoints];
                if (corrMatrixBuffer == null || corrMatrixBuffer.Length < 64)
                    corrMatrixBuffer = new double[64];

                // 6) Copy: interleaved bytes → short[] dataBuffer
                Buffer.BlockCopy(interleaved, 0, dataBuffer, 0, interleavedBytes);

                // 7) Copy matrix bytes → double[] corrMatrixBuffer
                Buffer.BlockCopy(mBytes, 0, corrMatrixBuffer, 0, matrixBytes);

                return true;
            }
            catch (Exception ex)
            {
                AppendMessage($"Communication error: {ex.Message}");
                if (!pipeClient.IsConnected)
                {
                    pipeClient.Dispose();
                    pipeClient = null;
                    isPaused = true;
                }
                isPaused = true;
                return false;
            }
        }

        /// <summary>
        /// Method that enables the experiment to monitor signal status
        /// and log any signal drops.
        /// </summary>
        /// <returns></returns>
        private async Task ReadSignal() 
        {
            autoReadCts = new CancellationTokenSource();
            var token = autoReadCts.Token;

            Motor3_Balancer bal3 = new Motor3_Balancer(motorController);
            List<DateTime[]> SignalDrops = []; // Record of the Start Time and End Time of a Signal Drop

            double maxVolts = 0; // Record the maximum voltage of the signal over the hour
            DateTime start = DateTime.Now;

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
                        // Find the mean of channel 0 from the values in the Daq Buffer
                        double mean = GetSignalMean(0);
                        CheckForDrops(SignalDrops, mean);

                        if(TimeToBalance)/* bal3.Update(mean); // if the balance window is open */

                        if(mean > maxVolts)  maxVolts = mean; 

                        DateTime now = DateTime.Now;
                        if((now - start).TotalHours >= 1)
                        {
                            AppendMessage($"Peak voltage between {start:HH:mm:ss.fff} - {now:HH:mm:ss.fff}: {maxVolts}");
                            start = now;
                            maxVolts = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auto read error: {ex.Message}");
                }

                await Task.Delay(100); // Delay for 100 milliseconds 
            }
        }

        

        /// <summary>
        /// Checks for When the Laser Signal Drops and Records Time They Happen
        /// </summary>
        private void CheckForDrops(List<DateTime[]> SignalDrops, double mean)
        {
            
            if (WaitTicks <= 0)
            {
                if (window.Dropped(mean))
                {
                    DateTime[] startEnd = [DateTime.Now, DateTime.Now]; // Put the start in the beggining and placeholder for end
                    SignalDrops.Add(startEnd);
                    WaitTicks = 150; // Wait 150 Ticks before testing another value
                    TimeToBalance = false;
                    SignalDropped = true; // Set the signal dropped flag to true
                }
                else
                {
                    window.Push(mean);
                }
                lastAiUpdateTime = DateTime.Now;
            }
            else
            {
                WaitTicks--;
                if(WaitTicks <= 0)
                {
                    SignalDrops[^1][1] = DateTime.Now; // Add Time Signal Returned to Record
                    AppendMessage($"Signal Dropped Between: {SignalDrops[^1][0]:HH:mm:ss.fff} - {SignalDrops[^1][1]:HH:mm:ss.fff}");
                    LogExperimentEvent($"Signal Dropped Between: {SignalDrops[^1][0]:HH:mm:ss.fff} - {SignalDrops[^1][1]:HH:mm:ss.fff}");
                    LogDroppedWindow($"Signal Dropped Between: {SignalDrops[^1][0]:HH:mm:ss.fff} - {SignalDrops[^1][1]:HH:mm:ss.fff}");
                    TimeToBalance = true;
                    SignalDropped = false; // Reset the signal dropped flag
                }
            }
        }

        /// <summary>
        /// Updates the signal chart with new data.
        /// </summary>
        private void UpdateChart()
        {
            int dataPointCount = DataPoints / 2;

            // If the collections are empty, initialize them
            if (ChannelAValues.Count == 0 || ChannelBValues.Count == 0)
            {
                for (int i = 0; i < dataPointCount; i++)
                {
                    ChannelAValues.Add(0);
                    ChannelBValues.Add(0);
                }
            }

            for (int i = 0; i < dataPointCount; i++)
            {
                ChannelAValues[i] = dataBuffer[i * 2] / 32768.0 * 240;                  // Transform the signal value to voltage for channel A
                ChannelBValues[i] = dataBuffer[i * 2 + 1] / 32768.0 * 240;              // Transform the signal value to voltage for channel B
            }
        }

        /// <summary>
        /// Updates the heatmap chart with new data.
        /// </summary>
        private void UpdateHeatmap()
        {
            int matrixSize = 8; // Assuming 8x8 correlation matrix

            if (heatValues.Count == 0)
            {
                for (int y = 0; y < matrixSize; y++) // y is the row index
                {
                    for (int x = 0; x < matrixSize; x++) // x is the column index
                    {
                        // Initially set to zero or any default value
                        heatValues.Add(new HeatPoint(x, y, 0.0));
                    }
                }
            }

            // Update the value of each HeatPoint
            for (int y = 0; y < matrixSize; y++)    // iterate over rows
            {
                for (int x = 0; x < matrixSize; x++)  // iterate over columns
                {
                    int index = y * matrixSize + x; // Index in row-major order

                    // Update the HeatPoint with the new value
                    heatValues[index].Weight = Math.Round(corrMatrixBuffer[index], 2);
                }
            }
        }



        /// <summary>
        /// Reduces 64 raw DAQ channels into 49 differential signals based on the 8x8 matrix topology.
        /// </summary>
        private double[] ReduceTo49Channels(double[] raw64Channels)
        {
            if (raw64Channels == null || raw64Channels.Length < 64)
                return new double[49];

            double[] reduced49 = new double[49];

            for (int i = 0; i < 49; i++)
            {
                // Convert 1-based matrix coordinates to 0-based array index logic
                int r1 = ReductionPairs[i, 0] - 1;
                int c1 = ReductionPairs[i, 1] - 1;
                int r2 = ReductionPairs[i, 2] - 1;
                int c2 = ReductionPairs[i, 3] - 1;

                // index = (row * width) + col
                int index1 = (r1 * 8) + c1;
                int index2 = (r2 * 8) + c2;

                reduced49[i] = raw64Channels[index1] - raw64Channels[index2];
            }

            return reduced49;
        }

        /// <summary>
        /// Retrieves the latest 64 channels from the correlation matrix, scaled properly.
        /// Applies the strict 1e-8 RMS threshold check to ignore noise/empty frames.
        /// </summary>
        private double[]? GetLatest64Channels(out double frame_rms)
        {
            // 0. Initialize the out parameter IMMEDIATELY to prevent CS0177
            frame_rms = 0.0;

            const double ScaleFactor = 0.0576 / 1073741824.0;

            // 1. Calculate Mean and Variance for the threshold check
            double sum = 0;
            for (int i = 0; i < 64; i++)
            {
                sum += corrMatrixBuffer[i] * ScaleFactor;
            }
            double mean = sum / 64.0;

            double sqSum = 0;
            for (int i = 0; i < 64; i++)
            {
                sqSum += Math.Pow(corrMatrixBuffer[i] * ScaleFactor - mean, 2);
            }
            double variance = sqSum / 64.0;

            // 2. RMS (Standard Deviation) calculation
            // FIX: REMOVED THE WORD 'double' HERE!
            frame_rms = Math.Sqrt(variance);

            // 3. The Crucial Threshold Check (User experience: 1e-8)
            if (frame_rms < 1e-8)
            {
                return null; // Skip this frame entirely!
            }

            // 4. Fetch & Scale the 64 channels
            double[] current64 = new double[64];
            for (int i = 0; i < 64; i++)
            {
                current64[i] = corrMatrixBuffer[i] * ScaleFactor;
            }

            return current64;
        }
        /// <summary>
        /// Updates the 49-Channel Bar Chart UI with Cumulative Sums and tracks skipped frames.
        /// </summary>
        private void Update49ChannelBarChart()
        {
            TotalFramesReceived++;
            double[]? current64 = GetLatest64Channels(out double currentRms);
            if (current64 == null) { TotalFramesSkipped++; return; }

            long validFrames = TotalFramesReceived - TotalFramesSkipped;

            // ✅ Snapshot the ACCEPTED (threshold-passed) scaled frame for other charts (integral, etc.)
            lock (_acceptedLock)
            {
                _lastAccepted64Scaled = current64;           // already scaled by ScaleFactor
                _lastAcceptedValidFrameIndex = validFrames;  // ties snapshot to validFrames
            }

            double[] reduced49 = ReduceTo49Channels(current64);

            Dispatcher.Invoke(() =>
            {
                if (validFrames <= 0) return;

                for (int i = 0; i < 49; i++)
                {
                    Cumulative49Channels[i] += reduced49[i];
                    double avgV2 = Cumulative49Channels[i] / validFrames;

                    double physValue = (avgV2 / conversionFactor_V2_per_rad2) * 1e12;

                    MatrixTableData[i].Value = avgV2;
                    MatrixTableData[i].PhysicalValue = physValue;
                }
            });

            UpdateSkipStatsUI(currentRms);
        }


        /// <summary>
        /// Helper to calculate and display the skipped frame percentage.
        /// </summary>
        private void UpdateSkipStatsUI(double rmsValue)
        {
            if (TotalFramesReceived == 0) return;

            long valid = TotalFramesReceived - TotalFramesSkipped;
            double percentSkipped = (double)TotalFramesSkipped / TotalFramesReceived * 100.0;

            SkippedFramesText.Text =
                $"Skipped: {TotalFramesSkipped} / {TotalFramesReceived} ({percentSkipped:F2}%)  •  " +
                $"Valid frames: {valid}  •  RMS: {rmsValue:E2}";
        }



        private void UpdateAllChannelsMSE()
        {
            double[] latest64Channels = corrMatrixBuffer;  // assume this is your latest 64-channel data

            // 1. Add newest sample to each rolling buffer
            for (int ch = 0; ch < 64; ch++)
            {
                var buf = channelRmsBuffers[ch];
                if (buf == null)
                {
                    channelRmsBuffers[ch] = new List<double>(RmsWindowSize + 10);
                    buf = channelRmsBuffers[ch];
                }

                buf.Add(latest64Channels[ch]);

                // Keep fixed window size
                if (buf.Count > RmsWindowSize)
                    buf.RemoveAt(0);
            }

            // 2. Compute MSE for each channel (against its own running mean)
            Dispatcher.Invoke(() =>
            {
                for (int ch = 0; ch < 64; ch++)
                {
                    var buf = channelRmsBuffers[ch];
                    if (buf == null || buf.Count < 10)
                    {
                        RmsValues[ch] = 0.0;  // or use a different ChartValues if you want separate MSE display
                        continue;
                    }

                    // Running mean (simple average over window)
                    double mean = buf.Average();

                    // Mean Squared Error = average of (x - mean)²
                    double sumSquaredDiff = buf.Sum(x => (x - mean) * (x - mean));
                    double mse = sumSquaredDiff / buf.Count;

                    RmsValues[ch] = mse;
                }

                // Optional: force chart redraw
                RmsPerChannelChart?.Update(true, true);
            });
        }

        private void HistoryTimer_Tick(object sender, EventArgs e)
        {
            // Capture the current physical values for all 49 channels into their history
            foreach (var item in MatrixTableData)
            {
                item.History.Add(item.PhysicalValue);

                // Limit to 100 points
                if (item.History.Count > 100)
                {
                    item.History.RemoveAt(0);
                }
            }
        }


        #endregion
    }
}
