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
using OxyPlot;

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
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            UpdateChart();
                            UpdateHeatmap();
                            Update49ChannelBarChart();
                        }));
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
                ProcessReceivedCorrelationMatrixFrame();

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
                    var localPipe = daqPipe;
                    if (localPipe != null && localPipe.IsConnected)
                    {
                        string response = await localPipe.SendCommandAsync("ReadAI");
                             
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
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Pipe is not connected."))
                {
                    Console.WriteLine("Auto read stopped: pipe disconnected.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auto read error: {ex.Message}");
                }

                await Task.Delay(100, token); // Delay for 100 milliseconds 
            }
        }

        private void ProcessReceivedCorrelationMatrixFrame()
        {
            double[] rawSnapshot = new double[64];
            Array.Copy(corrMatrixBuffer, rawSnapshot, rawSnapshot.Length);
            UpdateBackendFrameRateMetrics();

            lock (_acceptedLock)
            {
                TotalFramesReceived++;
                _lastMatrixFrameReceivedUtc = DateTime.UtcNow;
                Array.Copy(rawSnapshot, _latestRawMatrixFrame, rawSnapshot.Length);

                // Use every received frame for the live matrix/heatmap path.
                Array.Copy(rawSnapshot, _latestAcceptedHeatmapFrame, rawSnapshot.Length);

                TryAnalyzeCorrelationMatrix(rawSnapshot, out double[] current64, out double[] reduced49, out double frameRms, out double channel0Amplitude);

                long validFrames = TotalFramesReceived;
                _lastAccepted64Scaled = current64;
                _lastAcceptedValidFrameIndex = validFrames;
                _lastAcceptedFrameRms = frameRms;
                _lastAcceptedChannel0Amplitude = channel0Amplitude;
                _acceptedFrameRateFps = _backendFrameRateFps;
                Array.Copy(reduced49, _latestReduced49Frame, reduced49.Length);
                Array.Copy(reduced49, current49ChannelValues, reduced49.Length);
                UpdateSelectedPositionCumulativeHistories(reduced49);

                for (int i = 0; i < reduced49.Length; i++)
                {
                    Cumulative49Channels[i] += reduced49[i];
                }
            }
        }

        private void UpdateBackendFrameRateMetrics()
        {
            lock (_acceptedLock)
            {
                long nowTicks = Stopwatch.GetTimestamp();
                if (_lastBackendFrameTimestampTicks != 0)
                {
                    double deltaSeconds = (double)(nowTicks - _lastBackendFrameTimestampTicks) / Stopwatch.Frequency;
                    if (deltaSeconds > 0)
                    {
                        double instantaneousFps = 1.0 / deltaSeconds;
                        _backendFrameRateFps = _backendFrameRateFps <= 0
                            ? instantaneousFps
                            : 0.2 * instantaneousFps + 0.8 * _backendFrameRateFps;
                    }
                }
                _lastBackendFrameTimestampTicks = nowTicks;
            }
        }

        private void ResetBackendFrameRateMetrics()
        {
            lock (_acceptedLock)
            {
                _lastBackendFrameTimestampTicks = 0;
                _backendFrameRateFps = 0.0;
                _acceptedFrameRateFps = 0.0;
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

            if (_signalSeriesA == null || _signalSeriesB == null || SignalPlotModel == null)
            {
                return;
            }

            // Keep the LiveCharts buffers populated for the autobalance tab, but render the main signal plot with OxyPlot.
            if (ChannelAValues.Count == 0 || ChannelBValues.Count == 0)
            {
                for (int i = 0; i < dataPointCount; i++)
                {
                    ChannelAValues.Add(0);
                    ChannelBValues.Add(0);
                }
            }

            _signalSeriesA.Points.Clear();
            _signalSeriesB.Points.Clear();
            for (int i = 0; i < dataPointCount; i++)
            {
                double channelA = dataBuffer[i * 2] / 32768.0 * 240;
                double channelB = dataBuffer[i * 2 + 1] / 32768.0 * 240;
                ChannelAValues[i] = channelA;
                ChannelBValues[i] = channelB;
                _signalSeriesA.Points.Add(new DataPoint(i, channelA));
                _signalSeriesB.Points.Add(new DataPoint(i, channelB));
            }

            SignalPlotModel.InvalidatePlot(true);
        }

        /// <summary>
        /// Updates the heatmap chart with new data.
        /// </summary>
        private void UpdateHeatmap()
        {
            int matrixSize = 8; // Assuming 8x8 correlation matrix
            if (_heatmapSeries == null || HeatmapPlotModel == null)
            {
                return;
            }

            double[] heatmapFrame = new double[64];

            lock (_acceptedLock)
            {
                Array.Copy(_latestAcceptedHeatmapFrame, heatmapFrame, heatmapFrame.Length);
            }

            double[,] heatmapData = new double[matrixSize, matrixSize];
            for (int y = 0; y < matrixSize; y++)
            {
                for (int x = 0; x < matrixSize; x++)
                {
                    int index = y * matrixSize + x;
                    heatmapData[x, y] = Math.Round(heatmapFrame[index], 2);
                }
            }

            _heatmapSeries.Data = heatmapData;
            lock (_acceptedLock)
            {
                HeatmapPlotModel.Title = $"Cross Correlation | Backend FPS: {_backendFrameRateFps:F2} | Display FPS: {_acceptedFrameRateFps:F2}";
            }
            HeatmapPlotModel.InvalidatePlot(true);
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
        /// Rejects frames when channel 0 exceeds the indicator threshold.
        /// </summary>
        private void TryAnalyzeCorrelationMatrix(
            double[] sourceMatrix,
            out double[] current64,
            out double[] reduced49,
            out double frame_rms,
            out double channel0Amplitude)
        {
            current64 = Array.Empty<double>();
            reduced49 = Array.Empty<double>();
            frame_rms = 0.0;
            channel0Amplitude = 0.0;

            const double ScaleFactor = 0.0576 / 1073741824.0 * 100;

            // 1. Calculate Mean and Variance for the threshold check
            double sum = 0;
            for (int i = 0; i < sourceMatrix.Length; i++)
            {
                sum += sourceMatrix[i] * ScaleFactor;
            }
            double mean = sum / 64.0;

            double sqSum = 0;
            for (int i = 0; i < sourceMatrix.Length; i++)
            {
                sqSum += Math.Pow(sourceMatrix[i] * ScaleFactor - mean, 2);
            }
            double variance = sqSum / 64.0;

            // 2. RMS (standard deviation) remains diagnostic-only.
            frame_rms = Math.Sqrt(variance);

            // 3. Channel 0 remains diagnostic-only.
            channel0Amplitude = sourceMatrix[0] * ScaleFactor;

            // 4. Fetch & scale the 64 channels for the live processing path.
            current64 = new double[64];
            for (int i = 0; i < 64; i++)
            {
                current64[i] = sourceMatrix[i] * ScaleFactor;
            }

            reduced49 = ReduceTo49Channels(current64);
        }
        /// <summary>
        /// Updates the 49-Channel Bar Chart UI with Cumulative Sums and tracks skipped frames.
        /// </summary>
        private void Update49ChannelBarChart()
        {
            long totalFramesReceived;
            long totalFramesSkipped;
            double[] cumulativeSnapshot = new double[49];

            lock (_acceptedLock)
            {
                totalFramesReceived = TotalFramesReceived;
                totalFramesSkipped = TotalFramesSkipped;
                Array.Copy(Cumulative49Channels, cumulativeSnapshot, cumulativeSnapshot.Length);
            }

            long validFrames = totalFramesReceived;
            if (validFrames <= 0)
            {
                UpdateSkipStatsUI(totalFramesReceived, totalFramesSkipped);
                return;
            }

            for (int i = 0; i < 49; i++)
            {
                double avgV2 = cumulativeSnapshot[i] / validFrames;
                double physValue = (avgV2 / conversionFactor_V2_per_rad2) * 1e12;

                MatrixTableData[i].Value = avgV2;
                MatrixTableData[i].PhysicalValue = physValue;
            }

            UpdateSelectedAccumulationSummary();
            UpdateSelectedPositionAveragePlot();
            UpdateSkipStatsUI(totalFramesReceived, totalFramesSkipped);
        }


        /// <summary>
        /// Helper to calculate and display the skipped frame percentage.
        /// </summary>
        private void UpdateSkipStatsUI(long totalFramesReceived, long totalFramesSkipped)
        {
            if (totalFramesReceived == 0) return;

            double percentSkipped = (double)totalFramesSkipped / totalFramesReceived * 100.0;
            double backendFps;
            double acceptedFps;

            lock (_acceptedLock)
            {
                backendFps = _backendFrameRateFps;
                acceptedFps = _acceptedFrameRateFps;
            }

            SkippedFramesText.Text =
                $"Processed: {totalFramesReceived}  •  " +
                $"Backend FPS: {backendFps:F2}  •  Display FPS: {acceptedFps:F2}";
        }

        private void UpdateSelectedAccumulationSummary()
        {
            if (MatrixBalanceTable == null || SelectedAccumulationText == null || SelectedPositionAverageText == null)
            {
                return;
            }

            MatrixBalanceItem? selectedItem = MatrixBalanceTable.SelectedItems
                .Cast<MatrixBalanceItem>()
                .FirstOrDefault();

            if (selectedItem == null)
            {
                SelectedAccumulationText.Text = "Selected accumulation: none";
                SelectedPositionAverageText.Text = "Single-position cumulative average: none";
                return;
            }

            double position = System.Threading.Volatile.Read(ref currentESPPosition);
            string positionText = double.IsNaN(position)
                ? "position unavailable"
                : $"ESP {position:F4} mm";

            SelectedAccumulationText.Text =
                $"Selected accumulation: Pair {selectedItem.Channel} = {selectedItem.PhysicalValue:F2} μrad² ({positionText})";
        }

        private void UpdateSelectedPositionCumulativeHistories(double[] reduced49)
        {
            double position = System.Threading.Volatile.Read(ref currentESPPosition);
            if (double.IsNaN(position) || conversionFactor_V2_per_rad2 == 0)
            {
                return;
            }

            double binnedPosition = Math.Round(position / PositionHistoryBinSizeMm) * PositionHistoryBinSizeMm;
            int channelCount = Math.Min(reduced49.Length, MatrixTableData.Count);
            for (int i = 0; i < channelCount; i++)
            {
                MatrixBalanceItem item = MatrixTableData[i];
                if (double.IsNaN(item.CurrentPositionTrackedBin) || Math.Abs(item.CurrentPositionTrackedBin - binnedPosition) > 1e-9)
                {
                    item.CurrentPositionTrackedBin = binnedPosition;
                    item.CurrentPositionAcceptedCount = 0;
                    item.CurrentPositionRunningAverage = 0.0;
                    item.CurrentPositionCumulativeHistory.Clear();
                }

                double physicalSample = (reduced49[i] / conversionFactor_V2_per_rad2) * 1e12;
                item.CurrentPositionAcceptedCount++;
                item.CurrentPositionRunningAverage +=
                    (physicalSample - item.CurrentPositionRunningAverage) / item.CurrentPositionAcceptedCount;

                item.CurrentPositionCumulativeHistory.Add(
                    new ObservablePoint(item.CurrentPositionAcceptedCount, item.CurrentPositionRunningAverage));

                if (item.CurrentPositionCumulativeHistory.Count > 500)
                {
                    item.CurrentPositionCumulativeHistory.RemoveAt(0);
                }
            }
        }

        private void UpdateSelectedPositionAveragePlot()
        {
            if (MatrixBalanceTable == null || SelectedPositionAverageText == null || SelectedPositionAveragePlotModel == null)
            {
                return;
            }

            SelectedPositionAveragePlotModel.Series.Clear();
            var selectedItems = MatrixBalanceTable.SelectedItems.Cast<MatrixBalanceItem>().ToList();
            if (selectedItems.Count == 0)
            {
                SelectedPositionAverageText.Text = "Single-position cumulative average: none";
                SelectedPositionAveragePlotModel.InvalidatePlot(true);
                return;
            }

            foreach (var item in selectedItems)
            {
                var series = new OxyPlot.Series.LineSeries
                {
                    Title = $"Ch {item.Channel}",
                    StrokeThickness = 2
                };

                foreach (var point in item.CurrentPositionCumulativeHistory)
                {
                    series.Points.Add(new OxyPlot.DataPoint(point.X, point.Y));
                }

                SelectedPositionAveragePlotModel.Series.Add(series);
            }

            MatrixBalanceItem leadItem = selectedItems[0];
            if (double.IsNaN(leadItem.CurrentPositionTrackedBin) || leadItem.CurrentPositionAcceptedCount == 0)
            {
                SelectedPositionAverageText.Text = $"Single-position cumulative average: Pair {leadItem.Channel} waiting for accepted frames";
                return;
            }

            SelectedPositionAverageText.Text =
                $"Single-position cumulative average: Pair {leadItem.Channel} at ESP {leadItem.CurrentPositionTrackedBin:F4} mm over {leadItem.CurrentPositionAcceptedCount} accepted frames";
            SelectedPositionAveragePlotModel.InvalidatePlot(true);
        }

        private void UpdateSelectedTrendPlot()
        {
            if (MatrixBalanceTable == null || SelectedTrendPlotModel == null)
            {
                return;
            }

            SelectedTrendPlotModel.Series.Clear();
            foreach (var item in MatrixBalanceTable.SelectedItems.Cast<MatrixBalanceItem>())
            {
                var series = new OxyPlot.Series.LineSeries
                {
                    Title = $"Ch {item.Channel}",
                    StrokeThickness = 2
                };

                foreach (var point in item.History)
                {
                    series.Points.Add(new OxyPlot.DataPoint(point.X, point.Y));
                }

                SelectedTrendPlotModel.Series.Add(series);
            }

            SelectedTrendPlotModel.InvalidatePlot(true);
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

            if (_rmsSeries == null || RmsPlotModel == null)
            {
                return;
            }

            // 2. Compute MSE for each channel (against its own running mean)
            for (int ch = 0; ch < 64; ch++)
            {
                var buf = channelRmsBuffers[ch];
                double mse = 0.0;
                if (buf != null && buf.Count >= 10)
                {
                    double mean = buf.Average();
                    double sumSquaredDiff = buf.Sum(x => (x - mean) * (x - mean));
                    mse = sumSquaredDiff / buf.Count;
                }

                RmsValues[ch] = mse;
                _rmsSeries.Points[ch] = new DataPoint(ch, mse);
            }

            RmsPlotModel.InvalidatePlot(false);
        }

        private void HistoryTimer_Tick(object sender, EventArgs e)
        {
            double position = System.Threading.Volatile.Read(ref currentESPPosition);
            if (double.IsNaN(position))
            {
                return;
            }

            // Capture the current physical values for all 49 channels into their history
            foreach (var item in MatrixTableData)
            {
                UpdateHistoryAtPosition(item, position, item.PhysicalValue);
            }

            UpdateSelectedTrendPlot();
        }


        #endregion
    }
}
