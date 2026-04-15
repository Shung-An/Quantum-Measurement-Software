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


            motorController = new MotorController();         // Initialize MotorController instance
            DataContext = this;
            esp300Controller = new ESP300Controller
            {
                Axis = 1                  // Axis number
            };

            esp300Controller.Connect();         // Connect to the ESP300 controller

            // Initialize charts
            InitializeSignalChart();         // Initialize the signal chart data                                          
            InitializeHeatValues();         // Initialize heatmap values (8x8 grid)
            InitializeSpinNoiseMatrix();         // Initialize pixel chart of selected pixel of cross correlation matrix over time
            InitializeRmsValues();         // Initialize RMS buffers
            InitializeDAQCharts();         // Initialize DAQ charts
            InitializeAlignmentChart();         // Initialize alignment chart

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
        }

        #endregion

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
