"""Create an annotated diagonal tail-offset heatmap from a run folder.

The calculation matches ``cm_pipeline_all_in_one.py``: the mean 8x8
correlation matrix is calculated over all frames and every element has the
last element on its matrix diagonal subtracted.  Consequently, the last row
and last column are zero because those cells are the diagonal tails.

Usage
-----
    python diagonal_tail_offset_annotated.py "D:\\DataFiles\\20260804_142332"

If no command-line folder is supplied, the program uses ``RUN_FOLDER`` from
the user-settings section below.  Generated files are saved in that same run
folder by default.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import re
from datetime import datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import Normalize
import numpy as np


# =============================================================================
# USER SETTINGS
# =============================================================================

# Paste the run folder between the quotes before running this file.
# A folder supplied on the command line takes precedence over this variable.
RUN_FOLDER = r"D:\Quantum Squeezing Project\DataFiles\20260727_171009"

# Generate the physical-unit plot.  This requires a valid positive Conversion
# Factor in sensitivity.log; the program stops with a clear error if unavailable.
# Other supported choices are "auto", "v2", and "both".
DEFAULT_UNITS = "urad2"

# Match the Quantum Measurement Software post-processing heatmap.
DEFAULT_COLORMAP = "jet"
OUTPUT_DPI = 300


DETECTOR_AREA_SCALE = (0.24**2) / (32768.0**2)


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Plot the mean 8x8 diagonal tail-offset matrix with a value in "
            "every cell and the software's color scale."
        )
    )
    parser.add_argument(
        "run_folder",
        nargs="?",
        help="Run folder containing cm.bin (you will be prompted if omitted).",
    )
    parser.add_argument(
        "--output-folder",
        type=Path,
        help="Optional output folder (default: save directly in the run folder).",
    )
    parser.add_argument(
        "--units",
        choices=("auto", "v2", "urad2", "both"),
        default=DEFAULT_UNITS,
        help="Output units (default: %(default)s).",
    )
    parser.add_argument(
        "--cmap",
        default=DEFAULT_COLORMAP,
        help="Matplotlib color map (default: %(default)s).",
    )
    return parser.parse_args()


def resolve_run_folder(command_line_value: str | None) -> Path:
    if command_line_value:
        run_folder = Path(command_line_value.strip().strip('"'))
    elif str(RUN_FOLDER).strip():
        run_folder = Path(RUN_FOLDER)
    else:
        raise ValueError(
            "RUN_FOLDER is empty. Paste the run-folder path into RUN_FOLDER "
            "near the top of this file."
        )

    run_folder = run_folder.expanduser().resolve()
    if not run_folder.is_dir():
        raise FileNotFoundError(f"Run folder not found: {run_folder}")
    return run_folder


def mean_raw_matrix(cm_path: Path) -> tuple[np.ndarray, int]:
    """Return the all-frame mean matrix without loading all frames into RAM."""
    if not cm_path.is_file():
        raise FileNotFoundError(f"cm.bin not found: {cm_path}")

    bytes_per_frame = 64 * np.dtype("<f8").itemsize
    file_size = cm_path.stat().st_size
    if file_size == 0:
        raise ValueError(f"cm.bin is empty: {cm_path}")
    if file_size % bytes_per_frame:
        trailing = file_size % bytes_per_frame
        raise ValueError(
            f"cm.bin does not contain complete 8x8 float64 frames "
            f"({trailing} trailing bytes): {cm_path}"
        )

    frame_count = file_size // bytes_per_frame
    frames = np.memmap(cm_path, dtype="<f8", mode="r", shape=(frame_count, 8, 8))
    matrix_mean = np.asarray(frames.mean(axis=0), dtype=float)
    del frames
    return matrix_mean, int(frame_count)


def metadata_attenuator_correction(run_folder: Path) -> float:
    """Read the power-detector attenuator correction used by the pipeline."""
    path = run_folder / "metadata.json"
    if not path.is_file():
        return 1.0
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
        physics = payload.get("PhysicsData", {})
        applied = physics.get("PowerDetectorAttenuatorApplied", False)
        if isinstance(applied, str):
            applied = applied.strip().lower() in {"1", "true", "yes", "on"}
        factor = float(physics.get("PowerDetectorAttenuatorCorrectionFactor", 1.0))
        return factor if applied and np.isfinite(factor) and factor > 0 else 1.0
    except (AttributeError, TypeError, ValueError, json.JSONDecodeError):
        return 1.0


def conversion_factor_v2_per_rad2(run_folder: Path) -> float:
    """Read the last valid conversion factor recorded in sensitivity.log."""
    path = run_folder / "sensitivity.log"
    if not path.is_file():
        return math.nan
    text = path.read_text(encoding="utf-8", errors="ignore")
    matches = re.findall(
        r"Conversion Factor\s*=\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?)",
        text,
        flags=re.IGNORECASE,
    )
    for value in reversed(matches):
        try:
            factor = float(value)
        except ValueError:
            continue
        if np.isfinite(factor) and factor > 0:
            return factor
    return math.nan


def diagonal_tail_offset(matrix: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Subtract the last element of each diagonal from every diagonal cell."""
    if matrix.shape != (8, 8):
        raise ValueError(f"Expected an 8x8 matrix, received {matrix.shape}.")

    result = np.empty_like(matrix, dtype=float)
    tail_rows = np.empty((8, 8), dtype=int)
    tail_columns = np.empty((8, 8), dtype=int)

    for row in range(8):
        for column in range(8):
            diagonal = column - row
            if diagonal >= 0:
                tail_row, tail_column = 7 - diagonal, 7
            else:
                tail_row, tail_column = 7, 7 + diagonal
            result[row, column] = matrix[row, column] - matrix[tail_row, tail_column]
            tail_rows[row, column] = tail_row
            tail_columns[row, column] = tail_column

    return result, tail_rows, tail_columns


def number_format(values: np.ndarray, units_key: str) -> str:
    finite = np.abs(values[np.isfinite(values)])
    maximum = float(np.max(finite)) if finite.size else 0.0
    nonzero = finite[finite > 0]
    minimum_nonzero = float(np.min(nonzero)) if nonzero.size else 0.0

    if units_key == "v2" or (maximum and (maximum >= 10_000 or minimum_nonzero < 0.001)):
        return ".2e"
    if maximum >= 100:
        return ".1f"
    if maximum >= 10:
        return ".2f"
    return ".3f"


def color_normalization(values: np.ndarray) -> Normalize:
    finite = values[np.isfinite(values)]
    if not finite.size:
        raise ValueError("The tail-offset matrix has no finite values.")

    minimum = float(np.min(finite))
    maximum = float(np.max(finite))
    if minimum == maximum:
        padding = max(abs(minimum) * 0.01, 1.0)
        minimum -= padding
        maximum += padding
    return Normalize(vmin=minimum, vmax=maximum)


def provenance(run_folder: Path, frame_count: int, cm_path: Path) -> str:
    stat = cm_path.stat()
    modified = datetime.fromtimestamp(stat.st_mtime).isoformat(timespec="seconds")
    return (
        f"Run folder: {run_folder} | Raw: cm.bin | frames={frame_count:,} | "
        f"bytes={stat.st_size} | modified={modified}"
    )


def save_annotated_plot(
    values: np.ndarray,
    units_key: str,
    units_label: str,
    output_path: Path,
    cmap_name: str,
    source_note: str,
) -> None:
    norm = color_normalization(values)
    value_format = number_format(values, units_key)
    cmap = plt.get_cmap(cmap_name)

    fig, axis = plt.subplots(figsize=(9.5, 8.2))
    image = axis.imshow(values, cmap=cmap, norm=norm, origin="upper", interpolation="nearest")
    colorbar = fig.colorbar(image, ax=axis, fraction=0.046, pad=0.04)
    colorbar.set_label(f"Diagonal tail-offset ({units_label})")

    axis.set_title(f"Diagonal Tail-Offset ({units_label})", fontsize=17, pad=14)
    axis.set_xlabel("")
    axis.set_ylabel("")
    axis.set_xticks(np.arange(8), labels=np.arange(1, 9))
    axis.set_yticks(np.arange(8), labels=np.arange(1, 9))
    axis.set_xticks(np.arange(-0.5, 8, 1), minor=True)
    axis.set_yticks(np.arange(-0.5, 8, 1), minor=True)
    axis.grid(which="minor", color="white", linewidth=1.2, alpha=0.75)
    axis.tick_params(which="minor", bottom=False, left=False)

    for row in range(8):
        for column in range(8):
            value = float(values[row, column])
            red, green, blue, _alpha = cmap(norm(value))
            luminance = 0.2126 * red + 0.7152 * green + 0.0722 * blue
            text_color = "white" if luminance < 0.43 else "black"
            axis.text(
                column,
                row,
                format(value, value_format),
                ha="center",
                va="center",
                color=text_color,
                fontsize=8.5,
                fontweight="semibold",
            )

    fig.text(0.006, 0.006, source_note, ha="left", va="bottom", fontsize=6, color="#555555")
    fig.tight_layout(rect=(0, 0.035, 1, 1))
    fig.savefig(
        output_path,
        dpi=OUTPUT_DPI,
        bbox_inches="tight",
        metadata={"SourceRunFolder": source_note},
    )
    plt.close(fig)


def save_values_csv(
    output_path: Path,
    mean_v2: np.ndarray,
    offset_v2: np.ndarray,
    offset_urad2: np.ndarray | None,
    tail_rows: np.ndarray,
    tail_columns: np.ndarray,
) -> None:
    with output_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(
            [
                "matrix_row",
                "matrix_column",
                "tail_row",
                "tail_column",
                "mean_correlation_V2",
                "diagonal_tail_offset_V2",
                "diagonal_tail_offset_urad2",
            ]
        )
        for row in range(8):
            for column in range(8):
                urad_value = "" if offset_urad2 is None else f"{offset_urad2[row, column]:.12g}"
                writer.writerow(
                    [
                        row + 1,
                        column + 1,
                        int(tail_rows[row, column]) + 1,
                        int(tail_columns[row, column]) + 1,
                        f"{mean_v2[row, column]:.12g}",
                        f"{offset_v2[row, column]:.12g}",
                        urad_value,
                    ]
                )


def requested_units(units: str, conversion_factor: float) -> list[str]:
    valid_conversion = np.isfinite(conversion_factor) and conversion_factor > 0
    if units == "auto":
        return ["urad2"] if valid_conversion else ["v2"]
    if units == "urad2" and not valid_conversion:
        raise ValueError(
            "--units urad2 requires a positive Conversion Factor in sensitivity.log. "
            "Use --units v2 for this run."
        )
    if units == "both":
        return ["v2", "urad2"] if valid_conversion else ["v2"]
    return [units]


def run_analysis(
    run_folder: Path,
    output_folder: Path | None,
    units: str,
    cmap_name: str,
) -> list[Path]:
    cm_path = run_folder / "cm.bin"
    raw_mean, frame_count = mean_raw_matrix(cm_path)
    attenuator_factor = metadata_attenuator_correction(run_folder)
    mean_v2 = raw_mean * DETECTOR_AREA_SCALE * attenuator_factor
    offset_v2, tail_rows, tail_columns = diagonal_tail_offset(mean_v2)

    conversion_factor = conversion_factor_v2_per_rad2(run_folder)
    valid_conversion = np.isfinite(conversion_factor) and conversion_factor > 0
    offset_urad2 = offset_v2 / conversion_factor * 1e12 if valid_conversion else None

    if output_folder is None:
        output_folder = run_folder
    else:
        output_folder = output_folder.expanduser().resolve()
    output_folder.mkdir(parents=True, exist_ok=True)

    source_note = provenance(run_folder, frame_count, cm_path)
    prefix = run_folder.name
    outputs: list[Path] = []
    for unit_key in requested_units(units, conversion_factor):
        if unit_key == "urad2":
            assert offset_urad2 is not None
            values = offset_urad2
            label = r"$\mu rad^2$"
        else:
            values = offset_v2
            label = r"V$^2$"

        plot_path = output_folder / f"{prefix}_diagonal_tail_offset_annotated_{unit_key}.png"
        save_annotated_plot(values, unit_key, label, plot_path, cmap_name, source_note)
        outputs.append(plot_path)

    csv_path = output_folder / f"{prefix}_diagonal_tail_offset_values.csv"
    save_values_csv(
        csv_path,
        mean_v2,
        offset_v2,
        offset_urad2,
        tail_rows,
        tail_columns,
    )
    outputs.append(csv_path)

    print(f"Analyzed {frame_count:,} complete correlation-matrix frames.")
    print(f"Detector/attenuator scale: {DETECTOR_AREA_SCALE * attenuator_factor:.12g}")
    if valid_conversion:
        print(f"Conversion factor: {conversion_factor:.12g} V^2/rad^2")
    else:
        print("No valid conversion factor found; physical-unit output was not generated.")
    print(f"Input files were not modified: {run_folder}")
    print(f"Output folder: {output_folder}")
    for path in outputs:
        print(f"  {path.name}")
    return outputs


def main() -> None:
    args = parse_arguments()
    run_folder = resolve_run_folder(args.run_folder)
    run_analysis(run_folder, args.output_folder, args.units, args.cmap)


if __name__ == "__main__":
    main()
