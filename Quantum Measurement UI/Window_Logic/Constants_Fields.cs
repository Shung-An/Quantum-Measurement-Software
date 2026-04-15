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
using LiveCharts.Configurations;
using System.Collections.ObjectModel;
using System.Globalization;

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



        private double PixelCumulativeSum = 0;      // running sum for the chart
        private long PixelCount = 0; // total number of samples in the cumulative sum

        // Buffers to hold recent samples for RMS calculation (per channel)
        private List<double>[] channelRmsBuffers = new List<double>[64];



        private const int RmsWindowSize = 10000;  // number of samples used for RMS (~0.1–0.5 s at typical rates)

        // In constructor or initialization method:
        private void InitializeRmsBuffers()
        {
            for (int i = 0; i < 64; i++)
            {
                channelRmsBuffers[i] = new List<double>(RmsWindowSize + 10);
            }
        }

        public ChartValues<double> RmsValues { get; } = new ChartValues<double>(Enumerable.Repeat(0.0, 64));
        public string[] ChannelLabels { get; } = Enumerable.Range(1, 64).Select(i => $"Ch{i}").ToArray();
        public Func<double, string> RmsLabelFormatter => value => value.ToString("F4");
        
        // Array to hold the latest snapshot of the 49 reduced channels for the Bar Chart
        public double[] current49ChannelValues = new double[49];

        // The 49 pairs used to reduce the 8x8 matrix.
        // Format: { row1, col1, row2, col2 } (1-based indexing as provided)
        private static readonly int[,] ReductionPairs = new int[49, 4]
        {
            {1, 1, 8, 8}, {1, 2, 7, 8}, {1, 3, 6, 8}, {1, 4, 5, 8}, {1, 5, 4, 8}, {1, 6, 3, 8}, {1, 7, 2, 8},
            {2, 2, 8, 8}, {2, 3, 7, 8}, {2, 4, 6, 8}, {2, 5, 5, 8}, {2, 6, 4, 8}, {2, 7, 3, 8},
            {3, 3, 8, 8}, {3, 4, 7, 8}, {3, 5, 6, 8}, {3, 6, 5, 8}, {3, 7, 4, 8},
            {4, 4, 8, 8}, {4, 5, 7, 8}, {4, 6, 6, 8}, {4, 7, 5, 8},
            {5, 5, 8, 8}, {5, 6, 7, 8}, {5, 7, 6, 8},
            {6, 6, 8, 8}, {6, 7, 7, 8},
            {7, 7, 8, 8},
            {2, 1, 8, 7}, {3, 1, 8, 6}, {4, 1, 8, 5}, {5, 1, 8, 4}, {6, 1, 8, 3}, {7, 1, 8, 2},
            {3, 2, 8, 7}, {4, 2, 8, 6}, {5, 2, 8, 5}, {6, 2, 8, 4}, {7, 2, 8, 3},
            {4, 3, 8, 7}, {5, 3, 8, 6}, {6, 3, 8, 5}, {7, 3, 8, 4},
            {5, 4, 8, 7}, {6, 4, 8, 6}, {7, 4, 8, 5},
            {6, 5, 8, 7}, {7, 5, 8, 6},
            {7, 6, 8, 7}
        };
        // Correct (Properties)
        public ChartValues<double> MatrixChartValues { get; set; } = new ChartValues<double>();
        public IList<string> MatrixChartLabels { get; set; } = Enumerable.Range(1, 49).Select(i => i.ToString()).ToList();
        public CartesianMapper<double> BalanceMapper { get; set; }
        // Persistent array to hold the cumulative sum of the 49 channels
        public double[] Cumulative49Channels = new double[49];

        // Trackers for skipped frames
        public long TotalFramesReceived = 0;
        public long TotalFramesSkipped = 0;

        public class MatrixBalanceItem : System.ComponentModel.INotifyPropertyChanged
        {
            private double _value;
            private double _physValue;

            public int Channel { get; set; }
            public double Value { get => _value; set { _value = value; OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(Status)); } }
            public double PhysicalValue { get => _physValue; set { _physValue = value; OnPropertyChanged(nameof(PhysicalValue)); } }

            // Stores correlation history against the live ESP scan position.
            public ChartValues<ObservablePoint> History { get; set; } = new ChartValues<ObservablePoint>();
            public Dictionary<double, int> HistoryBinCounts { get; set; } = new Dictionary<double, int>();

            public string Status => Math.Abs(_value) > 0.005 ? "High" : "Balanced";

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        // In your MainWindow class fields:
        public ObservableCollection<MatrixBalanceItem> MatrixTableData { get; set; } = new ObservableCollection<MatrixBalanceItem>();
        public SeriesCollection SelectedTrendSeries { get; set; } = new SeriesCollection();
        private DispatcherTimer historyTimer;
        private readonly double _defaultSelectedTrendYMin = -10.0;
        private readonly double _defaultSelectedTrendYMax = 10.0;

        private bool TryGetSelectedTrendScale(out double minValue, out double maxValue)
        {
            minValue = 0;
            maxValue = 0;

            if (!double.TryParse(SelectedTrendYMinTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out minValue) ||
                !double.TryParse(SelectedTrendYMaxTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out maxValue))
            {
                AppendMessage("Enter valid numeric limits for the 3rd plot scale.");
                return false;
            }

            if (minValue >= maxValue)
            {
                AppendMessage("The 3rd plot scale requires Y Min to be smaller than Y Max.");
                return false;
            }

            return true;
        }

        private void SetSelectedTrendScale(double minValue, double maxValue)
        {
            SelectedTrendYAxis.MinValue = minValue;
            SelectedTrendYAxis.MaxValue = maxValue;
            SelectedTrendYMinTextBox.Text = minValue.ToString("G", CultureInfo.InvariantCulture);
            SelectedTrendYMaxTextBox.Text = maxValue.ToString("G", CultureInfo.InvariantCulture);
        }


        // Add this under your other private fields
        private double conversionFactor_V2_per_rad2 = 1.0;
        // Add this with your other public properties in MainWindow
        public ChartValues<double> IntegratedDataHistory { get; set; } = new ChartValues<double>();


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


        private readonly object _acceptedLock = new object();
        private double[]? _lastAccepted64Scaled;     // already scaled, already passed RMS threshold
        private long _lastAcceptedValidFrameIndex;   // validFrames value for that snapshot

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

        private long TotalIntegralFrames = 0;
        private long RejectedIntegralFrames = 0;

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
