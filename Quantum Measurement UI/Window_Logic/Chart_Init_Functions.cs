using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using LiveCharts.Configurations;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;


using QuantumSqueezingUI;
using System.Collections.ObjectModel;


namespace Quantum_measurement_UI
{
    public partial class MainWindow : Window
    {
        private const double PositionHistoryBinSizeMm = 0.0001;

        #region Chart Initialization Functions


        /// <summary>
        /// Initializes the signal chart data with zeros.
        /// </summary>
        private void InitializeSignalChart()
        {
            // Initialize the chart series
            ChannelAValues = new ChartValues<double>();
            ChannelBValues = new ChartValues<double>();

            SeriesCollection = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Channel A",
                    Values = ChannelAValues,                    // ChannelAValues binded to the Channel A series
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                },
                new LineSeries
                {
                    Title = "Channel B",
                    Values = ChannelBValues,                    // ChannelBValues binded to the Channel B series
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                }
            };

            SignalChart.Series = SeriesCollection;            // BIND the SeriesCollection to the SignalChart

            int dataPointCount = DataPoints / 2;


            // Initialize the ChannelAValues and ChannelBValues with zeros
            for (int i = 0; i < dataPointCount; i++)
            {
                ChannelAValues.Add(0);
                ChannelBValues.Add(0);
            }
        }

        /// <summary>
        /// Initializes the heatmap values (e.g., 8x8 matrix).
        /// </summary>
        private void InitializeHeatValues()
        {
            heatValues = new ChartValues<HeatPoint>();
            int matrixSize = 8; // Assuming an 8x8 correlation matrix
            HeatSeries.Values = heatValues; // Set once         // heatValues binded to the HeatSeries

            // Initialize the HeatPoint values
            for (int y = 0; y < matrixSize; y++)        // y is the row index
            {
                for (int x = 0; x < matrixSize; x++)    // x is the column index
                {
                    // Initially set to zero or any default value
                    heatValues.Add(new HeatPoint(x, y, 0.0)); // Add a new HeatPoint to the heatValues in row-major order
                }
            }
        }

        private void InitializeSpinNoiseMatrix()
        {
            // 1. Define your threshold colors
            var balancedColor = new SolidColorBrush(Color.FromRgb(31, 119, 180)); // Blue
            var warningColor = new SolidColorBrush(Color.FromRgb(214, 39, 40));   // Red

            // 2. Set the tolerance threshold (e.g., 0.005V)
            double tolerance = 0.005;

            // 3. Create the dynamic Mapper for LiveCharts
            BalanceMapper = Mappers.Xy<double>()
                .X((value, index) => index)  // X-axis is the bar index (1 to 49)
                .Y(value => value)           // Y-axis is the actual voltage difference
                .Fill(value => Math.Abs(value) > tolerance ? warningColor : balancedColor);

            MatrixTableData.Clear();
            for (int i = 1; i <= 49; i++)
            {
                MatrixTableData.Add(new MatrixBalanceItem { Channel = i, Value = 0.0 });
            }

            // 4. Initialize the 49-channel arrays with zeros
            MatrixChartValues = new ChartValues<double>(new double[49]);
            MatrixChartLabels = Enumerable.Range(1, 49).Select(i => i.ToString()).ToArray();
            // Change this line:
            historyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; // <--- Set to 1 second

            historyTimer.Tick += (s, e) => {
                double position = System.Threading.Volatile.Read(ref currentESPPosition);
                if (double.IsNaN(position))
                {
                    return;
                }

                foreach (var item in MatrixTableData)
                {
                    UpdateHistoryAtPosition(item, position, item.PhysicalValue);
                }
            };
            historyTimer.Start();

            // 5. Ensure the XAML can find these properties
            DataContext = this;
        }

        private void UpdateHistoryAtPosition(MatrixBalanceItem item, double position, double value)
        {
            double binnedPosition = Math.Round(position / PositionHistoryBinSizeMm) * PositionHistoryBinSizeMm;

            for (int i = 0; i < item.History.Count; i++)
            {
                if (Math.Abs(item.History[i].X - binnedPosition) < 1e-9)
                {
                    int count = item.HistoryBinCounts.TryGetValue(binnedPosition, out int existingCount) ? existingCount : 1;
                    item.History[i].Y = ((item.History[i].Y * count) + value) / (count + 1);
                    item.HistoryBinCounts[binnedPosition] = count + 1;
                    return;
                }
            }

            int insertIndex = 0;
            while (insertIndex < item.History.Count && item.History[insertIndex].X < binnedPosition)
            {
                insertIndex++;
            }

            item.History.Insert(insertIndex, new ObservablePoint(binnedPosition, value));
            item.HistoryBinCounts[binnedPosition] = 1;
        }
        private long lastProcessedFrameCount = 0;

        private void InitializeAlignmentChart()
        {
            DispatcherTimer integrationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            integrationTimer.Tick += (s, e) =>
            {
                double[]? accepted64;
                long acceptedValidFrame;

                lock (_acceptedLock)
                {
                    accepted64 = _lastAccepted64Scaled;
                    acceptedValidFrame = _lastAcceptedValidFrameIndex;
                }

                if (accepted64 == null) return;

                // Only update if we have a NEW accepted frame since last tick
                if (acceptedValidFrame > lastProcessedFrameCount)
                {
                    // IMPORTANT: indices are for 8x8 diagonal-like positions in 64 array
                    double integralSum =
                       ( accepted64[1] + accepted64[9] + accepted64[17] + accepted64[25] +
                        accepted64[33] + accepted64[41] + accepted64[49] + accepted64[57]) / 0.00000732421;

                    IntegratedDataHistory.Add(integralSum);
                    if (IntegratedDataHistory.Count > 100)
                        IntegratedDataHistory.RemoveAt(0);

                    lastProcessedFrameCount = acceptedValidFrame;

                    TotalIntegralFrames = acceptedValidFrame;
                    UpdateRejectionUI();
                }
            };

            integrationTimer.Start();
        }


        private void UpdateRejectionUI()
        {
            // Follow the global skip rate from your main data update logic
            long total = TotalFramesReceived;
            long skipped = TotalFramesSkipped;

            if (total == 0) return;

            double skipRate = (double)skipped / total * 100.0;
            IntegralRejectionStatsText.Text = $"System Skip Rate: {skipped} / {total} ({skipRate:F2}%)";
        }

        private void InitializeRmsValues()
        {
            // FIX: Create actual List<double> for each of the 64 channels
            channelRmsBuffers = new List<double>[64];
            for (int i = 0; i < 64; i++)
            {
                channelRmsBuffers[i] = new List<double>(RmsWindowSize + 10);  // pre-allocate capacity
            }

         
        }


        /// <summary>
        /// Initializes the charts used in the Autobalance feature.
        /// </summary>
        private void InitializeAutobalanceCharts()
        {
            // Initialize SignalSeriesCollectionAutobalance with two series for channels A and B
            SignalSeriesCollectionAutobalance = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Channel A Signal",
                    Values = ChannelAValues,
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                },
                new LineSeries
                {
                    Title = "Channel B Signal",
                    Values = ChannelBValues,
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                }
            };
            SignalChartAutobalance.Series = SignalSeriesCollectionAutobalance;

            MotorPositionSeriesCollection = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Motor 1 Position",
                    Values = autobalancer.MotorPositionValues1,
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                },
                new LineSeries
                {
                    Title = "Motor 2 Position",
                    Values = autobalancer.MotorPositionValues2,
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                }
            };
            MotorPositionChart.Series = MotorPositionSeriesCollection;

            // Initialize MetricSeriesCollection with two series for channels A and B
            MetricSeriesCollection = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Channel A Flatness Metric",
                    Values = autobalancer.MetricValuesA,
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                },
                new LineSeries
                {
                    Title = "Channel B Flatness Metric",
                    Values = autobalancer.MetricValuesB,
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                }
            };
            FlatnessChart.Series = MetricSeriesCollection;
        }

        /// <summary>
        /// Initializes the DAQ charts and their series.
        /// </summary>
        private void InitializeDAQCharts()
        {
            // Initialize the DAQ series collection with two series for channels A and B
            ESPPositionValues = new ChartValues<double>();

            ESPPositionChart.Series = new SeriesCollection
{
    new LineSeries
    {
        Title = "ESP Position",
        Values = ESPPositionValues,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    }
};


            // Initialize DAQ Channel Values
            DAQChannel0Values = new ChartValues<double>();
            DAQChannel1Values = new ChartValues<double>();
            DAQChannel2Values = new ChartValues<double>();
            DAQChannel3Values = new ChartValues<double>();
            DAQChannel4Values = new ChartValues<double>();
            DAQChannel5Values = new ChartValues<double>();

            // Set up DAQChart with 6 series
            DAQChart.Series = new SeriesCollection
{
    new LineSeries
    {
        Title = "Channel 0",
        Values = DAQChannel0Values,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
    new LineSeries
    {
        Title = "Channel 1",
        Values = DAQChannel1Values,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
    new LineSeries
    {
        Title = "Channel 2",
        Values = DAQChannel2Values,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
    new LineSeries
    {
        Title = "Channel 3",
        Values = DAQChannel3Values,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
    new LineSeries
    {
        Title = "Channel 4",
        Values = DAQChannel4Values,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
    new LineSeries
    {
        Title = "Channel 5",
        Values = DAQChannel5Values,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    }
};



            MotorVsAI5Values = new ChartValues<ObservablePoint>();

            MotorVsAI5Chart.Series = new SeriesCollection
{
    new LineSeries
    {
        Title = "Motor Pos vs AI5",
        Values = MotorVsAI5Values,
        PointGeometrySize = 5,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    }
};

            AI5TimeSeriesValues = new ChartValues<ObservablePoint>();
            AI4TimeSeriesValues = new ChartValues<ObservablePoint>();
            AI3TimeSeriesValues = new ChartValues<ObservablePoint>();
            AI2TimeSeriesValues = new ChartValues<ObservablePoint>();
            AI1TimeSeriesValues = new ChartValues<ObservablePoint>();
            AI0TimeSeriesValues = new ChartValues<ObservablePoint>();
            AI5HistogramValues = new ChartValues<double>();

            AI5TimeSeriesChart.Series = new SeriesCollection
{
    new LineSeries
    {
        Title = "AI0 Voltage",
        Values = AI0TimeSeriesValues,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
                new LineSeries
    {
        Title = "AI1 Voltage",
        Values = AI1TimeSeriesValues,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
                new LineSeries
    {
        Title = "AI2 Voltage",
        Values = AI2TimeSeriesValues,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
                new LineSeries
    {
        Title = "AI3 Voltage",
        Values = AI3TimeSeriesValues,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
                new LineSeries
    {
        Title = "AI4 Voltage",
        Values = AI4TimeSeriesValues,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    },
                new LineSeries
    {
        Title = "AI5 Voltage",
        Values = AI5TimeSeriesValues,
        PointGeometry = null,
        StrokeThickness = 2,
        Fill = Brushes.Transparent
    }
};
        }

        #endregion
    }
}
