from __future__ import annotations

import logging

import numpy as np
import pandas as pd

from app.core.config import settings
from app.schemas.profile import (
    Anomaly,
    CategoryFrequency,
    ColumnKind,
    ColumnProfile,
    ConfidenceBand,
    NumericHistogram,
    NumericSummary,
    OutlierSummary,
    SampleRows,
)
from app.services.ingestion import json_safe
from app.services.type_inference import infer_column_kind

logger = logging.getLogger(__name__)

MAX_TOP_CATEGORIES = 12
MAX_CATEGORIES_SCANNED = 50_000


def _memory_bytes(series: pd.Series) -> int:
    try:
        return int(series.memory_usage(deep=True, index=False))
    except (TypeError, ValueError):
        return 0


def _leading_whitespace_count(series: pd.Series) -> int:
    if series.dtype != object and not pd.api.types.is_string_dtype(series):
        return 0
    sample = series.dropna().head(10_000)
    if sample.empty:
        return 0
    try:
        as_text = sample.astype("string")
    except (TypeError, ValueError):
        return 0
    return int(as_text.str.match(r"^\s").sum())


def _sample_values(series: pd.Series, kind: ColumnKind, limit: int = 5) -> list[str]:
    values = series.dropna()
    if values.empty:
        return []
    if kind in (ColumnKind.NUMERIC, ColumnKind.INTEGER):
        values = values.astype("float64")
        picks = values.quantile([0.0, 0.25, 0.5, 0.75, 1.0]).dropna().unique()
        picks = picks[:limit]
    elif kind == ColumnKind.DATETIME:
        picks = pd.to_datetime(values, errors="coerce").dropna().sort_values().unique()
        picks = np.array([picks[0], picks[-1]]) if len(picks) > 1 else np.array(picks)
    else:
        picks = values.astype("string").drop_duplicates().head(limit).to_numpy()
    return [str(json_safe(v)) for v in picks]


def _numeric_summary(series: pd.Series) -> NumericSummary:
    values = pd.to_numeric(series, errors="coerce").dropna().astype("float64")
    if values.empty:
        return NumericSummary()

    summary = NumericSummary(
        mean=float(values.mean()),
        median=float(values.median()),
        std=float(values.std(ddof=1)) if len(values) > 1 else 0.0,
        min=float(values.min()),
        max=float(values.max()),
        q1=float(values.quantile(0.25)),
        q3=float(values.quantile(0.75)),
        p05=float(values.quantile(0.05)),
        p25=float(values.quantile(0.25)),
        p75=float(values.quantile(0.75)),
        p95=float(values.quantile(0.95)),
        zeros_count=int((values == 0).sum()),
        negative_count=int((values < 0).sum()),
        sum=float(values.sum()),
        variance=float(values.var(ddof=1)) if len(values) > 1 else 0.0,
    )
    summary.range = summary.max - summary.min
    if summary.std is not None and summary.mean:
        summary.coefficient_of_variation = abs(summary.std / summary.mean)

    # Skew/kurtosis need at least a few observations to be meaningful.
    if len(values) >= 3:
        summary.skewness = float(values.skew())
    if len(values) >= 4:
        summary.kurtosis = float(values.kurtosis())
    return summary


def _categorical_summary(series: pd.Series) -> tuple[list[CategoryFrequency], float]:
    total = int(series.notna().sum())
    if total == 0:
        return [], 0.0

    counts = series.value_counts(dropna=True)
    if len(counts) > MAX_CATEGORIES_SCANNED:
        head = counts.head(MAX_CATEGORIES_SCANNED)
        covered = int(head.sum())
        frequencies = [
            CategoryFrequency(
                value=str(json_safe(idx)),
                count=int(cnt),
                percentage=round(100.0 * cnt / total, 4),
            )
            for idx, cnt in head.items()
        ]
        return frequencies[:MAX_TOP_CATEGORIES], round(100.0 * covered / total, 4)

    frequencies = [
        CategoryFrequency(
            value=str(json_safe(idx)),
            count=int(cnt),
            percentage=round(100.0 * cnt / total, 4),
        )
        for idx, cnt in counts.head(MAX_TOP_CATEGORIES).items()
    ]
    return frequencies, round(100.0 * int(counts.sum()) / total, 4)


def _histogram(series: pd.Series, bins: int = 20) -> NumericHistogram | None:
    values = pd.to_numeric(series, errors="coerce").dropna().astype("float64")
    if len(values) < 2 or values.nunique() < 2:
        return None
    # Guard against pathological bin counts on huge, high-cardinality columns.
    try:
        counts, edges = np.histogram(values.to_numpy(), bins=bins)
    except (ValueError, MemoryError):
        return None
    return NumericHistogram(
        bin_edges=[round(float(e), 6) for e in edges],
        counts=[int(c) for c in counts],
    )


def _outliers(series: pd.Series, method: str) -> list[OutlierSummary]:
    values = pd.to_numeric(series, errors="coerce").dropna().astype("float64")
    n = len(values)
    if n < 8 or values.nunique() < 3:
        return []

    results: list[OutlierSummary] = []

    if method == "iqr":
        q1 = float(values.quantile(0.25))
        q3 = float(values.quantile(0.75))
        iqr = q3 - q1
        if iqr <= 0:
            return results
        low = q1 - settings.iqr_multiplier * iqr
        high = q3 + settings.iqr_multiplier * iqr
        mask = (values < low) | (values > high)
        count = int(mask.sum())
        if count:
            results.append(
                OutlierSummary(
                    method="IQR",
                    lower_bound=round(low, 6),
                    upper_bound=round(high, 6),
                    count=count,
                    percentage=round(100.0 * count / n, 4),
                    confidence=ConfidenceBand.HIGH,
                )
            )

    if method == "zscore":
        mean = float(values.mean())
        std = float(values.std(ddof=1))
        if std <= 0 or np.isinf(std):
            return results
        z = (values - mean).abs()
        count = int((z > settings.zscore_threshold).sum())
        if count:
            results.append(
                OutlierSummary(
                    method="Z-Score",
                    lower_bound=round(mean - settings.zscore_threshold * std, 6),
                    upper_bound=round(mean + settings.zscore_threshold * std, 6),
                    count=count,
                    percentage=round(100.0 * count / n, 4),
                    confidence=ConfidenceBand.MEDIUM,
                )
            )

    if method == "zscore_clip":
        # Trimming at +-3 sigma is a common (if blunt) cleaning heuristic.
        mean = float(values.mean())
        std = float(values.std(ddof=1))
        if std <= 0 or np.isinf(std):
            return results
        count = int(((values - mean).abs() > 3 * std).sum())
        if count:
            results.append(
                OutlierSummary(
                    method="Z-Score (3σ)",
                    lower_bound=round(mean - 3 * std, 6),
                    upper_bound=round(mean + 3 * std, 6),
                    count=count,
                    percentage=round(100.0 * count / n, 4),
                    confidence=ConfidenceBand.MEDIUM,
                )
            )

    return results


def _anomalies_for_column(name: str, kind: ColumnKind, series: pd.Series) -> list[Anomaly]:
    found: list[Anomaly] = []
    non_null = series.dropna()
    n = len(series)
    if n == 0:
        return found

    if len(non_null) == 0:
        found.append(
            Anomaly(
                column=name,
                kind="fully_empty",
                description="Column is entirely empty across all rows.",
                severity="high",
                evidence={"null_count": int(series.isna().sum())},
            )
        )
        return found

    distinct = int(non_null.nunique())
    if distinct == 1:
        found.append(
            Anomaly(
                column=name,
                kind="constant",
                description="Column has a single distinct value and carries no signal.",
                severity="medium",
                evidence={"value": str(json_safe(non_null.iloc[0]))},
            )
        )

    ws_count = _leading_whitespace_count(series)
    if ws_count:
        found.append(
            Anomaly(
                column=name,
                kind="leading_whitespace",
                description="Values contain leading or trailing whitespace.",
                severity="low",
                evidence={"affected_rows": ws_count},
            )
        )

    if kind in (ColumnKind.NUMERIC, ColumnKind.INTEGER):
        numeric = pd.to_numeric(series, errors="coerce").dropna().astype("float64")
        if len(numeric) >= 8:
            neg = int((numeric < 0).sum())
            if neg and neg / len(numeric) > 0.2:
                found.append(
                    Anomaly(
                        column=name,
                        kind="suspicious_negative_values",
                        description=(
                            "A substantial share of values are negative, which is often "
                            "a sign of a sign-flip or offset bug."
                        ),
                        severity="medium",
                        evidence={"negative_count": neg, "row_count": int(len(numeric))},
                    )
                )
            skew = float(numeric.skew()) if len(numeric) >= 3 else 0.0
            if abs(skew) > 3:
                found.append(
                    Anomaly(
                        column=name,
                        kind="extreme_skew",
                        description=(
                            "Distribution is extremely skewed; a log or Box-Cox transform "
                            "may be appropriate."
                        ),
                        severity="low",
                        evidence={"skewness": round(skew, 4)},
                    )
                )
        for out in _outliers(series, "iqr"):
            if out.count:
                found.append(
                    Anomaly(
                        column=name,
                        kind="outliers_iqr",
                        description=(
                            f"{out.count} values fall outside the 1.5xIQR fence "
                            f"({out.lower_bound} to {out.upper_bound})."
                        ),
                        severity="medium" if out.percentage > 10 else "low",
                        evidence=out.model_dump(),
                    )
                )

    return found


def profile_column(series: pd.Series, ordinal: int, *, detect_outliers: bool = True) -> tuple[ColumnProfile, list[Anomaly]]:
    name = str(series.name)
    total = len(series)
    decision = infer_column_kind(series, name)
    kind = decision.kind

    null_count = int(series.isna().sum())
    non_null_count = total - null_count
    unique_count = int(series.nunique(dropna=True)) if non_null_count else 0

    profile = ColumnProfile(
        name=name,
        ordinal=ordinal,
        inferred_kind=kind,
        pandas_dtype=str(series.dtype),
        non_null_count=non_null_count,
        null_count=null_count,
        missing_percentage=round(100.0 * null_count / total, 4) if total else 0.0,
        unique_count=unique_count,
        cardinality_ratio=round(unique_count / non_null_count, 6) if non_null_count else 0.0,
        is_constant=unique_count == 1 and non_null_count > 0,
        memory_size_bytes=_memory_bytes(series),
        leading_whitespace_count=_leading_whitespace_count(series),
        sample_values=_sample_values(series, kind),
    )

    notes: list[str] = []
    if kind in (ColumnKind.NUMERIC, ColumnKind.INTEGER) and non_null_count:
        profile.numeric = _numeric_summary(series)
        profile.histogram = _histogram(series)
        if detect_outliers:
            profile.outliers = _outliers(series, "iqr") + _outliers(series, "zscore")
        if profile.histogram is None and unique_count <= 10:
            notes.append("Too few distinct values to plot a histogram.")

    if kind in (ColumnKind.CATEGORICAL, ColumnKind.BOOLEAN, ColumnKind.CONSTANT) and non_null_count:
        profile.top_categories, profile.category_percentage_covered = _categorical_summary(series)

    if kind == ColumnKind.TEXT and non_null_count:
        if profile.cardinality_ratio > 0.9:
            notes.append("Nearly every row holds a unique value; likely free text or an ID.")

    if kind == ColumnKind.DATETIME:
        parsed = pd.to_datetime(series, errors="coerce").dropna()
        if not parsed.empty:
            profile.date_min = parsed.min().isoformat()
            profile.date_max = parsed.max().isoformat()

    profile.notes = notes
    return profile, _anomalies_for_column(name, kind, series)


def profile_columns(frame: pd.DataFrame, *, detect_outliers: bool = True) -> tuple[list[ColumnProfile], list[Anomaly]]:
    profiles: list[ColumnProfile] = []
    anomalies: list[Anomaly] = []
    for ordinal, column in enumerate(frame.columns):
        try:
            profile, column_anomalies = profile_column(
                frame[column], ordinal, detect_outliers=detect_outliers
            )
        except Exception as exc:  # noqa: BLE001 - one bad column must not fail the run
            logger.exception("Failed to profile column %s", column)
            profiles.append(
                ColumnProfile(
                    name=str(column),
                    ordinal=ordinal,
                    inferred_kind=ColumnKind.UNKNOWN,
                    pandas_dtype="unknown",
                    non_null_count=0,
                    null_count=int(frame[column].isna().sum()),
                    missing_percentage=0.0,
                    unique_count=0,
                    cardinality_ratio=0.0,
                    is_constant=False,
                    notes=[f"Profiling failed: {exc}"],
                )
            )
            continue
        profiles.append(profile)
        anomalies.extend(column_anomalies)
    return profiles, anomalies


def build_sample_rows(frame: pd.DataFrame, limit: int | None = None) -> SampleRows:
    limit = limit or settings.sample_row_limit
    head = frame.head(limit)
    columns = [str(c) for c in frame.columns]
    rows = [
        [("" if pd.isna(v) else str(json_safe(v))) for v in row]
        for row in head.itertuples(index=False, name=None)
    ]
    return SampleRows(
        columns=columns,
        rows=rows,
        truncated=len(frame) > len(head),
    )