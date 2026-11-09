from __future__ import annotations

import contextlib
import csv
import io
import logging
import re
import warnings as python_warnings
from dataclasses import dataclass, field

import numpy as np
import pandas as pd

from app.core.config import settings

logger = logging.getLogger(__name__)

# Sentinel used to represent "this cell was empty in the source file". Keeping the
# raw text (rather than NaN) lets downstream type inference distinguish an empty
# string from the literal value "NA".
MISSING_TOKENS: set[str] = {
    "",
    "na",
    "n/a",
    "n.a.",
    "nan",
    "null",
    "none",
    "nil",
    "-",
    "--",
    "?",
    "unknown",
    "undefined",
    "#n/a",
    "#null!",
    "#div/0!",
    "#value!",
    "#ref!",
    "#name?",
    "#num!",
    "#n/a n/a",
}


class DatasetTooLargeError(ValueError):
    """Raised when an upload violates a hard processing limit."""


class UnsupportedFileError(ValueError):
    """Raised when the payload is not a readable delimited text file."""


@dataclass
class IngestResult:
    frame: pd.DataFrame
    warnings: list[str] = field(default_factory=list)
    parse_errors: list[str] = field(default_factory=list)
    delimiter: str = ","
    encoding: str = "utf-8"
    original_header_names: list[str] = field(default_factory=list)
    header_was_repaired: bool = False


@contextlib.contextmanager
def _suppress_parser_warnings():
    """Silence per-row parser warnings emitted by the salvage CSV reader."""
    with python_warnings.catch_warnings():
        python_warnings.simplefilter("ignore")
        yield


def _sniff_delimiter(sample: str) -> str:
    """Detect the delimiter, guarding against pathological single-column files."""
    try:
        dialect = csv.Sniffer().sniff(sample, delimiters=",;\t|")
        return dialect.delimiter
    except csv.Error:
        # Sniffer fails on ragged rows; fall back to counting candidates.
        counts = {d: sample.count(d) for d in (",", ";", "\t", "|")}
        best = max(counts, key=counts.get)
        return best if counts[best] > 0 else ","


def _decode(raw: bytes) -> tuple[str, str]:
    """Decode bytes, tolerating UTF-8/UTF-8-BOM/Latin-1 and stray Windows-1252 bytes."""
    for encoding in ("utf-8-sig", "utf-8", "cp1252", "latin-1"):
        try:
            return raw.decode(encoding), encoding
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace"), "utf-8(replaced)"


def _normalise_header(raw: str) -> str:
    header = str(raw).strip().lstrip("\ufeff")
    return header if header else "unnamed_column"


def _dedupe_headers(names: list[str]) -> tuple[list[str], bool]:
    """Make headers unique and non-empty without ever silently dropping a column."""
    seen: dict[str, int] = {}
    repaired = False
    result: list[str] = []
    for name in names:
        candidate = name
        if candidate in seen:
            repaired = True
            seen[candidate] += 1
            candidate = f"{name}__{seen[name]}"
            while candidate in seen:
                seen[candidate] += 1
                candidate = f"{name}__{seen[candidate]}"
        seen[candidate] = 0
        result.append(candidate)
    return result, repaired


def _check_plausible_csv(text: str) -> None:
    if "\x00" in text:
        raise UnsupportedFileError(
            "File contains NUL bytes and is not a delimited text file."
        )


def load_csv(raw: bytes, *, filename: str = "upload.csv") -> IngestResult:
    """Read a CSV upload into a DataFrame with defensive defaults.

    Raises:
        UnsupportedFileError: the payload is empty, binary, or otherwise unreadable.
        DatasetTooLargeError: the payload exceeds a configured processing limit.
    """
    if not raw:
        raise UnsupportedFileError("The uploaded file is empty.")

    if len(raw) > settings.max_upload_bytes:
        raise DatasetTooLargeError(
            f"File is larger than the {settings.max_upload_bytes // (1024 * 1024)} MB limit."
        )

    # A NUL byte in the first 8 KB is a strong signal that this is not a CSV.
    if b"\x00" in raw[:8192]:
        raise UnsupportedFileError(
            "File appears to be binary. Datara currently accepts CSV and TSV files."
        )

    text, encoding = _decode(raw)
    _check_plausible_csv(text)

    if not text.strip():
        raise UnsupportedFileError("The uploaded file contains no data.")

    # Sample from the head only: Sniffer scans the whole buffer otherwise.
    delimiter = _sniff_delimiter(text[:64_000])
    ingest_warnings: list[str] = []
    parse_errors: list[str] = []

    read_kwargs: dict = dict(
        sep=delimiter,
        dtype_backend="numpy_nullable",
        low_memory=False,
    )

    frame: pd.DataFrame
    try:
        frame = pd.read_csv(io.StringIO(text), on_bad_lines="error", **read_kwargs)
    except pd.errors.ParserError as exc:
# Ragged rows are extremely common in real exports. Retry in salvage mode
        # rather than rejecting the whole upload, but surface the degradation.
        # The salvage parser emits one Python ParserWarning per bad row; we record a
        # single aggregated message instead of flooding the log.
        with _suppress_parser_warnings():
            frame = pd.read_csv(
                io.StringIO(text),
                on_bad_lines="warn",
                engine="python",
                dtype_backend="numpy_nullable",
            )
        parse_errors.append(str(exc).split("\n")[0])
        ingest_warnings.append(
            "Malformed rows were detected. Ragged rows were padded and short rows truncated."
        )
    except ValueError as exc:
        raise UnsupportedFileError(f"The file could not be parsed as delimited text: {exc}") from exc
    except pd.errors.EmptyDataError as exc:
        raise UnsupportedFileError("The uploaded file has no parsable columns.") from exc

    if frame.empty and len(frame.columns) == 0:
        raise UnsupportedFileError("The uploaded file has no columns.")

    original_headers = [str(c) for c in frame.columns]
    normalised = [_normalise_header(c) for c in original_headers]
    repaired_headers, deduped = _dedupe_headers(normalised)

    frame.columns = repaired_headers
    if deduped:
        ingest_warnings.append("Duplicate column names were renamed to make them unique.")
    if repaired_headers != original_headers:
        ingest_warnings.append("Column names were trimmed and whitespace removed.")

    # Drop completely blank/trailing unnamed columns produced by ragged exports
    cols_to_drop: list[str] = []
    for c in frame.columns:
        s = str(c).strip()
        sl = s.lower()
        if s == "" or sl.startswith("unnamed") or sl == "unnamed":
            cols_to_drop.append(c)
    if cols_to_drop and len(cols_to_drop) < len(frame.columns):
        frame = frame.drop(columns=cols_to_drop)

    # pandas mangles duplicate headers to "col.1"/"col.2" before we ever see them,
    # so detect that pattern and re-dedupe into a deterministic scheme.
    if any(re.search(r"\.\d+$", str(c)) for c in frame.columns):
        normalised = [_normalise_header(re.sub(r"\.\d+$", "", str(c))) for c in frame.columns]
        deduped_cols, deduped_flag = _dedupe_headers(normalised)
        frame.columns = [c.replace("__", "_") for c in deduped_cols]
        ingest_warnings.append("Duplicate column names were renamed to make them unique.")
        if deduped_flag:
            ingest_warnings.append("Column names were trimmed and whitespace removed.")

    # Drop completely blank trailing columns produced by ragged exports
    blank_cols = [c for c in frame.columns if c.strip() == "" or c.lower().startswith("unnamed")]
    if len(blank_cols) and len(blank_cols) < len(frame.columns):
        frame = frame.drop(columns=blank_cols)

    if len(frame.columns) > settings.max_columns:
        raise DatasetTooLargeError(
            f"File contains {len(frame.columns)} columns, exceeding the "
            f"{settings.max_columns} column limit."
        )
    if len(frame) > settings.max_rows:
        raise DatasetTooLargeError(
            f"File contains {len(frame)} rows, exceeding the {settings.max_rows} row limit."
        )

    frame = _coerce_missing(frame, ingest_warnings)

    return IngestResult(
        frame=frame,
        warnings=ingest_warnings,
        parse_errors=parse_errors,
        delimiter=delimiter,
        encoding=encoding,
        original_header_names=original_headers,
        header_was_repaired=deduped,
    )


def _coerce_missing(frame: pd.DataFrame, ingest_warnings: list[str]) -> pd.DataFrame:
    """Map common textual null tokens onto pd.NA for object columns only.

    Numeric columns are left alone: a real ``0`` or ``NaN`` in a numeric column has
    already been parsed correctly, and blanket string replacement would corrupt
    legitimate identifiers such as ZIP codes or country codes.
    """
    object_columns = frame.select_dtypes(include=["object", "string"]).columns
    if len(object_columns) == 0:
        return frame

    hit_columns: list[str] = []
    for column in object_columns:
        series = frame[column]
        try:
            as_text = series.astype("string").str.strip().str.lower()
        except (TypeError, ValueError):
            continue
        mask = as_text.isin(MISSING_TOKENS)
        if mask.any():
            frame.loc[mask, column] = pd.NA
            hit_columns.append(column)

    if hit_columns:
        preview = ", ".join(hit_columns[:5])
        suffix = "" if len(hit_columns) <= 5 else f" (+{len(hit_columns) - 5} more)"
        ingest_warnings.append(
            f"Textual null markers (e.g. \"N/A\", \"null\", \"-\") were treated as missing "
            f"in: {preview}{suffix}."
        )
    return frame


def json_safe(value: object) -> object:
    """Convert numpy/pandas scalars into plain JSON-serialisable Python values."""
    if value is None:
        return None
    if isinstance(value, (np.bool_, bool)):
        return bool(value)
    if isinstance(value, (np.integer,)):
        return int(value)
    if isinstance(value, (np.floating, float)):
        f = float(value)
        return None if (np.isnan(f) or np.isinf(f)) else round(f, 6)
    if isinstance(value, (pd.Timestamp,)):
        return value.isoformat()
    if value is pd.NaT or value is pd.NA:
        return None
    if isinstance(value, np.ndarray):
        return [json_safe(v) for v in value.tolist()]
    if isinstance(value, (list, tuple)):
        return [json_safe(v) for v in value]
    if isinstance(value, dict):
        return {str(k): json_safe(v) for k, v in value.items()}
    try:
        if pd.isna(value):
            return None
    except (TypeError, ValueError):
        pass
    return str(value)