using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
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

        /// <summary>
        /// Initializes the pixel chart for the selected pixel of the cross-correlation matrix over time.
        /// </summary>
        private void InitializePixelChart()
        {
            PixelValues = new ChartValues<double>();            // Initialize the PixelValues series
            PixelSeriesCollection = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Selected Pixel",
                    Values = PixelValues,                      // pixelValues binded to the Selected Pixel series
                    PointGeometry = null,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                }
            };

            PixelChart.Series = PixelSeriesCollection;          // BIND the PixelSeriesCollection to the PixelChart
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
