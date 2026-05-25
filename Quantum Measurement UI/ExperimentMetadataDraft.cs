using System.Text.Json.Serialization;

namespace Quantum_measurement_UI
{
    public class ExperimentMetadataDraft
    {
        public string Filename { get; set; } = "test.txt";
        public string Description { get; set; } = "Testing run";
        public double? Temperature_K { get; set; } = 294;
        public double? OnSamplePower_mW { get; set; }
        public bool PowerDetectorAttenuatorApplied { get; set; }
        public bool EnableFFT { get; set; }
        public List<string> Samples { get; set; } = new() { "Unknown" };
        public List<string> Tags { get; set; } = new();
        public bool UsedOpo { get; set; }
        public double? LaserWavelength_nm { get; set; } = 780;
        public string Detector { get; set; } = DetectorTypes.Si;

        [JsonIgnore]
        public double DetectorResponsivity_A_per_W => DetectorTypes.GetResponsivity(Detector, LaserWavelength_nm);
    }

    public static class DetectorTypes
    {
        public const string Si = "Si";
        public const string Pdb230C = "PDB230C (InGaAs)";
        public const string InGaAs = "InGaAs";

        public static readonly string[] All = [Si, Pdb230C];

        private static readonly (double WavelengthNm, double Responsivity)[] SiResponsivityCurve =
        [
            (320, 0.17),
            (360, 0.14),
            (380, 0.13),
            (450, 0.19),
            (500, 0.24),
            (600, 0.37),
            (700, 0.47),
            (760, 0.52),
            (800, 0.54),
            (850, 0.52),
            (900, 0.45),
            (950, 0.31),
            (1000, 0.15)
        ];

        private static readonly (double WavelengthNm, double Responsivity)[] Pdb230CResponsivityCurve =
        [
            (800, 0.15),
            (850, 0.23),
            (900, 0.38),
            (950, 0.62),
            (1000, 0.68),
            (1100, 0.75),
            (1200, 0.83),
            (1300, 0.93),
            (1400, 0.98),
            (1500, 1.01),
            (1550, 1.02),
            (1600, 1.00),
            (1650, 0.92),
            (1700, 0.18),
            (1750, 0.02),
            (1800, 0.00)
        ];

        public static double GetResponsivity(string? detector, double? wavelengthNm)
        {
            var curve = NormalizeDetector(detector) switch
            {
                Pdb230C => Pdb230CResponsivityCurve,
                _ => SiResponsivityCurve
            };

            double wavelength = wavelengthNm ?? GetDefaultWavelength(detector);
            return InterpolateResponsivity(curve, wavelength);
        }

        public static double GetDefaultWavelength(string? detector)
        {
            return NormalizeDetector(detector) == Pdb230C ? 1550.0 : 780.0;
        }

        public static double GetPowerCalibrationWavelengthNm(string? detector)
        {
            return NormalizeDetector(detector) == Pdb230C ? 1550.0 : 820.0;
        }

        public static double GetPowerCalibrationResponsivity(string? detector)
        {
            return GetResponsivity(detector, GetPowerCalibrationWavelengthNm(detector));
        }

        public static double GetPowerCalibrationVoltagePerMw(string? detector)
        {
            return 10.0;
        }

        public static string NormalizeDetector(string? detector)
        {
            return detector switch
            {
                Pdb230C => Pdb230C,
                InGaAs => Pdb230C,
                _ => Si
            };
        }

        private static double InterpolateResponsivity(
            IReadOnlyList<(double WavelengthNm, double Responsivity)> curve,
            double wavelengthNm)
        {
            if (wavelengthNm <= curve[0].WavelengthNm)
            {
                return curve[0].Responsivity;
            }

            int last = curve.Count - 1;
            if (wavelengthNm >= curve[last].WavelengthNm)
            {
                return curve[last].Responsivity;
            }

            for (int i = 1; i < curve.Count; i++)
            {
                var right = curve[i];
                if (wavelengthNm > right.WavelengthNm)
                {
                    continue;
                }

                var left = curve[i - 1];
                double fraction = (wavelengthNm - left.WavelengthNm) / (right.WavelengthNm - left.WavelengthNm);
                return left.Responsivity + fraction * (right.Responsivity - left.Responsivity);
            }

            return curve[last].Responsivity;
        }
    }
}
