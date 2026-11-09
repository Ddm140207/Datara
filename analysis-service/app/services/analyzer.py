from __future__ import annotations

import logging
import time

import pandas as pd

from app.schemas.profile import DatasetProfile, ProfileRequest
from app.services.correlations import compute_correlations
from app.services.ingestion import DatasetTooLargeError, load_csv
from app.services.profiling import build_sample_rows, profile_columns
from app.services.quality import (
    detect_duplicate_columns,
    detect_issues,
    detect_placeholder_categories,
)
from app.services.scoring import compute_ml_readiness
from app.services.type_inference import looks_like_target

logger = logging.getLogger(__name__)


def _count_duplicates(frame: pd.DataFrame) -> int:
    """Count exact duplicate rows.

    ``duplicated`` treats NaN == NaN as equal, which matches the intuition that two
    rows with the same gaps are duplicates of each other.
    """
    if frame.empty or len(frame.columns) == 0:
        return 0
    try:
        return int(frame.duplicated(keep="first").sum())
    except TypeError:
        # Unhashable cell values (rare, from exotic CSV content) fall back to a
        # string-keyed comparison rather than failing the whole analysis.
        return int(frame.astype(str).duplicated(keep="first").sum())


def _guess_target_candidates(frame: pd.DataFrame, profiles: list) -> list[str]:
    candidates = [
        profile.name
        for profile in profiles
        if looks_like_target(profile.name, profile.inferred_kind, profile.unique_count)
        and profile.non_null_count > 0
    ]
    return candidates[:10]


def profile_dataset(raw: bytes, options: ProfileRequest | None = None) -> DatasetProfile:
    """Full deterministic analysis of a CSV payload."""
    options = options or ProfileRequest()
    started = time.perf_counter()

    ingest = load_csv(raw)
    frame = ingest.frame
    logger.info(
        "Ingested %d rows x %d columns in %.2fs (delimiter=%r, encoding=%s)",
        len(frame),
        len(frame.columns),
        time.perf_counter() - started,
        ingest.delimiter,
        ingest.encoding,
    )

    row_count = len(frame)
    column_count = len(frame.columns)
    profiles, anomalies = profile_columns(frame, detect_outliers=options.detect_outliers)

    duplicate_row_count = _count_duplicates(frame)
    correlations = (
        compute_correlations(frame, profiles) if options.include_correlations else None
    )

    issues = detect_issues(
        frame,
        profiles,
        duplicate_row_count=duplicate_row_count,
        row_count=row_count,
        correlations=correlations,
        target_column=options.target_column,
    )
    issues.extend(detect_placeholder_categories(profiles))
    issues.extend(detect_duplicate_columns(frame, profiles))

    order = {"critical": 0, "high": 1, "medium": 2, "low": 3, "info": 4}
    issues.sort(key=lambda i: (order.get(i.severity.value, 5), -abs(i.score_impact)))

    readiness = compute_ml_readiness(
        frame,
        profiles,
        row_count=row_count,
        duplicate_row_count=duplicate_row_count,
        correlations=correlations,
        target_column=options.target_column,
    )

    total_cells = row_count * column_count
    total_missing = sum(p.null_count for p in profiles)
    samples = build_sample_rows(frame, options.sample_row_limit) if options.include_samples else None

    elapsed = time.perf_counter() - started
    logger.info("Profiled dataset in %.2fs", elapsed)

    return DatasetProfile(
        row_count=row_count,
        column_count=column_count,
        total_cells=total_cells,
        total_missing=total_missing,
        overall_missing_percentage=round(100.0 * total_missing / total_cells, 4)
        if total_cells
        else 0.0,
        duplicate_row_count=duplicate_row_count,
        duplicate_percentage=round(100.0 * duplicate_row_count / row_count, 4)
        if row_count
        else 0.0,
        fully_empty_columns=[p.name for p in profiles if p.non_null_count == 0],
        memory_usage_bytes=int(frame.memory_usage(deep=True).sum()),
        ingested_at_utc=pd.Timestamp.utcnow().isoformat(),
        warnings=ingest.warnings,
        parse_errors=ingest.parse_errors,
        columns=profiles,
        correlations=correlations,
        anomalies=anomalies,
        quality_issues=issues,
        ml_readiness=readiness,
        samples=samples,
        target_candidates=_guess_target_candidates(frame, profiles),
    )


def profile_file_path(path: str, options: ProfileRequest | None = None) -> DatasetProfile:
    """Convenience entry point for local runs and the test-suite."""
    with open(path, "rb") as handle:
        return profile_dataset(handle.read(), options)


__all__ = ["profile_dataset", "profile_file_path", "DatasetTooLargeError"]