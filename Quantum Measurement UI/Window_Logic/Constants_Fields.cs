using LiveCharts;
using LiveCharts.Defaults;
using System.IO.Pipes;
using System.Windows;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;
using QuantumSqueezingUI;
using Quantum_measurement_UI;
using Windows.Networking.PushNotifications;
using Microsoft.UI.Xaml.Controls;

namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window
    {
        #region Constants

        private volatile bool EnableFFT = false; // default off

        private int selectedDAQChannel = 0; // Default to Channel 0
        private CancellationTokenSource? motorVsAI5Cts;
        public ChartValues<ObservablePoint>? AI5TimeSeriesValues { get; set; }
        public ChartValues<ObservablePoint>? AI4TimeSeriesValues { get; set; }
        public ChartValues<ObservablePoint>? AI3TimeSeriesValues { get; set; }
        public ChartValues<ObservablePoint>? AI2TimeSeriesValues { get; set; }
        public ChartValues<ObservablePoint>? AI1TimeSeriesValues { get; set; }
        public ChartValues<ObservablePoint>? AI0TimeSeriesValues { get; set; }
        public ChartValues<double> AI5HistogramValues { get; set; }


        // --- Pixel chart diagonal mode state ---
        private bool UseDiagonalMode = true;        // set false to use selectedRow/selectedColumn as before
        private int SelectedDiagonalIndex = 6;      // 0..7, but code will skip 7 -> use 6 instead
        private double PixelCumulativeSum = 0;      // running sum for the chart
        private long PixelCount = 0; // total number of samples in the cumulative sum
                                     // --- State Variables (0-based internally) ---
        private int sigR = 2, sigC = 3; // Default: User's "3,4"
        private int anchR = 6, anchC = 7; // Default: User's "7,8"

        private List<double> ai5AmplitudeBuffer = new List<double>();
        private PipeClient? daqPipe;
        private Process? daqServiceProcess = null;
        private CancellationTokenSource? nidaqMonitoringCancellationTokenSource;
        public ChartValues<double> DAQChannel0Values { get; set; }
        public ChartValues<double> DAQChannel1Values { get; set; }
        public ChartValues<double> DAQChannel2Values { get; set; }
        public ChartValues<double> DAQChannel3Values { get; set; }
        public ChartValues<double> DAQChannel4Values { get; set; }
        public ChartValues<double> DAQChannel5Values { get; set; }
        private double[] daqBuffer = new double[900]; // Example buffer (6 channels x 10000 samples)
                                                      // === Motor Position vs AI5 Amplitude ===
        private int daqUpdateCounter = 0;
        public ChartValues<ObservablePoint> MotorVsAI5Values { get; set; }
        private CancellationTokenSource? autoReadCts;
        public class StatRow
        {
            public string Channel { get; set; }
            public double Mean { get; set; }
            public double StdDev { get; set; }
            public double Min { get; set; }
            public double Max { get; set; }
            public double Power { get; set; }

            public StatRow(string ch, double mean, double std, double min, double max, double power)
            {
                Channel = ch;
                Mean = mean;
                StdDev = std;
                Min = min;
                Max = max;
                Power = power;
            }
        }

        private List<double> ai5CumulativeData = [];
        private List<double> ai5CurrentWindowData = [];
        private List<double>[] aiWindowData = new List<double>[6];
        private double ai5SampleRate = 10000; // 10kHz
        private List<double> aiTimeTracker = [];
        private Mov_Avg window;



        public ChartValues<double> ESPPositionValues { get; set; }

        // Constants for process communication using named pipe 
        private const string PipeName = "DataPipe";

        // Constants for data updates about signal, cross-correlation matrix visualization
        private const int DataPoints = 100;
        private const double UpdateInterval = 200; // milliseconds, 5 Hz update rate


        // For configuration file and experiment log
        private const string IniFilePath = @"StreamThruGPU.ini";   // Path to the GageStreamGPU .ini file
        private const string resultsBaseDirectory = @"D:\Quantum Squeezing Project\DataFiles";   // Base directory for storing experiment logs, ## can be modified for different users
        private const string exePath = @"C:\Quantum Squeezing\Quantum-Measurement-Software\GageStreamThruGPU\x64\Debug\GageStreamThruGPU.exe"; // executable path for GageStreamThruGPU program
        private const string exePathAlignment = @"C:\Quantum Squeezing\Quantum-Measurement-Software\GageStreamThruGPUAlignment\x64\Debug\GageStreamThruGPU.exe";


        string fftExePath = @"C:\Quantum Squeezing\Andy test\GageStreamThruGPU-FFT\x64\Debug\GageStreamThruGPU-FFT.exe";




        private CancellationTokenSource? motorVsAi5AutoLogCts;
        private bool isMotorVsAi5AutoLogging = false;
        private string motorVsAi5AutoLogDirectory = @"C:\Quantum Squeezing\Quantum-Measurement-Software\results\MotorVsAI5Logs\";


        #endregion

        #region Fields

        // Buffers for storing data (signal) and correlation matrix
        private short[] dataBuffer = new short[DataPoints];
        private double[] corrMatrixBuffer = new double[64];

        // Named pipe client for process communication about data including signal and cross-correlation matrix
        private NamedPipeClientStream pipeClient;

        // Task for updating data periodically
        private Task? updateTask;
        private CancellationTokenSource? cancellationTokenSource;

        // LiveCharts for signal and cross-correlation visualization
        // For SignalChart to visualize the dual channels' signals
        public SeriesCollection? SeriesCollection { get; set; }  // Collection of series for the SignalChart
        public ChartValues<double> ChannelAValues { get; set; } // Values for Channel A
        public ChartValues<double> ChannelBValues { get; set; } // Values for Channel B

        // For Heatmap to visualize the cross-correlation matrix
        public ChartValues<HeatPoint> heatValues { get; set; }

        // For PixelChart to track the selected pixel value of cross-correlation matrix over time
        public SeriesCollection? PixelSeriesCollection { get; set; }
        public ChartValues<double> PixelValues { get; set; }
        private int selectedRow = 0;
        private int selectedColumn = 0;

        // Time counter for the auto scan routine
       
        private System.Diagnostics.Stopwatch autoScanStopwatch;
        private DispatcherTimer autoScanUiTimer;

        private int autoScanTotalSteps;      // total (forward + backward) steps across all loops
        private int autoScanCompletedSteps;  // how many steps we’ve finished so far
        private int autoScanStepDwellSec;    // dwell per step (sec)


        // For Autobalance Charts
        public SeriesCollection? SignalSeriesCollectionAutobalance { get; set; }  // For Signal charts in Autobalance
        public SeriesCollection? MotorPositionSeriesCollection { get; set; }  // For Motor Positions charts in Autobalance
        public SeriesCollection? MetricSeriesCollection { get; set; }        // For Flatness Metric charts in Autobalance

        // Shared cached ESP position for the whole UI (written by the logger/sampler)
        private double currentESPPosition = double.NaN;

        // Cap for ESP position chart points (read by UpdateESPPosition, etc.)
        private const int EspChartCapacity = 300;


        // Motor controller and corresponding fields for functionalities
        private MotorController motorController;   // MotorController instance for controlling the motor

        // Fields for managing automatic continuous motion
        private CancellationTokenSource? motionCancellationTokenSource; // For cancelling motion
        private bool isPaused = true; // Flag for pausing/resuming motion
        private object pauseLock = new object(); // Lock object for pause/resume synchronization

        // Fields for updating motor positions automatically
        private CancellationTokenSource? motorPositionCancellationTokenSource; // For cancelling position updates

        // For Autobalance functionality
        private Autobalancer autobalancer;          // Autobalancer instance, used for automatic balancing

        // For ESP300 Controller which controls delay stage
        private ESP300Controller esp300Controller; // ESP300 controller instance for controlling the delay stage

        // For delay stage position logging
        private CancellationTokenSource? delayStagePositionCancellationTokenSource;
        private StreamWriter delayStageLogWriter;
        private double delayStageCurrentPosition = 0.0; // Stores the current position of the delay stage

        // For experiment log
        private string experimentLogDirectory;      // Stores the directory name for the experiment log

        private StreamWriter experimentLogWriter;
        private StreamWriter motorMetricLogWriter;
        private StreamWriter sensitivityLogWriter;
        private StreamWriter droppedWindowLogWriter;

        private string experimentLogFilePath;
        private string motorMetricLogFilePath;
        private string sensitivityLogFilePath;
        private string droppedWindowLogFilePath;

        // For experiment status and elapsed time
        private DateTime experimentStartTime;   // Stores the start time of the experiment
        private bool isExperimentRunning;       // Flag to indicate if the experiment is running
        private bool isAlignmentRunning;
        private DispatcherTimer elapsedTimer;   // Timer to update the elapsed time display
        private int extClkValue; // Stores the external clock value from the ini file

        // For the GageStreamThruGPU process
        private Process? gageStreamProcess;      // Process for starting the GageStreamThruGPU program

        private bool TimeToBalance = true; // Flag to indicate if it's time to balance the motor
        private bool SignalDropped = false; // Flag to indicate if the signal has dropped
        
        #endregion
    }
    
    public class Mov_Avg // Object to compare new data points in the NiDaq to existing points to find drops in voltage
    {
        private Queue<double> values;
        private int _size;

        public Mov_Avg(int size)
        { 
            this._size = size;
            values = new ();
        }

        public void Push(double value) // Pushes values into the queue, limiting the queue to a certain size for memory management
        {
            values.Enqueue(value);

            if(values.Count > _size)
            {
                values.Dequeue();
            }
        }

        public bool Dropped(double test) // tests whether the voltage drops by comparing test voltage to avg voltage
        {
            var avg = values.Count > 0? values.Average() : 0;

            return avg * 0.7 > test;
        }

        public double Average()
        {
            return values.Count > 0 ? values.Average() : 0;
        }
    }

 
    public class Motor3_Balancer // Object to balance Motor 3
    {
        private bool dir; // direction of the balance (true: up, false: down)
        Queue<double> values;

        MotorController controller; // Controller to move the motor
        double currentAvg;

        int UndoInstance;
        bool on;
        DateTime SinceShutDown; // Record when the controller was last shut down

        public Motor3_Balancer(MotorController controller)
        {
            dir = true;
            values = new ();
            this.controller = controller;
            currentAvg = -Double.MaxValue;
            UndoInstance = 0;
            on = true;
            SinceShutDown = DateTime.Now; // Placeholder value
        }

        private void Undo() // Move in the opposite direction of the current movement
        {
            if (dir)
            {
                controller.MoveMinus1(3);
            }
            else
            {
                controller.MovePlus1(3);
            }

            dir = !dir; // swap the direction of the balance

            if(++UndoInstance >= 2) // if the motor balance undid twice, that means that its at a local maximum 
            {
                on = false; // Turn off motor balance
                UndoInstance = 0;
                currentAvg = -Double.MaxValue;
                SinceShutDown = DateTime.Now;
            }
        }

        private void Move() // Move motor in the direction of the balance
        {
            if (dir)
            {
                controller.MovePlus10(3);
            }
            else
            {
                controller.MoveMinus10(3);
            }
        }

        public void Update(double mean) // method to update the balance 
        {
            if ((DateTime.Now - SinceShutDown).TotalHours >= 1) on = true; // Turn on the motor balance after being off for an hour
            if (!on) return; // Do not do anything if the motor balancer is off

            values.Enqueue(mean);

            // Once 10 values are addeed, check to see if the average has increased or decreased
            if(values.Count > 10 && currentAvg < values.Average()) 
            { // If increased, move the balance in the direction of motion and clear values
                currentAvg = values.Average();
                Move();
                values.Clear();
            } 
            else if(values.Count > 10) // if average has decreased, move the balance backwards
            {
                Undo();
                values.Clear();
            }
        }
                
    } 
}
