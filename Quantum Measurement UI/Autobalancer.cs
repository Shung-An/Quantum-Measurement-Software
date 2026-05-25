using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;

namespace Quantum_measurement_UI
{
    public class Autobalancer
    {
        private readonly MotorController motorController;   // Reference to MotorController
        private readonly Func<short[]> getDataBuffer;      // Function to get data buffer
        private readonly Dispatcher dispatcher;            // Reference to Dispatcher for UI Thread operations
                                                           // Autobalancer.cs  (new field)
        private readonly Func<bool> isTimeToBalance;


        // *** Added reference to MainWindow ***
        private readonly MainWindow mainWindow;         // Reference to MainWindow

        private CancellationTokenSource autobalanceCancellationTokenSource; // Cancellation token source for autobalance

        public bool IsRunning { get; private set; } = false;    // Flag to indicate if autobalance is running

        // Chart data for motor positions and metrics
        public ChartValues<double> MotorPositionValues1 { get; private set; }
        public ChartValues<double> MotorPositionValues2 { get; private set; }
        public ChartValues<double> MetricValuesA { get; private set; }
        public ChartValues<double> MetricValuesB { get; private set; }

        private double currentMetricA;
        private double currentMetricB;
        private int currentMotor1Position;
        private int currentMotor2Position;
        private double previousDiffM1 = double.NaN;
        private double previousDiffM2 = double.NaN;

        // Direction that reduces a positive signed voltage difference for each channel.
        // These are calibrated at the start of each autobalance session.
        public int A_PosMetricReduceDir = +1; // motor 1, diff = ch1 - ch2
        public int B_PosMetricReduceDir = -1; // motor 2, diff = ch3 - ch4

        // Control/hysteresis
        private const double Deadband = 1e-6;           // inside this, do nothing
        private const double HysteresisFactor = 2.0;    // sign must exceed Deadband*HysteresisFactor to accept a reversal

        // Adaptive step limits
        private const int MinStep = 1;
        private const int MaxStep = 8;
        private double metricThreshold;  // set from Start(threshold,...)


        // *** Modified constructor to accept MainWindow reference ***
        // Autobalancer.cs  (ctor signature + assignment)
        public Autobalancer(
            MotorController motorController,
            Func<short[]> getDataBuffer,
            Dispatcher dispatcher,
            MainWindow mainWindow,
            Func<bool> isTimeToBalance) // NEW
        {
            this.motorController = motorController;
            this.getDataBuffer = getDataBuffer;
            this.dispatcher = dispatcher;
            this.mainWindow = mainWindow;
            this.isTimeToBalance = isTimeToBalance; // NEW

            MotorPositionValues1 = new ChartValues<double>();
            MotorPositionValues2 = new ChartValues<double>();
            MetricValuesA = new ChartValues<double>();
            MetricValuesB = new ChartValues<double>();
        }


        public void Start(double threshold, int numsegments)
        {
            if (IsRunning)
            {
                mainWindow.AppendMessage("Autobalance is already running.");
                mainWindow.LogExperimentEvent("Autobalance is already running.");
                return;
            }

            IsRunning = true;
            autobalanceCancellationTokenSource = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    var token = autobalanceCancellationTokenSource.Token;
                    bool reportedBalanced = false;

                    while (!token.IsCancellationRequested)
                    {
                        // 1) Wait until TimeToBalance opens
                        while (!isTimeToBalance() && !token.IsCancellationRequested)
                            await Task.Delay(50, token);
                        if (token.IsCancellationRequested) break;

                        // Stay active the whole window
                        while (isTimeToBalance() && !token.IsCancellationRequested)
                        {
                            bool balanced = await RunSingleSessionMinimize(numsegments, token, threshold);

                            if (balanced)
                            {
                                if (!reportedBalanced)
                                {
                                    dispatcher.Invoke(() =>
                                    {
                                        mainWindow.AppendMessage("[AutoBalance] Balance reached. Continuing autobalance until manually terminated.");
                                        mainWindow.LogExperimentEvent("[AutoBalance] Balance reached. Continuing autobalance until manually terminated.");
                                    });
                                    reportedBalanced = true;
                                }
                            }
                            else
                            {
                                reportedBalanced = false;
                            }

                            // Optional: small delay between sweeps to avoid thrashing
                            await Task.Delay(balanced ? 1000 : 75, token);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    dispatcher.Invoke(() => mainWindow.AppendMessage($"Error during autobalance: {ex.Message}"));
                }
                finally { IsRunning = false; }
            });
        }

      

        /// <summary>
        /// Waits until motor finishes moving.
        /// </summary>
        private async Task WaitForMotorToStop(int motorNumber, CancellationToken cancellationToken)
        {
            bool isMoving = true;
            while (isMoving && !cancellationToken.IsCancellationRequested)
            {
                motorController.IsMotionDone(motorNumber, out bool done);
                isMoving = !done;
                await Task.Delay(50, cancellationToken);
            }
        }


        private async Task WaitForMotorReady(int motorNumber, CancellationToken cancellationToken)
        {
            bool isMotionDone = false;
            while (!isMotionDone && !cancellationToken.IsCancellationRequested)
            {
                motorController.IsMotionDone(motorNumber, out isMotionDone);
                await Task.Delay(200, cancellationToken);
            }

            // Extra delay for firmware settle
            await Task.Delay(100, cancellationToken);

            // Just call the error check (logging happens internally)
            motorController.CheckForErrors();
        }


        private async Task<bool> RunSingleSessionMinimize(int numsegments, CancellationToken cancellationToken, double threshold)
        {
            dispatcher.Invoke(() =>
            {
                mainWindow.LogExperimentEvent("[AutoBalance] Coarse tuning session started.");
            });

            double tolerance = threshold > 0 ? threshold : 0.005;
            const int sampleSize = 12;
            const int maxIterationsPerSession = 10;
            const int maxTotalSessionTravelPerMotor = 24;
            const int settleDelayMs = 400;
            const int directionProbeStep = 8;

            int sessionTravelMotor1 = 0;
            int sessionTravelMotor2 = 0;
            int iteration = 0;
            bool reachedBalance = false;

            if (!await WaitForVoltageSamples(sampleSize, cancellationToken))
            {
                return false;
            }

            await CalibrateMotorDirection(1, sampleSize, directionProbeStep, settleDelayMs, tolerance, cancellationToken);
            await CalibrateMotorDirection(2, sampleSize, directionProbeStep, settleDelayMs, tolerance, cancellationToken);

            while (!cancellationToken.IsCancellationRequested &&
                   isTimeToBalance() &&
                   iteration < maxIterationsPerSession)
            {
                if (!await WaitForVoltageSamples(sampleSize, cancellationToken))
                {
                    break;
                }

                (double diffM1, double diffM2) = ReadSmoothedVoltageDiffs(sampleSize);
                currentMetricA = diffM1;
                currentMetricB = diffM2;

                UpdateVoltageDifferenceUi(diffM1, diffM2);

                motorController.GetCurrentPosition(1, out currentMotor1Position);
                motorController.GetCurrentPosition(2, out currentMotor2Position);
                UpdateChartData();

                bool movedMotor = false;

                if (Math.Abs(diffM1) <= tolerance && Math.Abs(diffM2) <= tolerance)
                {
                    reachedBalance = true;
                    break;
                }

                // === Step 2: Motor 1 ===
                if (Math.Abs(diffM1) > tolerance && sessionTravelMotor1 < maxTotalSessionTravelPerMotor)
                {
                    int step1 = ComputeDampedStep(diffM1, tolerance, previousDiffM1, maxTotalSessionTravelPerMotor - sessionTravelMotor1);
                    int dir1 = diffM1 > 0 ? A_PosMetricReduceDir : -A_PosMetricReduceDir;
                    motorController.CheckForErrors();

                    await SafeMoveMotor(1, step1 * dir1, cancellationToken);
                    sessionTravelMotor1 += step1;
                    movedMotor = true;
                }

                // === Step 3: Motor 2 ===
                if (Math.Abs(diffM2) > tolerance && sessionTravelMotor2 < maxTotalSessionTravelPerMotor)
                {
                    int step2 = ComputeDampedStep(diffM2, tolerance, previousDiffM2, maxTotalSessionTravelPerMotor - sessionTravelMotor2);
                    int dir2 = diffM2 > 0 ? B_PosMetricReduceDir : -B_PosMetricReduceDir;
                    motorController.CheckForErrors();

                    await SafeMoveMotor(2, step2 * dir2, cancellationToken);
                    sessionTravelMotor2 += step2;
                    movedMotor = true;
                }

                previousDiffM1 = diffM1;
                previousDiffM2 = diffM2;
                iteration++;

                // === Step 4: Settling Time ===
                // If we moved a motor, we must wait long enough for the DAQ to capture the physical change
                if (movedMotor)
                {
                    await Task.Delay(settleDelayMs, cancellationToken);
                }
                else
                {
                    break;
                }
            }

            dispatcher.Invoke(() =>
            {
                string outcome = reachedBalance ? "balanced" : "stopped";
                mainWindow.LogExperimentEvent($"[AutoBalance] Session {outcome} after {iteration} passes. Travel M1={sessionTravelMotor1}, M2={sessionTravelMotor2}.");
            });

            return reachedBalance;
        }

        private async Task<bool> WaitForVoltageSamples(int sampleSize, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && isTimeToBalance())
            {
                if (mainWindow.DAQChannel1Values.Count >= sampleSize &&
                    mainWindow.DAQChannel2Values.Count >= sampleSize &&
                    mainWindow.DAQChannel3Values.Count >= sampleSize &&
                    mainWindow.DAQChannel4Values.Count >= sampleSize)
                {
                    return true;
                }

                await Task.Delay(100, cancellationToken);
            }

            return false;
        }

        private (double diffM1, double diffM2) ReadSmoothedVoltageDiffs(int sampleSize)
        {
            double ch1Avg = mainWindow.DAQChannel1Values.TakeLast(sampleSize).Average();
            double ch2Avg = mainWindow.DAQChannel2Values.TakeLast(sampleSize).Average();
            double ch3Avg = mainWindow.DAQChannel3Values.TakeLast(sampleSize).Average();
            double ch4Avg = mainWindow.DAQChannel4Values.TakeLast(sampleSize).Average();

            return (ch1Avg - ch2Avg, ch3Avg - ch4Avg);
        }

        private void UpdateVoltageDifferenceUi(double diffM1, double diffM2)
        {
            dispatcher.Invoke(() =>
            {
                mainWindow.PowerDiffCh1.Text = diffM1.ToString("F4");
                mainWindow.PowerDiffCh2.Text = diffM2.ToString("F4");
            });
        }

        private async Task CalibrateMotorDirection(
            int motorNumber,
            int sampleSize,
            int probeStep,
            int settleDelayMs,
            double tolerance,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || !isTimeToBalance())
            {
                return;
            }

            (double beforeM1, double beforeM2) = ReadSmoothedVoltageDiffs(sampleSize);
            double beforeDiff = motorNumber == 1 ? beforeM1 : beforeM2;

            dispatcher.Invoke(() =>
            {
                mainWindow.LogExperimentEvent($"[AutoBalance] Calibrating Motor {motorNumber} direction with +{probeStep} step probe.");
            });

            await SafeMoveMotor(motorNumber, probeStep, cancellationToken);
            await Task.Delay(settleDelayMs, cancellationToken);

            if (!await WaitForVoltageSamples(sampleSize, cancellationToken))
            {
                return;
            }

            (double afterM1, double afterM2) = ReadSmoothedVoltageDiffs(sampleSize);
            double afterDiff = motorNumber == 1 ? afterM1 : afterM2;
            double delta = afterDiff - beforeDiff;

            await SafeMoveMotor(motorNumber, -probeStep, cancellationToken);
            await Task.Delay(settleDelayMs, cancellationToken);

            double minimumUsefulDelta = Math.Max(Math.Abs(tolerance) * 0.25, 1e-5);
            if (Math.Abs(delta) < minimumUsefulDelta)
            {
                dispatcher.Invoke(() =>
                {
                    mainWindow.LogExperimentEvent($"[AutoBalance] Motor {motorNumber} direction probe too small (delta={delta:F6}); keeping existing direction.");
                });
                return;
            }

            int reducePositiveDirection = delta < 0 ? +1 : -1;
            if (motorNumber == 1)
            {
                A_PosMetricReduceDir = reducePositiveDirection;
            }
            else
            {
                B_PosMetricReduceDir = reducePositiveDirection;
            }

            dispatcher.Invoke(() =>
            {
                mainWindow.LogExperimentEvent($"[AutoBalance] Motor {motorNumber} direction calibrated: positive diff uses {reducePositiveDirection:+#;-#;0} steps. Probe delta={delta:F6}.");
            });
        }

        private static int ComputeDampedStep(double diff, double tolerance, double previousDiff, int remainingTravelBudget)
        {
            double normalizedError = Math.Abs(diff) / Math.Max(tolerance, 1e-6);

            int step = normalizedError switch
            {
                > 8.0 => 4,
                > 4.0 => 3,
                > 2.0 => 2,
                _ => 1
            };

            // If the metric is already shrinking, keep the controller gentle instead of pushing harder.
            if (!double.IsNaN(previousDiff) && Math.Abs(diff) < Math.Abs(previousDiff))
            {
                step = Math.Max(1, step - 1);
            }

            // If we crossed zero, take the smallest possible corrective move.
            if (!double.IsNaN(previousDiff) && Math.Sign(diff) != Math.Sign(previousDiff))
            {
                step = 1;
            }

            return Math.Max(1, Math.Min(step, Math.Max(1, remainingTravelBudget)));
        }


        private async Task SafeMoveMotor(int motorNumber, int steps, CancellationToken token)
        {
            try
            {
                await MoveMotor(motorNumber, steps, token);
                await WaitForMotorReady(motorNumber, token);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("114") || ex.Message.Contains("MOTION IN PROGRESS"))
                {
                    // Just log and skip this cycle
                    dispatcher.Invoke(() =>
                    {
                        mainWindow.AppendMessage($"[AutoBalance] Motor {motorNumber}: still in motion, skipping this move.");
                    });
                }
                else
                {
                    // Other errors should still be shown
                    dispatcher.Invoke(() =>
                    {
                        mainWindow.AppendMessage($"[AutoBalance] Motor {motorNumber} error: {ex.Message}");
                    });
                }
            }
        }



        /*
                private async Task RunSingleSessionMinimize(int numsegments, CancellationToken cancellationToken, double threshold)
                {
                    const int motor1 = 1, motor2 = 2;
                    int stepSizeA = GetAdaptiveStep(currentMetricA, threshold *//* or MetricThreshold *//*);
                    int stepSizeB = GetAdaptiveStep(currentMetricB, threshold);

                    // Apply scaling when metric is far from zero
                    await CoarseTuneMotors(cancellationToken); // coarse stage

                    const double epsilon = 1e-6; // anti-chatter around zero
                    while (!cancellationToken.IsCancellationRequested && isTimeToBalance())
                    {
                        bool motor1Done = false, motor2Done = false;

                    motorController.GetCurrentPosition(1, out currentMotor1Position);
                    motorController.GetCurrentPosition(2, out currentMotor2Position);

                    dispatcher.Invoke(() =>
                    {
                        mainWindow.AppendMessage($"[AutoBalance] Session start @ M1={currentMotor1Position}, M2={currentMotor2Position}");
                        mainWindow.LogExperimentEvent($"[AutoBalance] Session start @ M1={currentMotor1Position}, M2={currentMotor2Position}");
                    });



                    short[] buf = getDataBuffer();
                    int startA = GetStartIndex(buf, 'A');
                    int startB = GetStartIndex(buf, 'B');
                    currentMetricA = ComputeFlatnessMetric(buf, numsegments, 'A', startA);
                    currentMetricB = ComputeFlatnessMetric(buf, numsegments, 'B', startB);
                    double prevA = currentMetricA, prevB = currentMetricB;

                    while (!cancellationToken.IsCancellationRequested && isTimeToBalance() && ( !motor1Done || !motor2Done))
                    {
                        // Refresh metrics (for charting & decisions)
                        buf = getDataBuffer();
                        startA = GetStartIndex(buf, 'A');
                        startB = GetStartIndex(buf, 'B');
                        currentMetricA = ComputeFlatnessMetric(buf, numsegments, 'A', startA);
                        currentMetricB = ComputeFlatnessMetric(buf, numsegments, 'B', startB);
                        UpdateChartData();

                            // === Motor 1 / Channel A ===
                            if (!motor1Done)
                            {
                                int dir1 = (currentMetricA < 0) ? -1 : +1;  // negative if metric<0, positive if metric>0
                                if (Math.Abs(currentMetricA) > 150)
                                    stepSizeA *= 1;

                                bool improved = await MoveMotorAndCheckMetric(motor1, stepSizeA * dir1, 'A', numsegments, cancellationToken, prevA);
                                await Task.Delay(100, cancellationToken);
                                if (Math.Abs(currentMetricA) < epsilon)
                                {
                                    prevA = currentMetricA;
                                    // optional: stop this motor if you want
                                }
                                else
                                {
                                    prevA = currentMetricA;
                                }
                            }

                            // === Motor 2 / Channel B ===
                            if (!motor2Done)
                            {
                                int dir2 = (currentMetricB < 0) ? +1 : -1;  // positive if metric<0, negative if metric>0

                                if (Math.Abs(currentMetricB) > 150)
                                    stepSizeB *= 8;
                                bool improved = await MoveMotorAndCheckMetric(motor2, stepSizeB * dir2, 'B', numsegments, cancellationToken, prevB);
                                await Task.Delay(100, cancellationToken);
                                if (Math.Abs(currentMetricB) < epsilon)
                                {
                                    prevB = currentMetricB;
                                    // optional: stop this motor if you want
                                }
                                else
                                {
                                    prevB = currentMetricB;
                                }
                            }

                            dispatcher.Invoke(() =>
                            {
                                     mainWindow.CalibrationMetricA.Text = currentMetricA.ToString("F4");
                                    mainWindow.
        .Text = currentMetricB.ToString("F4");
                                      });

                    }

                    dispatcher.Invoke(() =>
                    {
                        mainWindow.AppendMessage("[AutoBalance] Session complete. Waiting for next TimeToBalance window…");
                        mainWindow.LogExperimentEvent("[AutoBalance] Session complete. Waiting for next TimeToBalance window…");

                    });

                        await Task.Delay(100, cancellationToken);
                    }
                }*/


        public void Stop()
        {
            if (autobalanceCancellationTokenSource != null)
            {
                autobalanceCancellationTokenSource.Cancel();
                autobalanceCancellationTokenSource = null;
            }
            IsRunning = false;
            // *** Optionally, log that autobalance was stopped ***
            mainWindow.AppendMessage("Autobalance stopped.");
            mainWindow.LogExperimentEvent("Autobalance stopped.");
            mainWindow.LogMotorNMetric("Autobalance stopped.");
        }

   
        // Update chart data of motor positions and metrics
        private void UpdateChartData()
        {
            dispatcher.Invoke(() =>
            {
                // Update Motor Position Values
                MotorPositionValues1.Add(currentMotor1Position);
                MotorPositionValues2.Add(currentMotor2Position);

                // Keep MotorPositionValues at a manageable size
                int maxMotorPoints = 100;
                if (MotorPositionValues1.Count > maxMotorPoints)
                {
                    MotorPositionValues1.RemoveAt(0);
                    MotorPositionValues2.RemoveAt(0);
                }

                // Update Metric Values
                MetricValuesA.Add(currentMetricA);
                MetricValuesB.Add(currentMetricB);

                // Keep MetricValues at a manageable size
                int maxMetricPoints = 100;
                if (MetricValuesA.Count > maxMetricPoints)
                {
                    MetricValuesA.RemoveAt(0);
                    MetricValuesB.RemoveAt(0);
                }
            });
        }



        // Get the starting index for continous waveforms. Start Index should be the point next to the lowest point in the first cycle
        private int GetStartIndex(short[] data, char channel)
        {
            int channelOffset = (channel == 'A') ? 0 : 1;

            // Find the lowest point in the waveform
            int lowestIndex = 0;
            double lowestValue = double.MaxValue;

            for (int i = 0; i < 16; i += 2)
            {
                double value = data[i + channelOffset];

                if (value < lowestValue)
                {
                    lowestValue = value;
                    lowestIndex = i;
                }
            }

            // Start index should be the point next to the lowest point
            return lowestIndex + 2;
        }

        // Compute flatness metric for a specific channel (A or B)
        // data is the raw data buffer
        // numSegments is the number of segments to calculate for metric
        // channel is the channel to calculate the metric for ('A' or 'B')
        // startIndex is the starting index for data points being processed, the start index should be start of the waveform, the point next to the lowest point
/*        private double ComputeFlatnessMetric(short[] data, int numSegments, char channel, int startIndex)
        {
            const int segmentLength = 16;
            if (data == null || startIndex < 0 || startIndex >= data.Length) return 0.0;

            int totalSegments = (data.Length - startIndex) / segmentLength;
            int segmentsToProcess = Math.Min(numSegments, totalSegments);
            if (segmentsToProcess <= 0) return 0.0;

            int channelOffset = (channel == 'A') ? 0 : 1;
            double sumMetric = 0.0;

            for (int s = 0; s < segmentsToProcess; s++)
            {
                int baseIndex = startIndex + s * segmentLength;

                // position 2 -> i = 1 ; position 3 -> i = 2
                int idx2 = baseIndex + (1 * 2) + channelOffset;
                int idx3 = baseIndex + (2 * 2) + channelOffset;

                double v2 = (idx2 >= 0 && idx2 < data.Length) ? data[idx2] : 0.0;
                double v3 = (idx3 >= 0 && idx3 < data.Length) ? data[idx3] : 0.0;

                // Keep the sign — no Math.Abs()
                double metric = (v2 + v3) / 2 / 32768 * 240;

                sumMetric = metric;
            }

            return sumMetric;
        }*/

        private double ComputeFlatnessMetric(short[] data, int numSegments, char channel, int startIndex)
        {
            const int segmentLength = 16;
            if (data == null || startIndex < 0 || startIndex >= data.Length) return 0.0;

            int totalSegments = (data.Length - startIndex) / segmentLength;
            int segmentsToProcess = Math.Min(numSegments, totalSegments);
            if (segmentsToProcess <= 0) return 0.0;

            int channelOffset = (channel == 'A') ? 0 : 1;
            double sumMetric = 0.0;

            for (int s = 0; s <1; s++)
            {
                double v2;
                double v3;
                if (channel == 'A')
                {

                     v2 = data[2] ;
                     v3 = data[4] ;
                }
                else
                {
                     v2 = data[3];
                     v3 = data[5];
                }


                // Keep the sign — no Math.Abs()
                double metric = (v2 + v3) / 2 / 32768 * 240;

                sumMetric = metric;
            }

            return sumMetric;
        }




        // Grows step size as metric deviates from 0.
        // Example: metric <= thr => minStep; metric = 10*thr => up to maxStep.
        private int GetAdaptiveStep(double metric, double thr, int minStep = 1, int maxStep = 8)
        {
            if (thr <= 0) return minStep;
            double factor = Math.Max(1.0, metric / thr);
            int step = (int)Math.Ceiling(minStep * factor);
            if (step > maxStep) step = maxStep;
            if (step < minStep) step = minStep;
            return step;
        }


        // aysnc method to move motor and check if metric decreases 
        private async Task<bool> MoveMotorAndCheckMetric(
            int motorNumber,
            int stepSize,
            char channel,
            int numSegments,
            CancellationToken cancellationToken,
            double previousMetric)
        {
            await MoveMotor(motorNumber, stepSize, cancellationToken);

            await Task.Delay(100, cancellationToken);

            short[] currentDataBuffer = getDataBuffer();

            int startIndex = GetStartIndex(currentDataBuffer, channel);
            double newMetric = ComputeFlatnessMetric(currentDataBuffer, numSegments, channel, startIndex);

            bool didMetricDecrease = newMetric < previousMetric;

            if (channel == 'A')
                currentMetricA = newMetric;
            else
                currentMetricB = newMetric;

            return didMetricDecrease;
        }

        private async Task MoveMotor(int motorNumber, int steps, CancellationToken cancellationToken)
        {
            bool moveStatus = false;
            
            await dispatcher.InvokeAsync(() =>
            {
                moveStatus = this.motorController.MoveRelative(motorNumber, steps);
            });

            if (!moveStatus)
            {
                // *** Use AppendMessage to log error ***
                dispatcher.Invoke(() => mainWindow.AppendMessage($"Failed to move motor {motorNumber}."));
                throw new Exception($"Failed to move motor {motorNumber}.");
            }

            if (motorNumber == 1)
                currentMotor1Position += steps;
            else if (motorNumber == 2)
                currentMotor2Position += steps;

            bool isMotionDone = false;
            while (!isMotionDone)
            {
                await this.dispatcher.InvokeAsync(() =>
                {
                    this.motorController.CheckForErrors();
                    this.motorController.IsMotionDone(motorNumber, out isMotionDone);
                });

                await Task.Delay(50, cancellationToken);
            }

            // *** Log motor movement ***
            this.dispatcher.Invoke(() =>
            {
                if (motorNumber == 1)
                {

                    this.mainWindow.LogExperimentEvent($"Motor {motorNumber} moved {steps} steps to position {currentMotor1Position}");
                    this.mainWindow.LogMotorNMetric($"Motor {motorNumber} moved {steps} steps to position {currentMotor1Position}, Metric is {currentMetricA}");
                }
                else
                {
                    this.mainWindow.LogExperimentEvent($"Motor {motorNumber} moved {steps} steps to position {currentMotor2Position}");
                    this.mainWindow.LogMotorNMetric($"Motor {motorNumber} moved {steps} steps to position {currentMotor2Position}, Metric is {currentMetricB}");
                }
            });


        }
    }
}
