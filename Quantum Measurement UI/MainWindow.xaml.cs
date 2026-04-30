using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Controls;
using QuantumSqueezingUI;
using System.Collections.ObjectModel;


namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window // Logic for the Main Window is within the "Window Logic" Folder
    {
        
        #region Constructor

        public MainWindow()
        {
            // Constructor for the MainWindow class, initializes all the UI components and fields needed for the application
            InitializeComponent();          // Initialize the UI components


            motorController = new MotorController(
                BypassUsbConnections,
                deferUsbInitialization: !BypassUsbConnections);         // Initialize MotorController instance
            DataContext = this;
            esp300Controller = new ESP300Controller
            {
                Axis = 1                  // Axis number
            };

            // Initialize charts
            InitializeSignalChart();         // Initialize the signal chart data                                          
            InitializeHeatValues();         // Initialize heatmap values (8x8 grid)
            InitializeSpinNoiseMatrix();         // Initialize pixel chart of selected pixel of cross correlation matrix over time
            InitializeRmsValues();         // Initialize RMS buffers
            InitializeDAQCharts();         // Initialize DAQ charts
            InitializeAlignmentChart();         // Initialize alignment chart
            InitializeIntegralPlot();
            InitializeSelectedTrendPlot();
            InitializeSelectedPositionAveragePlot();

                                           // Initialize Autobalancer
            autobalancer = new Autobalancer(
                motorController,
                () =>
                {
                    lock (dataBuffer)
                    {
                        return (short[])dataBuffer.Clone();
                    }
                },
                Dispatcher,
                this,
                () => TimeToBalance   // NEW: gate
            );


            InitializeAutobalanceCharts();  // Initialize Autobalance Charts

            // Initialize elapsed time timer
            elapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            elapsedTimer.Tick += UpdateElapsedTime;

            Loaded += (_, _) => _ = StartUsbHardwareInitializationAsync();
        }

        #endregion

        private Task StartUsbHardwareInitializationAsync()
        {
            if (usbHardwareInitializationTask != null)
            {
                return usbHardwareInitializationTask;
            }

            usbHardwareInitializationTask = InitializeUsbHardwareAsync();
            return usbHardwareInitializationTask;
        }

        private async Task EnsureUsbHardwareInitializedAsync()
        {
            if (BypassUsbConnections)
            {
                return;
            }

            await StartUsbHardwareInitializationAsync();
        }

        private async Task InitializeUsbHardwareAsync()
        {
            if (BypassUsbConnections)
            {
                esp300Controller.Connect(bypassUsbConnection: true);
                return;
            }

            AppendMessage("USB hardware initialization started in background.");
            var totalStopwatch = Stopwatch.StartNew();

            Task motorTask = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    motorController.InitializeDevice();
                    Dispatcher.Invoke(() => AppendMessage($"Newport controller connected in {sw.Elapsed.TotalSeconds:F1}s."));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendMessage($"Newport controller connection failed after {sw.Elapsed.TotalSeconds:F1}s: {ex.Message}"));
                }
            });

            Task espTask = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                bool connected = esp300Controller.Connect(bypassUsbConnection: false);
                Dispatcher.Invoke(() =>
                {
                    string status = connected ? "connected" : "not connected";
                    AppendMessage($"ESP300 {status} in {sw.Elapsed.TotalSeconds:F1}s.");
                });
            });

            await Task.WhenAll(motorTask, espTask);
            AppendMessage($"USB hardware initialization finished in {totalStopwatch.Elapsed.TotalSeconds:F1}s.");
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void ApplySelectedTrendScale_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedTrendScale(out double minValue, out double maxValue))
                return;

            SetSelectedTrendScale(minValue, maxValue);
        }

        private void AutoScaleSelectedTrend_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedTrendScale(double.NaN, double.NaN);
            SelectedTrendYMinTextBox.Text = _defaultSelectedTrendYMin.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            SelectedTrendYMaxTextBox.Text = _defaultSelectedTrendYMax.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
