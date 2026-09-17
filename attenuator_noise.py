from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd


# ============================================================
# Datafile convention used by FFT_analysis.py
# ============================================================

DATA_ROOT = Path("D:/Quantum Squeezing Project/DataFiles")
FFT_ANALYSIS_FILE = Path("fft_analysis/interleaved_fft_low_frequency_loglog.csv")

# Fill these with a run id such as "20260629_153455", a run folder, or the csv
# file itself. The loader resolves all three forms to the FFT analysis csv.
FILTER_ONLY_LIGHT_SOURCE = "20260709_120551"  # A: Filter only, light
FILTER_ATT_LIGHT_SOURCE = "20260709_115845"   # B: Filter + ATT, light
FILTER_ONLY_DARK_SOURCE = "20260706_173122"   # C: Filter only, dark
FILTER_ATT_DARK_SOURCE = "20260707_113944"    # D: Filter + ATT, dark
DIGITIZER_TERMINATOR_SOURCE = "20260617_151246"  # E: digitizer input + 50 ohm terminator

CONDITION_SOURCES = {
    "A": FILTER_ONLY_LIGHT_SOURCE,
    "B": FILTER_ATT_LIGHT_SOURCE,
    "C": FILTER_ONLY_DARK_SOURCE,
    "D": FILTER_ATT_DARK_SOURCE,
    "E": DIGITIZER_TERMINATOR_SOURCE,
}

CONDITION_LABELS = {
    "A": "A: Filter only, light",
    "B": "B: Filter + ATT, light",
    "C": "C: Filter only, dark",
    "D": "D: Filter + ATT, dark",
    "E": "E: Digitizer + 50 ohm terminator",
}

CONDITION_STYLES = {
    "A": {"linestyle": "-", "linewidth": 1.2},
    "B": {"linestyle": "-", "linewidth": 1.2},
    "C": {"linestyle": "--", "linewidth": 1.2},
    "D": {"linestyle": "--", "linewidth": 1.2},
    "E": {"linestyle": ":", "linewidth": 1.2},
}

FREQ_COL = "Frequency_Hz"
CHANNEL_COLS = {
    "ch1": "Data_1_1 interleaved ch1",
    "ch2": "Data_2_1 interleaved ch2",
}


# ============================================================
# Analysis settings
# ============================================================

ATTENUATION_DB = 20
PSD_INPUT_MODE = "linear"  # "linear" for FFT_analysis output; use "dB" only for dB PSD files.

FULL_RANGE = (1e3, 4e9)
FOCUS_WINDOWS = {
    "Near 76 MHz": (50e6, 120e6),
    "20-200 MHz": (20e6, 200e6),
    "500 MHz-1 GHz": (500e6, 1e9),
    "1-4 GHz": (1e9, 4e9),
}
BANDS = {
    "Full 1 MHz-4 GHz": (1e6, 1e9),
    "Near 76 MHz": (50e6, 120e6),
    "20-200 MHz": (20e6, 200e6),
    "500 MHz-1 GHz": (500e6, 1e9),
    "1-4 GHz": (1e9, 4e9),
}

SAVE_FIGURES = True
SHOW_FIGURES = True
OUTPUT_DIR = Path("D:/Quantum Squeezing Project/DataFiles/Attenuator_Analysis")
LOG_BINS_PER_DECADE = 35


# ============================================================
# Loading
# ============================================================

def resolve_fft_csv(source, data_root=DATA_ROOT):
    """
    Resolve a run id, run folder, or csv path to the FFT analysis csv.

    FFT_analysis.py consistently reads:
        <DATA_ROOT>/<run_id>/fft_analysis/interleaved_fft_low_frequency_loglog.csv
    """
    if source is None or str(source).strip() == "":
        raise ValueError(
            "Missing data source. Fill sources A/B/C/D/E in CONDITION_SOURCES "
            "before running the attenuator excess-noise analysis."
        )

    path = Path(source).expanduser()
    candidates = []

    if path.suffix.lower() == ".csv":
        candidates.append(path)
    else:
        candidates.append(path / FFT_ANALYSIS_FILE)
        candidates.append(path)

    if not path.is_absolute():
        data_root = Path(data_root)
        candidates.append(data_root / str(source) / FFT_ANALYSIS_FILE)
        candidates.append(data_root / str(source))

    for candidate in candidates:
        if candidate.is_file():
            return candidate

    tried = "\n  ".join(str(candidate) for candidate in candidates)
    raise FileNotFoundError(f"Could not resolve FFT csv from {source!r}. Tried:\n  {tried}")


def read_psd_csv(source):
    path = resolve_fft_csv(source)
    df = pd.read_csv(path)
    missing = [col for col in [FREQ_COL, *CHANNEL_COLS.values()] if col not in df.columns]
    if missing:
        raise KeyError(
            f"{path} is missing required columns: {missing}\n"
            f"Available columns: {list(df.columns)}"
        )
    return df


def load_condition_frames(condition_sources=CONDITION_SOURCES):
    frames = {}
    for name, source in condition_sources.items():
        if source is None or str(source).strip() == "":
            continue
        frames[name] = read_psd_csv(source)

    missing_required = [name for name in ("A", "B", "C", "D", "E") if name not in frames]
    if missing_required:
        raise ValueError(f"Missing required A/B/C/D/E sources: {missing_required}")

    return frames


# ============================================================
# Numeric helpers
# ============================================================

def db_to_linear(x_db):
    return 10 ** (np.asarray(x_db) / 10)


def linear_to_db(x):
    x = np.asarray(x)
    return 10 * np.log10(np.where(x > 0, x, np.nan))


def ratio_to_db(numerator, denominator, amplitude=False):
    numerator = np.asarray(numerator)
    denominator = np.asarray(denominator)
    with np.errstate(divide="ignore", invalid="ignore"):
        ratio = np.where((numerator > 0) & (denominator > 0), numerator / denominator, np.nan)
        multiplier = 20 if amplitude else 10
        return multiplier * np.log10(ratio)


def clip_psd(psd):
    return np.maximum(psd, 0)


def subtract_psd(signal, dark):
    # PSD subtraction must be done in linear units. Negative residuals are clipped
    # because they come from measurement scatter around the dark floor.
    return clip_psd(signal - dark)


def clean_channel_df(df, channel_col):
    out = df[[FREQ_COL, channel_col]].copy()
    out = out.rename(columns={channel_col: "PSD"})
    out = out.replace([np.inf, -np.inf], np.nan).dropna()
    out = out[out[FREQ_COL] > 0]
    return out.sort_values(FREQ_COL)


def get_linear_psd(df, channel_col, psd_input_mode=PSD_INPUT_MODE):
    cleaned = clean_channel_df(df, channel_col)
    freq = cleaned[FREQ_COL].to_numpy()
    psd = cleaned["PSD"].to_numpy()

    if psd_input_mode.lower() == "db":
        psd = db_to_linear(psd)
    elif psd_input_mode.lower() != "linear":
        raise ValueError("PSD_INPUT_MODE must be 'linear' or 'dB'.")

    return freq, psd


def make_log_frequency_grid(frames, channel_col, full_range=FULL_RANGE, points_per_decade=1200):
    ranges = []
    for df in frames:
        cleaned = clean_channel_df(df, channel_col)
        ranges.append((cleaned[FREQ_COL].min(), cleaned[FREQ_COL].max()))

    f_min = max([low for low, _ in ranges] + [full_range[0]])
    f_max = min([high for _, high in ranges] + [full_range[1]])

    if f_min >= f_max:
        raise ValueError(f"No overlapping frequency range for {channel_col}: {f_min} >= {f_max}")

    decades = np.log10(f_max) - np.log10(f_min)
    n_points = max(2, int(np.ceil(points_per_decade * decades)))
    return np.logspace(np.log10(f_min), np.log10(f_max), n_points)


def make_native_frequency_grid(
    frames,
    channel_col,
    full_range=FULL_RANGE,
    reference_name="A",
    required_names=("A", "B", "C", "D", "E"),
):
    """
    Use the FFT frequency bins written by the measurement software.

    The Quantum Measurement UI writes Frequency_Hz as bin * sampleRateHz / fftLength.
    Keeping those native bins avoids changing the apparent FFT bin width in this
    post-processing step.
    """
    reference_freq, _ = get_linear_psd(frames[reference_name], channel_col)

    ranges = []
    for name in required_names:
        freq, _ = get_linear_psd(frames[name], channel_col)
        ranges.append((np.nanmin(freq), np.nanmax(freq)))

    f_min = max([low for low, _ in ranges] + [full_range[0]])
    f_max = min([high for _, high in ranges] + [full_range[1]])

    if f_min >= f_max:
        raise ValueError(f"No overlapping frequency range for {channel_col}: {f_min} >= {f_max}")

    mask = (reference_freq >= f_min) & (reference_freq <= f_max)
    f_grid = reference_freq[mask]
    if f_grid.size < 2:
        raise ValueError(f"Native frequency grid for {channel_col} has fewer than two bins.")

    return f_grid


def estimate_bin_width_hz(freq_hz):
    diffs = np.diff(np.asarray(freq_hz, dtype=float))
    diffs = diffs[np.isfinite(diffs) & (diffs > 0)]
    if diffs.size == 0:
        return np.nan
    return float(np.nanmedian(diffs))


def format_bin_width_hz(bin_width_hz):
    if not np.isfinite(bin_width_hz):
        return "unknown"
    return f"{bin_width_hz:.1f} Hz"


def interpolate_to_grid(df, channel_col, f_grid, psd_input_mode=PSD_INPUT_MODE):
    freq, psd = get_linear_psd(df, channel_col, psd_input_mode)
    return np.interp(f_grid, freq, psd, left=np.nan, right=np.nan)


def integrate_psd(freq, psd_linear, f_low, f_high):
    mask = (freq >= f_low) & (freq <= f_high)
    if np.count_nonzero(mask) < 2:
        return np.nan
    return np.sqrt(np.trapezoid(psd_linear[mask], freq[mask]))


def safe_name(text):
    return (
        text.replace(" ", "_")
        .replace("/", "_")
        .replace("-", "_")
        .replace(":", "_")
    )


# ============================================================
# Counterfactual analysis
# ============================================================

def analyze_channel(
    frames,
    channel_name,
    channel_col,
    attenuation_db=ATTENUATION_DB,
    full_range=FULL_RANGE,
    bands=BANDS,
    psd_input_mode=PSD_INPUT_MODE,
):
    f_grid = make_native_frequency_grid(
        frames,
        channel_col=channel_col,
        full_range=full_range,
    )
    bin_width_hz = estimate_bin_width_hz(f_grid)

    psd = {
        name: interpolate_to_grid(df, channel_col, f_grid, psd_input_mode)
        for name, df in frames.items()
    }

    attenuation_power_factor = 10 ** (attenuation_db / 10)

    # A-C and B-D are still useful diagnostics, but the main ideal curves below
    # use E as the no-added-noise digitizer floor.
    light_filter = subtract_psd(psd["A"], psd["C"])
    light_att = subtract_psd(psd["B"], psd["D"])

    # Ideal no-added-noise attenuator predictions. E is the digitizer floor and
    # is not attenuated; only the portion of the upstream spectrum above E is.
    ideal_dark = psd["E"] + subtract_psd(psd["C"], psd["E"]) / attenuation_power_factor
    ideal_light = psd["E"] + subtract_psd(psd["A"], psd["E"]) / attenuation_power_factor
    dark_excess_psd = subtract_psd(psd["D"], ideal_dark)
    light_excess_psd = subtract_psd(psd["B"], ideal_light)

    # Backward-compatible aliases for older helper plots that are no longer in
    # the default figure set.
    ideal_light_att = subtract_psd(psd["A"], psd["E"]) / attenuation_power_factor
    b_ideal = ideal_light
    dark_path_ideal = ideal_dark
    attenuator_excess_psd = light_excess_psd

    integrated_rows = []
    for band_name, (f_low, f_high) in bands.items():
        v_light_filter = integrate_psd(f_grid, light_filter, f_low, f_high)
        v_ideal_light_att = integrate_psd(f_grid, ideal_light_att, f_low, f_high)
        v_light_att = integrate_psd(f_grid, light_att, f_low, f_high)
        v_filter_dark = integrate_psd(f_grid, psd["C"], f_low, f_high)
        v_att_dark = integrate_psd(f_grid, psd["D"], f_low, f_high)
        v_digitizer = integrate_psd(f_grid, psd["E"], f_low, f_high)
        v_dark_ideal = integrate_psd(f_grid, ideal_dark, f_low, f_high)
        v_light_ideal = integrate_psd(f_grid, ideal_light, f_low, f_high)
        v_b_ideal = v_light_ideal
        v_b_measured = integrate_psd(f_grid, psd["B"], f_low, f_high)
        v_dark_excess = integrate_psd(f_grid, dark_excess_psd, f_low, f_high)
        v_light_excess = integrate_psd(f_grid, light_excess_psd, f_low, f_high)

        integrated_rows.append(
            {
                "channel": channel_name,
                "band": band_name,
                "f_low_Hz": f_low,
                "f_high_Hz": f_high,
                "C_filter_only_dark_RMS_like": v_filter_dark,
                "D_filter_att_dark_RMS_like": v_att_dark,
                "E_digitizer_terminator_RMS_like": v_digitizer,
                "I_dark_ideal_RMS_like": v_dark_ideal,
                "A_filter_only_light_RMS_like": integrate_psd(f_grid, psd["A"], f_low, f_high),
                "B_filter_att_light_RMS_like": v_b_measured,
                "I_light_ideal_RMS_like": v_light_ideal,
                "dark_excess_RMS_like": v_dark_excess,
                "light_excess_RMS_like": v_light_excess,
                "D_over_I_dark_dB": ratio_to_db(v_att_dark, v_dark_ideal, amplitude=True),
                "B_over_I_light_dB": ratio_to_db(v_b_measured, v_light_ideal, amplitude=True),
                "filter_only_light_dependent_RMS_like": v_light_filter,
                "ideal_after_20dB_light_dependent_RMS_like": v_ideal_light_att,
                "measured_att_light_dependent_RMS_like": v_light_att,
                "filter_att_dark_floor_RMS_like": v_att_dark,
                "B_measured_total_RMS_like": v_b_measured,
                "B_ideal_counterfactual_RMS_like": v_b_ideal,
                "attenuator_excess_RMS_like": v_light_excess,
                "measured_B_over_B_ideal_dB": ratio_to_db(v_b_measured, v_b_ideal, amplitude=True),
                "actual_light_dependent_attenuation_dB": ratio_to_db(
                    v_light_att,
                    v_light_filter,
                    amplitude=True,
                ),
            }
        )

    return {
        "channel": channel_name,
        "frequency_Hz": f_grid,
        "bin_width_Hz": bin_width_hz,
        "psd": psd,
        "psd_dB": {name: linear_to_db(values) for name, values in psd.items()},
        "light_filter": light_filter,
        "light_att": light_att,
        "ideal_dark": ideal_dark,
        "ideal_light": ideal_light,
        "dark_excess_psd": dark_excess_psd,
        "light_excess_psd": light_excess_psd,
        "ideal_light_att": ideal_light_att,
        "b_ideal": b_ideal,
        "dark_path_ideal": dark_path_ideal,
        "attenuator_excess_psd": attenuator_excess_psd,
        "light_filter_dB": linear_to_db(light_filter),
        "light_att_dB": linear_to_db(light_att),
        "ideal_dark_dB": linear_to_db(ideal_dark),
        "ideal_light_dB": linear_to_db(ideal_light),
        "ideal_light_att_dB": linear_to_db(ideal_light_att),
        "b_ideal_dB": linear_to_db(b_ideal),
        "dark_path_ideal_dB": linear_to_db(dark_path_ideal),
        "dark_excess_above_ideal_dB": ratio_to_db(psd["D"], ideal_dark),
        "light_excess_above_ideal_dB": ratio_to_db(psd["B"], ideal_light),
        "excess_above_ideal_dB": ratio_to_db(psd["B"], b_ideal),
        "actual_light_attenuation_dB": ratio_to_db(light_att, light_filter),
        "integrated_noise_table": pd.DataFrame(integrated_rows),
    }


# ============================================================
# Plotting
# ============================================================

def save_or_show(fig, output_path=None, save_figures=SAVE_FIGURES, show_figures=SHOW_FIGURES):
    if save_figures and output_path is not None:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        fig.savefig(output_path, dpi=300, bbox_inches="tight")
    if show_figures:
        plt.show()
    else:
        plt.close(fig)


def log_bin_summary(freq_hz, values, bins_per_decade=LOG_BINS_PER_DECADE):
    freq_hz = np.asarray(freq_hz)
    values = np.asarray(values)
    mask = np.isfinite(freq_hz) & np.isfinite(values) & (freq_hz > 0)
    freq_hz = freq_hz[mask]
    values = values[mask]

    if len(freq_hz) < 2:
        return np.array([]), np.array([]), np.array([]), np.array([])

    f_min = np.nanmin(freq_hz)
    f_max = np.nanmax(freq_hz)
    n_decades = np.log10(f_max) - np.log10(f_min)
    n_bins = max(1, int(np.ceil(n_decades * bins_per_decade)))
    edges = np.logspace(np.log10(f_min), np.log10(f_max), n_bins + 1)

    centers = []
    q25 = []
    med = []
    q75 = []
    for low, high in zip(edges[:-1], edges[1:]):
        bin_mask = (freq_hz >= low) & (freq_hz < high)
        if np.count_nonzero(bin_mask) < 2:
            continue
        bin_values = values[bin_mask]
        centers.append(np.sqrt(low * high))
        q25.append(np.nanpercentile(bin_values, 25))
        med.append(np.nanmedian(bin_values))
        q75.append(np.nanpercentile(bin_values, 75))

    return np.asarray(centers), np.asarray(q25), np.asarray(med), np.asarray(q75)


def plot_with_log_binned_summary(
    ax,
    freq_hz,
    values,
    label,
    y_log=False,
    color=None,
    linestyle="-",
    raw_alpha=0.22,
):
    x_mhz = np.asarray(freq_hz) / 1e6
    plotter = ax.loglog if y_log else ax.semilogx
    raw_line = plotter(
        x_mhz,
        values,
        color=color,
        linestyle=linestyle,
        linewidth=0.7,
        alpha=raw_alpha,
        label="_nolegend_",
    )[0]
    color = raw_line.get_color()

    centers, q25, med, q75 = log_bin_summary(freq_hz, values)
    if len(centers) == 0:
        return

    if y_log:
        positive = (q25 > 0) & (med > 0) & (q75 > 0)
        centers, q25, med, q75 = centers[positive], q25[positive], med[positive], q75[positive]
    if len(centers) == 0:
        return

    ax.fill_between(centers / 1e6, q25, q75, color=color, alpha=0.16, linewidth=0)
    plotter(
        centers / 1e6,
        med,
        color=color,
        linestyle=linestyle,
        linewidth=2.2,
        label=label,
    )


def plot_raw_curve(
    ax,
    freq_hz,
    values,
    label,
    y_log=False,
    color=None,
    linestyle="-",
    linewidth=0.9,
    alpha=0.95,
):
    freq_hz = np.asarray(freq_hz)
    values = np.asarray(values)
    mask = np.isfinite(freq_hz) & np.isfinite(values) & (freq_hz > 0)
    if y_log:
        mask &= values > 0

    plotter = ax.loglog if y_log else ax.semilogx
    return plotter(
        freq_hz[mask] / 1e6,
        values[mask],
        color=color,
        linestyle=linestyle,
        linewidth=linewidth,
        alpha=alpha,
        label=label,
    )


def plot_channel_results(
    results,
    attenuation_db=ATTENUATION_DB,
    focus_windows=FOCUS_WINDOWS,
    output_dir=OUTPUT_DIR,
    save_figures=SAVE_FIGURES,
    show_figures=SHOW_FIGURES,
):
    channel_name = results["channel"]
    channel_dir = Path(output_dir) / channel_name

    plot_raw_overlay(results, channel_dir, save_figures, show_figures)
    plot_measured_vs_counterfactual(results, attenuation_db, channel_dir, save_figures, show_figures)
    plot_excess_ratio(results, channel_dir, save_figures, show_figures)
    plot_actual_attenuation(results, attenuation_db, channel_dir, save_figures, show_figures)
    plot_integrated_noise(results, channel_dir, save_figures, show_figures)


def plot_raw_overlay(results, channel_dir, save_figures, show_figures):
    f_mhz = results["frequency_Hz"] / 1e6

    fig, ax = plt.subplots(figsize=(10, 6))
    for name, values in results["psd"].items():
        ax.loglog(f_mhz, values, label=CONDITION_LABELS[name], **CONDITION_STYLES[name])
    ax.set_xlabel("Frequency (MHz)")
    ax.set_ylabel("PSD")
    ax.set_title(f"{results['channel']}: raw PSD comparison")
    ax.grid(True, which="both", alpha=0.3)
    ax.legend()
    fig.tight_layout()
    save_or_show(fig, channel_dir / f"{results['channel']}_01_raw_overlay_loglog.png", save_figures, show_figures)


def plot_light_dependent_extraction(results, attenuation_db, channel_dir, save_figures, show_figures):
    f_mhz = results["frequency_Hz"] / 1e6

    fig, ax = plt.subplots(figsize=(10, 6))
    ax.loglog(f_mhz, results["light_filter"], label="A - C: light PSD without attenuator")
    ax.loglog(f_mhz, results["light_att"], label="B - D: light PSD with attenuator")
    ax.loglog(
        f_mhz,
        results["ideal_light_att"],
        "--",
        label=f"(A - C) / {10 ** (attenuation_db / 10):.0f}: ideal {attenuation_db} dB",
    )
    ax.set_xlabel("Frequency (MHz)")
    ax.set_ylabel("Light-dependent PSD")
    ax.set_title(f"{results['channel']}: light-dependent PSD extraction")
    ax.grid(True, which="both", alpha=0.3)
    ax.legend()
    fig.tight_layout()
    save_or_show(
        fig,
        channel_dir / f"{results['channel']}_02_light_dependent_extraction.png",
        save_figures,
        show_figures,
    )


def plot_measured_vs_counterfactual(results, attenuation_db, channel_dir, save_figures, show_figures):
    freq = results["frequency_Hz"]

    fig, ax = plt.subplots(figsize=(10, 6))
    plot_with_log_binned_summary(
        ax,
        freq,
        results["psd"]["B"],
        "B measured: Filter + ATT, light",
        y_log=True,
    )
    plot_with_log_binned_summary(
        ax,
        freq,
        results["b_ideal"],
        f"B ideal = D + max(A-C, 0) / {10 ** (attenuation_db / 10):.0f}",
        y_log=True,
        linestyle="--",
    )
    plot_with_log_binned_summary(
        ax,
        freq,
        results["psd"]["D"],
        "D: Filter + ATT, dark",
        y_log=True,
        linestyle=":",
    )
    ax.set_xlabel("Frequency (MHz)")
    ax.set_ylabel("PSD")
    ax.set_title(f"{results['channel']}: measured Filter + ATT vs ideal prediction")
    ax.grid(True, which="both", alpha=0.3)
    ax.legend()
    fig.tight_layout()
    save_or_show(
        fig,
        channel_dir / f"{results['channel']}_02_measured_vs_counterfactual.png",
        save_figures,
        show_figures,
    )


def plot_excess_ratio(results, channel_dir, save_figures, show_figures):
    freq = results["frequency_Hz"]

    fig, ax = plt.subplots(figsize=(10, 5))
    plot_with_log_binned_summary(
        ax,
        freq,
        results["excess_above_ideal_dB"],
        "B measured / B ideal",
    )
    ax.axhline(0, linestyle="--", linewidth=1, color="black")
    ax.set_xlabel("Frequency (MHz)")
    ax.set_ylabel("10 log10(B measured / B ideal) (dB)")
    ax.set_title(f"{results['channel']}: excess above ideal expectation")
    ax.grid(True, which="both", alpha=0.3)
    ax.legend()
    fig.tight_layout()
    save_or_show(fig, channel_dir / f"{results['channel']}_03_excess_above_ideal.png", save_figures, show_figures)


def plot_actual_attenuation(results, attenuation_db, channel_dir, save_figures, show_figures):
    freq = results["frequency_Hz"]

    fig, ax = plt.subplots(figsize=(10, 5))
    plot_with_log_binned_summary(
        ax,
        freq,
        results["actual_light_attenuation_dB"],
        "10 log10((B-D)/(A-C))",
    )
    ax.axhline(-attenuation_db, linestyle="--", linewidth=1, color="black", label=f"Ideal -{attenuation_db} dB")
    ax.set_xlabel("Frequency (MHz)")
    ax.set_ylabel("Actual light-dependent attenuation (dB)")
    ax.set_title(f"{results['channel']}: attenuation after dark subtraction")
    ax.grid(True, which="both", alpha=0.3)
    ax.legend()
    fig.tight_layout()
    save_or_show(fig, channel_dir / f"{results['channel']}_04_actual_attenuation.png", save_figures, show_figures)


def plot_focus_windows(results, focus_windows, channel_dir, save_figures, show_figures):
    freq = results["frequency_Hz"]
    f_mhz = freq / 1e6

    for window_name, (f_low, f_high) in focus_windows.items():
        mask = (freq >= f_low) & (freq <= f_high)
        if np.count_nonzero(mask) < 2:
            continue

        fig, ax = plt.subplots(figsize=(9, 5))
        ax.semilogx(f_mhz[mask], results["psd_dB"]["B"][mask], label="B measured")
        ax.semilogx(f_mhz[mask], results["b_ideal_dB"][mask], "--", label="B ideal")
        ax.semilogx(f_mhz[mask], results["psd_dB"]["D"][mask], ":", label="D dark floor")
        ax.set_xlabel("Frequency (MHz)")
        ax.set_ylabel("PSD (dB)")
        ax.set_title(f"{results['channel']}: measured vs ideal, {window_name}")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend()
        fig.tight_layout()
        save_or_show(
            fig,
            channel_dir / f"{results['channel']}_06_zoom_{safe_name(window_name)}.png",
            save_figures,
            show_figures,
        )


def plot_integrated_noise(results, channel_dir, save_figures, show_figures):
    integrated_df = results["integrated_noise_table"]
    plot_cols = [
        "filter_only_light_dependent_RMS_like",
        "ideal_after_20dB_light_dependent_RMS_like",
        "measured_att_light_dependent_RMS_like",
    ]

    fig, ax = plt.subplots(figsize=(11, 6))
    integrated_df.set_index("band")[plot_cols].plot(kind="bar", ax=ax)
    ax.set_yscale("log")
    ax.set_ylabel("Integrated noise, RMS-like")
    ax.set_title(f"{results['channel']}: band-integrated counterfactual summary")
    ax.grid(True, axis="y", alpha=0.3)
    ax.tick_params(axis="x", rotation=30)
    fig.tight_layout()
    save_or_show(
        fig,
        channel_dir / f"{results['channel']}_05_integrated_noise.png",
        save_figures,
        show_figures,
    )

    if save_figures:
        channel_dir.mkdir(parents=True, exist_ok=True)
        integrated_df.to_csv(channel_dir / f"{results['channel']}_integrated_noise_table.csv", index=False)


def common_bin_width_label(all_results):
    widths = [
        results.get("bin_width_Hz", np.nan)
        for results in all_results.values()
    ]
    widths = np.asarray(widths, dtype=float)
    widths = widths[np.isfinite(widths)]
    if widths.size == 0:
        return "unknown"
    return format_bin_width_hz(float(np.nanmedian(widths)))


def channel_axes(nrows=2, figsize=(12, 8), sharex=True):
    fig, axes = plt.subplots(nrows, 1, figsize=figsize, sharex=sharex)
    return fig, np.atleast_1d(axes)


def set_common_log_ylim(axes, value_sets, padding_fraction=0.06):
    positive_values = []
    for values in value_sets:
        values = np.asarray(values)
        mask = np.isfinite(values) & (values > 0)
        if np.any(mask):
            positive_values.append(values[mask])

    if not positive_values:
        return

    positive_values = np.concatenate(positive_values)
    log_min = np.log10(np.nanmin(positive_values))
    log_max = np.log10(np.nanmax(positive_values))
    if not np.isfinite(log_min) or not np.isfinite(log_max):
        return

    if log_max <= log_min:
        log_min -= 0.5
        log_max += 0.5
    else:
        padding = (log_max - log_min) * padding_fraction
        log_min -= padding
        log_max += padding

    for ax in np.ravel(axes):
        ax.set_ylim(10 ** log_min, 10 ** log_max)


def set_common_linear_ylim(axes, value_sets, padding_fraction=0.08, include_zero=True):
    finite_values = []
    for values in value_sets:
        values = np.asarray(values)
        mask = np.isfinite(values)
        if np.any(mask):
            finite_values.append(values[mask])

    if not finite_values:
        return

    finite_values = np.concatenate(finite_values)
    y_min = float(np.nanmin(finite_values))
    y_max = float(np.nanmax(finite_values))
    if include_zero:
        y_min = min(y_min, 0.0)
        y_max = max(y_max, 0.0)

    if not np.isfinite(y_min) or not np.isfinite(y_max):
        return

    if y_max <= y_min:
        y_min -= 1.0
        y_max += 1.0
    else:
        padding = (y_max - y_min) * padding_fraction
        y_min -= padding
        y_max += padding

    for ax in np.ravel(axes):
        ax.set_ylim(y_min, y_max)


def plot_combined_results(
    all_results,
    attenuation_db=ATTENUATION_DB,
    output_dir=OUTPUT_DIR,
    save_figures=SAVE_FIGURES,
    show_figures=SHOW_FIGURES,
):
    output_dir = Path(output_dir)
    plot_combined_dark_condition_proof(all_results, attenuation_db, output_dir, save_figures, show_figures)
    plot_combined_light_condition_proof(all_results, attenuation_db, output_dir, save_figures, show_figures)
    plot_combined_actual_vs_ideal_prediction(all_results, output_dir, save_figures, show_figures)
    plot_combined_measured_ideal_difference(all_results, output_dir, save_figures, show_figures)


def plot_combined_dark_condition_proof(all_results, attenuation_db, output_dir, save_figures, show_figures):
    fig, axes = channel_axes(figsize=(12, 8.5))
    bin_width_label = common_bin_width_label(all_results)
    y_value_sets = []

    for ax, (channel_name, results) in zip(axes, all_results.items()):
        freq = results["frequency_Hz"]
        y_value_sets.extend([results["psd"]["C"], results["psd"]["D"], results["psd"]["E"]])
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["C"],
            "C: Filter only, dark",
            y_log=True,
            color="tab:blue",
            linewidth=0.85,
        )
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["D"],
            "D: Filter + ATT, dark",
            y_log=True,
            color="tab:red",
            linewidth=0.95,
        )
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["E"],
            "E: Digitizer + 50 ohm terminator",
            y_log=True,
            color="0.35",
            linewidth=0.75,
            alpha=0.9,
        )
        ax.set_ylabel("PSD")
        ax.set_title(f"{channel_name}: dark condition raw comparison")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=8)

    set_common_log_ylim(axes, y_value_sets)
    axes[-1].set_xlabel("Frequency (MHz)")
    fig.suptitle(
        f"Dark condition raw spectra "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(
        fig,
        Path(output_dir) / "01_dark_condition_raw_comparison.png",
        save_figures,
        show_figures,
    )


def plot_combined_light_condition_proof(all_results, attenuation_db, output_dir, save_figures, show_figures):
    fig, axes = channel_axes(figsize=(12, 8.5))
    bin_width_label = common_bin_width_label(all_results)
    y_value_sets = []

    for ax, (channel_name, results) in zip(axes, all_results.items()):
        freq = results["frequency_Hz"]
        y_value_sets.extend([results["psd"]["A"], results["psd"]["B"], results["psd"]["E"]])
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["A"],
            "A: Filter only, light",
            y_log=True,
            color="tab:blue",
            linewidth=0.85,
        )
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["B"],
            "B: Filter + ATT, light",
            y_log=True,
            color="tab:red",
            linewidth=0.95,
        )
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["E"],
            "E: Digitizer + 50 ohm terminator",
            y_log=True,
            color="0.35",
            linewidth=0.75,
            alpha=0.9,
        )
        ax.set_ylabel("PSD")
        ax.set_title(f"{channel_name}: light condition raw comparison")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=8)

    set_common_log_ylim(axes, y_value_sets)
    axes[-1].set_xlabel("Frequency (MHz)")
    fig.suptitle(
        f"Light condition raw spectra "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(
        fig,
        Path(output_dir) / "02_light_condition_raw_comparison.png",
        save_figures,
        show_figures,
    )


def plot_combined_actual_vs_ideal_prediction(all_results, output_dir, save_figures, show_figures):
    fig, axes = plt.subplots(len(all_results), 2, figsize=(14, 8.5), sharex=True, squeeze=False)
    bin_width_label = common_bin_width_label(all_results)
    y_value_sets = []

    for row, (channel_name, results) in enumerate(all_results.items()):
        freq = results["frequency_Hz"]
        dark_ax = axes[row, 0]
        light_ax = axes[row, 1]
        y_value_sets.extend(
            [
                results["psd"]["D"],
                results["ideal_dark"],
                results["psd"]["B"],
                results["ideal_light"],
            ]
        )

        plot_raw_curve(
            dark_ax,
            freq,
            results["psd"]["D"],
            "Measured D: Filter + ATT, dark",
            y_log=True,
            color="tab:red",
            linewidth=0.9,
        )
        plot_raw_curve(
            dark_ax,
            freq,
            results["ideal_dark"],
            "Ideal I_dark = E + 0.01 max(C-E, 0)",
            y_log=True,
            color="tab:green",
            linewidth=0.9,
        )
        dark_ax.set_ylabel("PSD")
        dark_ax.set_title(f"{channel_name}: dark actual vs ideal")

        plot_raw_curve(
            light_ax,
            freq,
            results["psd"]["B"],
            "Measured B: Filter + ATT, light",
            y_log=True,
            color="tab:red",
            linewidth=0.9,
        )
        plot_raw_curve(
            light_ax,
            freq,
            results["ideal_light"],
            "Ideal I_light = E + 0.01 max(A-E, 0)",
            y_log=True,
            color="tab:green",
            linewidth=0.9,
        )
        light_ax.set_title(f"{channel_name}: light actual vs ideal")

        for ax in (dark_ax, light_ax):
            ax.grid(True, which="both", alpha=0.3)
            ax.legend(fontsize=8)

    set_common_log_ylim(axes, y_value_sets)
    for ax in axes[-1, :]:
        ax.set_xlabel("Frequency (MHz)")

    fig.suptitle(
        f"Measured attenuator spectra vs ideal no-added-noise predictions "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(
        fig,
        Path(output_dir) / "03_actual_vs_ideal_attenuator_predictions.png",
        save_figures,
        show_figures,
    )


def plot_combined_measured_ideal_difference(all_results, output_dir, save_figures, show_figures):
    fig, axes = plt.subplots(len(all_results), 2, figsize=(14, 8.2), sharex=True, squeeze=False)
    bin_width_label = common_bin_width_label(all_results)
    y_value_sets = []

    for row, (channel_name, results) in enumerate(all_results.items()):
        freq = results["frequency_Hz"]
        dark_diff_db = results["dark_excess_above_ideal_dB"]
        light_diff_db = results["light_excess_above_ideal_dB"]
        y_value_sets.extend([dark_diff_db, light_diff_db])

        dark_ax = axes[row, 0]
        light_ax = axes[row, 1]

        plot_raw_curve(
            dark_ax,
            freq,
            dark_diff_db,
            "10log10(D / I_dark)",
            color="tab:red",
            linewidth=0.85,
        )
        dark_ax.axhline(0, linestyle="-", linewidth=0.8, color="black", label="ideal expectation")
        dark_ax.set_ylabel("Measured - ideal (dB)")
        dark_ax.set_title(f"{channel_name}: dark measured minus ideal")

        plot_raw_curve(
            light_ax,
            freq,
            light_diff_db,
            "10log10(B / I_light)",
            color="tab:red",
            linewidth=0.85,
        )
        light_ax.axhline(0, linestyle="-", linewidth=0.8, color="black", label="ideal expectation")
        light_ax.set_title(f"{channel_name}: light measured minus ideal")

        for ax in (dark_ax, light_ax):
            ax.grid(True, which="both", alpha=0.3)
            ax.legend(fontsize=8)

    set_common_linear_ylim(axes, y_value_sets)
    for ax in axes[-1, :]:
        ax.set_xlabel("Frequency (MHz)")

    fig.suptitle(
        f"Difference between measured and ideal attenuator spectra "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(
        fig,
        Path(output_dir) / "04_measured_minus_ideal_attenuator_predictions_dB.png",
        save_figures,
        show_figures,
    )


def plot_combined_dark_and_light_excess(all_results, output_dir, save_figures, show_figures):
    fig, axes = plt.subplots(2, 1, figsize=(12, 7.8), sharex=True)
    bin_width_label = common_bin_width_label(all_results)
    colors = ["tab:blue", "tab:orange", "tab:purple", "tab:brown"]

    for index, (channel_name, results) in enumerate(all_results.items()):
        color = colors[index % len(colors)]
        plot_raw_curve(
            axes[0],
            results["frequency_Hz"],
            results["dark_excess_above_ideal_dB"],
            f"{channel_name}: 10log10(D / I_dark)",
            color=color,
            linewidth=0.85,
        )
        plot_raw_curve(
            axes[1],
            results["frequency_Hz"],
            results["light_excess_above_ideal_dB"],
            f"{channel_name}: 10log10(B / I_light)",
            color=color,
            linewidth=0.85,
        )

    axes[0].axhline(0, linestyle="-", linewidth=0.8, color="black", label="ideal expectation")
    axes[1].axhline(0, linestyle="-", linewidth=0.8, color="black", label="ideal expectation")
    axes[0].set_ylabel("Dark excess (dB)")
    axes[1].set_ylabel("Light excess (dB)")
    axes[1].set_xlabel("Frequency (MHz)")
    axes[0].set_title("Dark condition: measured D above ideal I_dark")
    axes[1].set_title("Light condition: measured B above ideal I_light")

    for ax in axes:
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=8)

    fig.suptitle(
        f"Excess above ideal attenuator prediction "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(
        fig,
        Path(output_dir) / "03_excess_above_ideal_dark_and_light_dB.png",
        save_figures,
        show_figures,
    )


def plot_combined_filter_only_vs_att(all_results, output_dir, save_figures, show_figures):
    fig, axes = channel_axes(figsize=(12, 8.5))
    bin_width_label = common_bin_width_label(all_results)
    for ax, (channel_name, results) in zip(axes, all_results.items()):
        freq = results["frequency_Hz"]
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["A"],
            "Filter only, light",
            y_log=True,
            color="tab:blue",
            linewidth=0.85,
        )
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["B"],
            "Filter + ATT, light",
            y_log=True,
            color="tab:red",
            linewidth=0.85,
        )
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["D"],
            "Filter + ATT, dark/no light",
            y_log=True,
            color="0.35",
            linestyle=":",
            linewidth=0.85,
            alpha=0.9,
        )
        ax.set_ylabel("PSD")
        ax.set_title(f"{channel_name}: filter only vs filter + attenuator")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=9)
    axes[-1].set_xlabel("Frequency (MHz)")
    fig.suptitle(
        f"Spectrum change caused by inserting the attenuator "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(
        fig,
        Path(output_dir) / "01_raw_filter_only_vs_filter_plus_attenuator.png",
        save_figures,
        show_figures,
    )


def plot_combined_raw_overlay(all_results, output_dir, save_figures, show_figures):
    fig, axes = channel_axes(figsize=(12, 8.5))
    for ax, (channel_name, results) in zip(axes, all_results.items()):
        f_mhz = results["frequency_Hz"] / 1e6
        for name, values in results["psd"].items():
            ax.loglog(f_mhz, values, label=CONDITION_LABELS[name], **CONDITION_STYLES[name])
        ax.set_ylabel("PSD")
        ax.set_title(f"{channel_name}: raw PSD comparison")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=8)
    axes[-1].set_xlabel("Frequency (MHz)")
    fig.suptitle("Raw PSD overlay")
    fig.tight_layout()
    save_or_show(fig, Path(output_dir) / "01_raw_overlay_loglog.png", save_figures, show_figures)


def plot_combined_measured_vs_counterfactual(all_results, attenuation_db, output_dir, save_figures, show_figures):
    fig, axes = channel_axes(figsize=(12, 8.5))
    bin_width_label = common_bin_width_label(all_results)
    for ax, (channel_name, results) in zip(axes, all_results.items()):
        freq = results["frequency_Hz"]
        plot_raw_curve(
            ax,
            freq,
            results["psd"]["B"],
            "B measured: Filter + ATT, light",
            y_log=True,
            color="tab:red",
            linewidth=0.85,
        )
        plot_raw_curve(
            ax,
            freq,
            results["b_ideal"],
            f"B ideal = D + max(A-C, 0) / {10 ** (attenuation_db / 10):.0f}",
            y_log=True,
            color="tab:green",
            linewidth=0.85,
        )
        ax.set_ylabel("PSD")
        ax.set_title(f"{channel_name}: actual attenuator response vs ideal")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=9)
    axes[-1].set_xlabel("Frequency (MHz)")
    fig.suptitle(
        f"Measured Filter + ATT vs floor-limited ideal prediction "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(fig, Path(output_dir) / "02_actual_vs_floor_limited_ideal.png", save_figures, show_figures)


def plot_combined_excess_ratio(all_results, output_dir, save_figures, show_figures):
    fig, axes = channel_axes(figsize=(12, 7.5))
    bin_width_label = common_bin_width_label(all_results)
    for ax, (channel_name, results) in zip(axes, all_results.items()):
        plot_raw_curve(
            ax,
            results["frequency_Hz"],
            results["excess_above_ideal_dB"],
            "B measured - B ideal",
            color="tab:red",
            linewidth=0.85,
        )
        ax.axhline(0, linestyle="-", linewidth=0.8, color="black", label="ideal expectation")
        ax.set_ylabel("Measured - ideal (dB)")
        ax.set_title(f"{channel_name}: measured spectrum minus expected spectrum")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=8)
    axes[-1].set_xlabel("Frequency (MHz)")
    fig.suptitle(
        f"Difference between measured and expected attenuator spectrum "
        f"(native software FFT bin width {bin_width_label})"
    )
    fig.tight_layout()
    save_or_show(fig, Path(output_dir) / "03_measured_minus_floor_limited_ideal_dB.png", save_figures, show_figures)


def plot_combined_actual_attenuation(all_results, attenuation_db, output_dir, save_figures, show_figures):
    fig, axes = channel_axes(figsize=(12, 7.5))
    for ax, (channel_name, results) in zip(axes, all_results.items()):
        plot_raw_curve(
            ax,
            results["frequency_Hz"],
            results["actual_light_attenuation_dB"],
            "10 log10((B-D)/(A-C))",
            color="tab:blue",
            linewidth=0.85,
        )
        ax.axhline(-attenuation_db, linestyle="--", linewidth=1, color="black", label=f"Ideal -{attenuation_db} dB")
        ax.set_ylabel("Attenuation (dB)")
        ax.set_title(f"{channel_name}: dark-subtracted actual attenuation")
        ax.grid(True, which="both", alpha=0.3)
        ax.legend(fontsize=8)
    axes[-1].set_xlabel("Frequency (MHz)")
    fig.suptitle("Actual attenuation after dark subtraction")
    fig.tight_layout()
    save_or_show(fig, Path(output_dir) / "03_dark_subtracted_actual_attenuation.png", save_figures, show_figures)


def plot_combined_proof_summary(all_results, attenuation_db, output_dir, save_figures, show_figures):
    fig, axes = plt.subplots(len(all_results), 2, figsize=(14, 8), sharex=True)
    axes = np.atleast_2d(axes)

    for row, (channel_name, results) in enumerate(all_results.items()):
        freq = results["frequency_Hz"]

        excess_ax = axes[row, 0]
        plot_with_log_binned_summary(
            excess_ax,
            freq,
            results["excess_above_ideal_dB"],
            "B measured / B ideal",
            raw_alpha=0.12,
        )
        excess_ax.axhline(0, linestyle="--", linewidth=1, color="black", label="ideal")
        excess_ax.set_ylabel("Excess (dB)")
        excess_ax.set_title(f"{channel_name}: measured above ideal")
        excess_ax.grid(True, which="both", alpha=0.3)
        excess_ax.legend(fontsize=8)

        attenuation_ax = axes[row, 1]
        plot_with_log_binned_summary(
            attenuation_ax,
            freq,
            results["actual_light_attenuation_dB"],
            "actual attenuation",
            raw_alpha=0.12,
        )
        attenuation_ax.axhline(
            -attenuation_db,
            linestyle="--",
            linewidth=1,
            color="black",
            label=f"ideal -{attenuation_db} dB",
        )
        attenuation_ax.set_ylabel("Attenuation (dB)")
        attenuation_ax.set_title(f"{channel_name}: dark-subtracted attenuation")
        attenuation_ax.grid(True, which="both", alpha=0.3)
        attenuation_ax.legend(fontsize=8)

    for ax in axes[-1, :]:
        ax.set_xlabel("Frequency (MHz)")

    fig.suptitle("Quantitative proof of attenuator excess / non-ideal attenuation")
    fig.tight_layout()
    save_or_show(fig, Path(output_dir) / "02_quantitative_proof.png", save_figures, show_figures)


def plot_combined_integrated_noise(all_results, output_dir, save_figures, show_figures):
    plot_cols = [
        "filter_only_light_dependent_RMS_like",
        "ideal_after_20dB_light_dependent_RMS_like",
        "measured_att_light_dependent_RMS_like",
    ]

    fig, axes = plt.subplots(1, len(all_results), figsize=(15, 5.8), sharey=True)
    axes = np.atleast_1d(axes)
    for ax, (channel_name, results) in zip(axes, all_results.items()):
        results["integrated_noise_table"].set_index("band")[plot_cols].plot(kind="bar", ax=ax)
        ax.set_yscale("log")
        ax.set_title(channel_name)
        ax.set_xlabel("")
        ax.grid(True, axis="y", alpha=0.3)
        ax.tick_params(axis="x", rotation=30)
        ax.legend(fontsize=8)
    axes[0].set_ylabel("Integrated noise, RMS-like")
    fig.suptitle("Band-integrated counterfactual summary")
    fig.tight_layout()
    save_or_show(fig, Path(output_dir) / "03_integrated_noise_summary.png", save_figures, show_figures)


def run_full_analysis(
    condition_sources=CONDITION_SOURCES,
    output_dir=OUTPUT_DIR,
    save_figures=SAVE_FIGURES,
    show_figures=SHOW_FIGURES,
):
    frames = load_condition_frames(condition_sources)

    all_results = {}
    all_integrated_tables = []
    for channel_name, channel_col in CHANNEL_COLS.items():
        print(f"Running analysis for {channel_name}: {channel_col}")
        results = analyze_channel(frames, channel_name, channel_col)
        all_results[channel_name] = results
        all_integrated_tables.append(results["integrated_noise_table"])

    plot_combined_results(
        all_results,
        output_dir=output_dir,
        save_figures=save_figures,
        show_figures=show_figures,
    )

    combined_table = pd.concat(all_integrated_tables, ignore_index=True)
    if save_figures:
        Path(output_dir).mkdir(parents=True, exist_ok=True)
        combined_table.to_csv(Path(output_dir) / "combined_integrated_noise_table.csv", index=False)

    return all_results, combined_table


if __name__ == "__main__":
    run_full_analysis()
