using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.Statistics;
using ScottPlot;

namespace Quantum_measurement_UI
{
    public class QuantumAnalysisService
    {
        // --- Constants from MATLAB Config ---
        private const double BinSizeMm = 0.1;
        private const double MmToPs = 6.6;
        private const double FrameDtS = 0.025; // 25ms
        private const double ScaleFactorRawToV = (0.24 * 0.24) / (32768.0 * 32768.0);

        public class AnalysisResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        private struct MotorLogEntry
        {
            public DateTime Timestamp;
            public double Position;
        }

        /// <summary>
        /// Main entry point: Equivalent to running 'cm_pipeline_all_in_one'
        /// </summary>
        public AnalysisResult RunPipeline(string runFolder)
        {
            try
            {
                // 1. Define Paths
                string binPath = Path.Combine(runFolder, "cm.bin");
                string sensitivityPath = Path.Combine(runFolder, "sensitivity.log");
                // Find delay stage log (handling variable naming)
                string posLogPath = Directory.GetFiles(runFolder, "delay_stage_positions*.log").FirstOrDefault()
                                    ?? Directory.GetFiles(runFolder, "*motor*.log").FirstOrDefault();

                if (!File.Exists(binPath) || posLogPath == null)
                    return new AnalysisResult { Success = false, Message = "Missing cm.bin or position log." };

                // 2. Load Data
                double[,] cmData = LoadBinaryCM(binPath, out int frameCount); // [Rows=Frames, Cols=64]
                var positions = LoadPositionLog(posLogPath);
                double sensitivity = LoadSensitivity(sensitivityPath);

                // 3. Process & Interpolate
                // Generate time vector for frames (0, 0.025, 0.050...)
                double[] frameTimes = Enumerable.Range(0, frameCount).Select(i => i * FrameDtS).ToArray();

                // Convert log timestamps to relative seconds (relative to start of log)
                DateTime t0 = positions[0].Timestamp;
                double[] posTimes = positions.Select(p => (p.Timestamp - t0).TotalSeconds).ToArray();
                double[] posValues = positions.Select(p => p.Position).ToArray();

                // Create Interpolator (Linear) - Equivalent to MATLAB interp1
                var interpolator = LinearSpline.InterpolateSorted(posTimes, posValues);

                // 4. Binning Logic
                var binnedData = new Dictionary<double, List<int>>(); // BinCenter -> List of Frame Indices

                for (int i = 0; i < frameCount; i++)
                {
                    double currentTime = frameTimes[i];

                    // Safety check: Don't extrapolate beyond log time
                    if (currentTime > posTimes.Last()) break;

                    double interpolatedPos = interpolator.Interpolate(currentTime);

                    // Round to nearest bin (e.g., 0.1mm)
                    double bin = Math.Round(interpolatedPos / BinSizeMm) * BinSizeMm;

                    if (!binnedData.ContainsKey(bin)) binnedData[bin] = new List<int>();
                    binnedData[bin].Add(i);
                }

                // 5. Generate Visualizations (The "Extra Analysis" part)
                GenerateHeatmaps(runFolder, cmData, frameCount, sensitivity);
                GenerateBinPlots(runFolder, binnedData, cmData, sensitivity);

                return new AnalysisResult { Success = true, Message = "Analysis Complete." };
            }
            catch (Exception ex)
            {
                return new AnalysisResult { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        // ---------------------------------------------------------
        // DATA LOADING HELPERS
        // ---------------------------------------------------------

        private double[,] LoadBinaryCM(string path, out int frameCount)
        {
            // MATLAB: fread(fid, [64, inf], 'double')
            // C# must read bytes and convert
            byte[] bytes = File.ReadAllBytes(path);
            int doubleSize = sizeof(double);
            int totalDoubles = bytes.Length / doubleSize;
            frameCount = totalDoubles / 64;

            double[,] data = new double[frameCount, 64];

            for (int i = 0; i < frameCount; i++)
            {
                for (int j = 0; j < 64; j++)
                {
                    // Calculate byte offset
                    int offset = (i * 64 + j) * doubleSize;
                    data[i, j] = BitConverter.ToDouble(bytes, offset);
                }
            }
            return data;
        }

        private List<MotorLogEntry> LoadPositionLog(string path)
        {
            var list = new List<MotorLogEntry>();
            var lines = File.ReadAllLines(path);

            // Regex to parse: "14:30:01.123, 24.5" or similar formats
            var regex = new Regex(@"(\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)\s*[,;]\s*([+-]?\d+(?:\.\d+)?)");

            // Assuming log dates are today (logs usually only have time), 
            // we construct a full DateTime object.
            DateTime today = DateTime.Today;

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    TimeSpan ts = TimeSpan.Parse(match.Groups[1].Value);
                    double pos = double.Parse(match.Groups[2].Value);
                    list.Add(new MotorLogEntry { Timestamp = today + ts, Position = pos });
                }
            }
            return list;
        }

        private double LoadSensitivity(string path)
        {
            // Default logic if file missing or parse fails
            if (!File.Exists(path)) return 1.0;

            // Simple parser: look for last valid number
            var lines = File.ReadAllLines(path);
            foreach (var line in lines.Reverse())
            {
                // Logic depends on your specific log format
                // Assuming format: "Time: Value"
                var parts = line.Split(':');
                if (parts.Length > 1 && double.TryParse(parts.Last(), out double val))
                {
                    return val;
                }
            }
            return 1.0;
        }

        // ---------------------------------------------------------
        // VISUALIZATION GENERATORS (ScottPlot)
        // ---------------------------------------------------------

        private void GenerateHeatmaps(string folder, double[,] cmData, int frames, double sensitivity)
        {
            // Calculate Global Mean of the entire run
            double[] meanVector = new double[64];

            for (int col = 0; col < 64; col++)
            {
                double sum = 0;
                for (int row = 0; row < frames; row++) sum += cmData[row, col];
                meanVector[col] = sum / frames;
            }

            // Convert to urad^2: (Val * ScaleFactor) / Sensitivity * 1e12
            // Note: Sensitivity logic might differ slightly based on your specific math
            double ScaleToUrad(double v) => (v * ScaleFactorRawToV / sensitivity) * 1e12;

            double[,] grid8x8 = new double[8, 8];

            // Reshape 64 array -> 8x8 matrix
            for (int i = 0; i < 64; i++)
            {
                int row = i / 8; // Integer division
                int col = i % 8; // Modulo
                grid8x8[row, col] = ScaleToUrad(meanVector[i]);
            }

            // Create Plot
            var plt = new ScottPlot.Plot();

            // Add Heatmap
            var hm = plt.Add.Heatmap(grid8x8);
            hm.Colormap = new ScottPlot.Colormaps.Jet(); // Matches MATLAB 'jet'

            // Add Colorbar
            plt.Add.ColorBar(hm);

            plt.Title("Mean Covariance (µrad²)");

            // Invert Y axis to match matrix coordinates (Row 0 at top)
            plt.Axes.InvertY();

            // Save
            plt.SavePng(Path.Combine(folder, "heatmap_mean_urad2.png"), 600, 500);
        }

        private void GenerateBinPlots(string folder, Dictionary<double, List<int>> bins, double[,] cmData, double sensitivity)
        {
            // Logic for the contour plot or "Delay Stage" plot
            var plt = new ScottPlot.Plot();

            List<double> xDelay = new List<double>();
            List<double> yMetric = new List<double>();

            // Calculate a metric per bin (e.g., total energy)
            foreach (var bin in bins.OrderBy(k => k.Key))
            {
                double positionMM = bin.Key;
                double delayPs = positionMM * MmToPs;

                // Calculate mean of all frames in this bin
                double binSum = 0;
                int count = 0;

                foreach (int frameIdx in bin.Value)
                {
                    // Sum all 64 pixels for this frame
                    for (int p = 0; p < 64; p++) binSum += cmData[frameIdx, p];
                    count++;
                }

                if (count > 0)
                {
                    double avgRaw = binSum / count / 64.0; // Average per pixel per frame

                    // Convert to units
                    double valUrad = (avgRaw * ScaleFactorRawToV / sensitivity) * 1e12;

                    xDelay.Add(delayPs);
                    yMetric.Add(valUrad);
                }
            }

            // Plot scatter
            var sp = plt.Add.Scatter(xDelay.ToArray(), yMetric.ToArray());
            sp.LineWidth = 2;

            plt.Title("Squeezing vs Delay");
            plt.XLabel("Delay (ps)");
            plt.YLabel("Magnitude (µrad²)");

            plt.SavePng(Path.Combine(folder, "Squeezing_vs_Delay.png"), 800, 600);
        }
    }
}