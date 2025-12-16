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
                        Dispatcher.Invoke(() => UpdatePixelChart());    // update the PixelChart in the UI thread
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
                const double responsivity = 0.1;           // A/W
                const double VtoW = 0.001;                 // 1 mV = 1 µW

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


        /*        /// <summary>
                /// Updates the pixel chart with the selected pixel value over time.
                /// </summary>
                private void UpdatePixelChart()
                {
                    int index = selectedRow * 8 + selectedColumn;       // Row major order
                    double selectedValue = corrMatrixBuffer[index];

                    // Update the SelectedPixelValue TextBox
                    SelectedPixelValue.Text = selectedValue.ToString("F2");

                    // Add the new value to the PixelValues series
                    PixelValues.Add(selectedValue);

                    // Keep the series length manageable
                    if (PixelValues.Count > 100) // Keep last 100 points
                    {
                        PixelValues.RemoveAt(0);
                    }
                }*/


        /// <summary>
        /// Updates the pixel chart. In diagonal mode, picks (i,i) (i != 7),
        /// subtracts (7,7) and plots the cumulative sum over time.
        /// Otherwise, uses the selected (row,col) and still subtracts (7,7),
        /// plotting the cumulative sum.
        /// </summary>
        private void SetDiagonalMode(bool enabled, int diagonalIndex = 6)
        {
            UseDiagonalMode = enabled;
            SelectedDiagonalIndex = diagonalIndex == 7 ? 6 : Math.Max(0, Math.Min(7, diagonalIndex));

            PixelCumulativeSum = 0;
            PixelCount = 0;

            if (PixelValues != null)
                PixelValues.Clear();
        }


        private void UpdatePixelChart()
        {
            if (corrMatrixBuffer == null || corrMatrixBuffer.Length < 64) return;

            const int size = 8;
            int anchorIdx = 7 * size + 7;
            double anchor = corrMatrixBuffer[anchorIdx];

            int r, c;
            if (UseDiagonalMode)
            {
                int d = SelectedDiagonalIndex;
                if (d < 0) d = 0;
                if (d > 7) d = 7;
                if (d == 7) d = 6;
                r = d; c = d;
            }
            else
            {
                r = Math.Max(0, Math.Min(size - 1, selectedRow));
                c = Math.Max(0, Math.Min(size - 1, selectedColumn));
                if (r == 7 && c == 7) { r = 6; c = 6; }
            }

            int idx = r * size + c;
            double diff = (corrMatrixBuffer[idx] - anchor)*0.24*0.24/32768/32768;

            // Increment count and sum
            PixelCount++;
            PixelCumulativeSum += diff;

            // Show both current diff and count
            SelectedPixelValue.Text = $"{diff:F2}  (Δ=({r},{c})-(7,7)) | Count: {PixelCount}";

            // Push cumulative sum to chart
            PixelValues.Add(PixelCumulativeSum);

            // Keep chart display to last 100 points, but DO NOT reset sum or count
            if (PixelValues.Count > 100)
                PixelValues.RemoveAt(0);
        }



        #endregion
    }
}
