using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Quantum_measurement_UI
{
    public class ExperimentRecord
    {
        // 1. CHANGE THIS TO STRING: This prevents the crash on "2025-12-16 12:49:18"
        [JsonPropertyName("Timestamp")]
        public string TimestampString { get; set; }

        // 2. ADD THIS: The actual date object we use for Sorting and the Grid
        [JsonIgnore]
        public DateTime SortableDate { get; set; }

        public string Duration { get; set; }
        public string Filename { get; set; }
        public string Description { get; set; }
        public string Sample { get; set; }
        public bool UsedOPO { get; set; }
        public double? LaserWavelength_nm { get; set; }
        public string Detector { get; set; }
        public double DetectorResponsivity_A_per_W { get; set; }

        public List<string> Tags { get; set; } = new List<string>();

        public ExperimentConfiguration Configuration { get; set; }
        public ExperimentPhysics PhysicsData { get; set; }

        // --- Helper Properties ---
        [JsonIgnore]
        public string TagsDisplay => Tags != null ? string.Join(", ", Tags) : "";

        [JsonIgnore]
        public double ShotNoiseResult => PhysicsData?.ShotNoiseResult_urad2_rtHz ?? 0;

        [JsonIgnore]
        public string ShotNoiseDisplay
        {
            get
            {
                if (PhysicsData == null) return "-";
                if (PhysicsData.IsDarkNoiseRun || string.Equals(PhysicsData.DisplayAmplitudeUnit, "V^2", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{PhysicsData.ShotNoiseResult_V2_rtHz:N2} V^2/rtHz";
                }

                return $"{PhysicsData.ShotNoiseResult_urad2_rtHz:N2} urad^2/rtHz";
            }
        }

        [JsonIgnore]
        public double TotalPower => (PhysicsData?.Power_mW_1 ?? 0) + (PhysicsData?.Power_mW_2 ?? 0);

        [JsonIgnore]
        public double CenterPosition
        {
            get
            {
                if (PhysicsData == null) return 0;
                return (PhysicsData.ScanMin_mm + PhysicsData.ScanMax_mm) / 2.0;
            }
        }
        // --- NEW HELPERS FOR DATA GRID ---

        // 1. Scan Range (Requested)
        [JsonIgnore]
        public double ScanRange => PhysicsData?.ScanRange_mm ?? 0;

        [JsonIgnore]
        public double? ScanVelocity => PhysicsData?.ScanVelocity_mm_s;

        // 2. Motor Positions (Useful context from Metadata)
        [JsonIgnore]
        public string MotorPositions
        {
            get
            {
                if (Configuration == null) return "-";
                return $"M1:{Configuration.Motor1Position} / M2:{Configuration.Motor2Position}";
            }
        }

        [JsonIgnore]
        public string FullPath { get; set; }
    }

    // ... (Keep ExperimentConfiguration and ExperimentPhysics classes as they were) ...
    public class ExperimentConfiguration
    {
        public bool EnableFFT { get; set; }
        public string ExternalClock { get; set; }
        public string Motor1Position { get; set; }
        public string Motor2Position { get; set; }
        public string ExternalClockStatus { get; set; }
    }

    public class ExperimentPhysics
    {
        [JsonPropertyName("Power_mW_1")] public double Power_mW_1 { get; set; }
        [JsonPropertyName("Power_mW_2")] public double Power_mW_2 { get; set; }
        [JsonPropertyName("Sensitivity_V_photon")] public double Sensitivity_V_photon { get; set; }
        [JsonPropertyName("ShotNoiseResult_urad2_rtHz")] public double ShotNoiseResult_urad2_rtHz { get; set; }
        [JsonPropertyName("ShotNoiseResult_V2_rtHz")] public double ShotNoiseResult_V2_rtHz { get; set; }
        [JsonPropertyName("ScanRange_mm")] public double ScanRange_mm { get; set; }
        [JsonPropertyName("ScanMin_mm")] public double ScanMin_mm { get; set; }
        [JsonPropertyName("ScanMax_mm")] public double ScanMax_mm { get; set; }
        [JsonPropertyName("ScanVelocity_mm_s")] public double? ScanVelocity_mm_s { get; set; }
        [JsonPropertyName("ShotNoise1_V")] public double ShotNoise1_V { get; set; }
        [JsonPropertyName("ShotNoise2_V")] public double ShotNoise2_V { get; set; }
        [JsonPropertyName("SignalLevel_V2_rtHz")] public double SignalLevel_V2_rtHz { get; set; }
        [JsonPropertyName("ConversionFactor_V2_rad2")] public double ConversionFactor_V2_rad2 { get; set; }
        [JsonPropertyName("Temperature_K")] public double? Temperature_K { get; set; }
        [JsonPropertyName("OnSamplePower_mW")] public double? OnSamplePower_mW { get; set; }
        [JsonPropertyName("UsedOPO")] public bool UsedOPO { get; set; }
        [JsonPropertyName("LaserWavelength_nm")] public double? LaserWavelength_nm { get; set; }
        [JsonPropertyName("Detector")] public string Detector { get; set; }
        [JsonPropertyName("DetectorResponsivity_A_per_W")] public double DetectorResponsivity_A_per_W { get; set; }
        [JsonPropertyName("PowerDetectorAttenuatorApplied")] public bool PowerDetectorAttenuatorApplied { get; set; }
        [JsonPropertyName("PowerDetectorAttenuatorCount")] public int PowerDetectorAttenuatorCount { get; set; }
        [JsonPropertyName("PowerDetectorAttenuatorEach_dB")] public double PowerDetectorAttenuatorEach_dB { get; set; }
        [JsonPropertyName("PowerDetectorAttenuatorTotal_dB")] public double PowerDetectorAttenuatorTotal_dB { get; set; }
        [JsonPropertyName("PowerDetectorAttenuatorCorrectionFactor")] public double PowerDetectorAttenuatorCorrectionFactor { get; set; }
        [JsonPropertyName("IsDarkNoiseRun")] public bool IsDarkNoiseRun { get; set; }
        [JsonPropertyName("DisplayAmplitudeUnit")] public string DisplayAmplitudeUnit { get; set; }
    }
}
