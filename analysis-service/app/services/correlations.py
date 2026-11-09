from __future__ import annotations

import logging
import math
import re

import numpy as np
import pandas as pd

from app.schemas.profile import (
    ColumnKind,
    ColumnProfile,
    CorrelationMatrix,
    CorrelationPair,
)

logger = logging.getLogger(__name__)

STRONG_CORRELATION = 0.7
MODERATE_CORRELATION = 0.4
MAX_PAIRS = 200


def _numeric_view(series: pd.Series, kind: ColumnKind) -> pd.Series | None:
    if kind == ColumnKind.BOOLEAN:
        mapped = series.map({True: 1.0, False: 0.0, pd.NA: np.nan})
        return pd.to_numeric(mapped, errors="coerce")
    if kind in (ColumnKind.NUMERIC, ColumnKind.INTEGER):
        return pd.to_numeric(series, errors="coerce")
    return None


def compute_correlations(
    frame: pd.DataFrame, profiles: list[ColumnProfile], *, max_columns: int = 60
) -> CorrelationMatrix | None:
    """Pearson correlation across numeric and boolean columns.

    Columns are capped so a wide table cannot blow up the O(n^2) matrix. When the cap
    is hit we keep the columns with the lowest missingness, which preserves the most
    statistically useful pairs.
    """
    candidates: list[tuple[str, pd.Series]] = []
    for profile in profiles:
        if profile.inferred_kind not in (
            ColumnKind.NUMERIC,
            ColumnKind.INTEGER,
            ColumnKind.BOOLEAN,
        ):
            continue
        if profile.unique_count <= 1:
            continue
        candidates.append((profile.name, _numeric_view(frame[profile.name], profile.inferred_kind)))

    if len(candidates) < 2:
        return None

    if len(candidates) > max_columns:
        ranked = sorted(candidates, key=lambda item: item[1].isna().mean())
        candidates = ranked[:max_columns]
        note = (
            f"Correlation computed over the {max_columns} most complete numeric columns "
            f"of {len(profiles)} profiled."
        )
    else:
        note = None

    names = [name for name, _ in candidates]
    matrix = pd.DataFrame({name: series for name, series in candidates})
    matrix = matrix.replace([np.inf, -np.inf], np.nan)
    matrix = matrix.dropna(axis=1, how="all")

    if matrix.shape[1] < 2:
        return None

    with np.errstate(invalid="ignore", divide="ignore"):
        corr = matrix.corr(method="pearson", numeric_only=False)

    # Constant columns produce NaN; a dataset made only of constants has nothing to say.
    if corr.isna().all().all():
        return None

    names = [str(c) for c in corr.columns]
    values: list[list[float | None]] = []
    for row in corr.to_numpy():
        values.append(
            [None if (v is None or (isinstance(v, float) and math.isnan(v))) else round(float(v), 6) for v in row]
        )

    pairs: list[CorrelationPair] = []
    for i, a in enumerate(names):
        for j, b in enumerate(names):
            if j <= i:
                continue
            coeff = values[i][j]
            if coeff is None:
                continue
            pairs.append(CorrelationPair(column_a=a, column_b=b, coefficient=coeff, method="pearson"))

    pairs.sort(key=lambda p: abs(p.coefficient), reverse=True)

    return CorrelationMatrix(
        method="pearson",
        columns=names,
        values=values,
        pairs=pairs[:MAX_PAIRS],
        note=note,
    )


LEAKAGE_CORRELATION_THRESHOLD = 0.98
LEAKAGE_MIN_DISTINCT = 8
LEAKAGE_MIN_ROWS = 50
MAX_LEAKAGE_SIGNALS = 6

# Column-name fragments that imply a value recorded *after* an outcome occurred.
# These are the highest-signal leakage indicators available without a target.
_POST_OUTCOME_HINT = re.compile(
    r"(closed|cancelled|canceled|terminated|churned|resolved|refunded|returned|"
    r"escalated|resolved_at|closed_at|deleted|archived)",
    re.IGNORECASE,
)

_POST_OUTCOME_TOKENS = {
    "closed",
    "cancelled",
    "canceled",
    "terminated",
    "churned",
    "resolved",
    "refunded",
    "returned",
    "escalated",
    "deleted",
    "archived",
}

_TARGET_NAME_HINT = re.compile(
    r"(^|_)(target|label|outcome|churn|result|y_true|ground_truth)(_|$)|^(is|has|was)_",
    re.IGNORECASE,
)


def _is_bare_post_outcome_name(name: str) -> bool:
    """True when the column is *itself* the outcome, not a post-outcome artefact.

    ``churned`` is the label; ``churned_at`` or ``account_closed_flag`` is leakage.
    """
    stripped = re.sub(r"^(is|has|was|did)_", "", (name or "").strip(), flags=re.IGNORECASE)
    return stripped.lower() in _POST_OUTCOME_TOKENS


DUPLICATE_COLUMN_MAX_CARDINALITY = 2_000


def find_duplicate_columns(
    frame: pd.DataFrame, profiles: list[ColumnProfile]
) -> list[tuple[str, str]]:
    """Return pairs of columns holding identical values.

    Duplicate columns are a common export artefact and, when one copy is the target,
    the single most damaging leakage pattern. Detection is by 64-bit content hash of
    the string representation, which is independent of column naming and dtype.
    """
    candidates = [
        p
        for p in profiles
        if p.non_null_count > 0
        and p.unique_count > 1
        and p.unique_count <= DUPLICATE_COLUMN_MAX_CARDINALITY
    ]
    if len(candidates) < 2:
        return []

    hashes: dict[str, np.ndarray] = {}
    null_counts: dict[str, int] = {}
    for profile in candidates:
        try:
            if profile.inferred_kind in (ColumnKind.NUMERIC, ColumnKind.INTEGER, ColumnKind.BOOLEAN):
                # Compare numerically so an int column and a float copy of it are
                # recognised as the same data rather than as different strings.
                canonical: pd.Series = pd.to_numeric(
                    frame[profile.name], errors="coerce"
                ).astype("float64")
            else:
                canonical = frame[profile.name].astype("string")
            canonical = canonical.where(canonical.notna(), "<NA>")
            hashes[profile.name] = pd.util.hash_pandas_object(
                canonical, index=False
            ).to_numpy(dtype="uint64")
            null_counts[profile.name] = profile.null_count
        except (TypeError, ValueError):
            continue

    pairs: list[tuple[str, str]] = []
    names = list(hashes.keys())
    for i, a in enumerate(names):
        for b in names[i + 1 :]:
            # Identical non-null content with a different null pattern is a partial
            # overlap, which is far less interesting than a true copy.
            if null_counts[a] != null_counts[b]:
                continue
            if np.array_equal(hashes[a], hashes[b]):
                pairs.append((a, b))
    return pairs


def find_target_leakage(
    frame: pd.DataFrame, profiles: list[ColumnProfile], correlations: CorrelationMatrix | None
) -> list[dict]:
    """Heuristics for columns that leak the prediction target.

    Detection is deliberately conservative: a false positive here costs the user
    real modelling time, so every signal must clear a strength threshold before it
    is reported.

    Returns plain dicts; the scoring engine converts them into typed signals.
    """
    signals: list[dict] = []
    usable = [p for p in profiles if p.non_null_count > 0 and p.unique_count >= 2]

    # --- rule 0: an exact duplicate of the target column ---------------------
    duplicates = find_duplicate_columns(frame, profiles)
    for a, b in duplicates:
        signals.append(
            {
                "column": a,
                "other": b,
                "signal": "duplicate_column",
                "severity": "high",
                "rationale": (
                    f"'{a}' and '{b}' contain identical values in every row. One of them "
                    "is a redundant copy; if either is the prediction target, keeping both "
                    "leaks the answer into training."
                ),
                "evidence": {"other_column": b, "rows": len(frame)},
            }
        )

    # --- rule 1: near-perfect correlation between two columns -----------------
    if correlations:
        seen_pairs: set[frozenset[str]] = set()
        strong = [
            p
            for p in correlations.pairs
            if abs(p.coefficient) >= LEAKAGE_CORRELATION_THRESHOLD
            and p.column_a != p.column_b
        ]
        for pair in strong:
            key = frozenset({pair.column_a, pair.column_b})
            if key in seen_pairs:
                continue
            seen_pairs.add(key)
            a = next((p for p in usable if p.name == pair.column_a), None)
            b = next((p for p in usable if p.name == pair.column_b), None)
            # Ties and low cardinality manufacture near-perfect Pearson
            # correlations on small samples, so require genuine variation.
            if not a or not b:
                continue
            if a.unique_count < LEAKAGE_MIN_DISTINCT or b.unique_count < LEAKAGE_MIN_DISTINCT:
                continue
            signals.append(
                {
                    "column": pair.column_a,
                    "other": pair.column_b,
                    "signal": "near_perfect_correlation",
                    "severity": "high",
                    "rationale": (
                        f"'{pair.column_a}' and '{pair.column_b}' correlate at "
                        f"{pair.coefficient:.4f}. One is almost certainly derived from or "
                        "recorded alongside the other, so including both will leak."
                    ),
                    "evidence": {
                        "correlation": pair.coefficient,
                        "other_column": pair.column_b,
                        "unique_values_a": a.unique_count,
                        "unique_values_b": b.unique_count,
                    },
                }
            )
            if len(signals) >= MAX_LEAKAGE_SIGNALS:
                break

    # --- rule 2: post-outcome column naming ----------------------------------
    for profile in usable:
        if not _POST_OUTCOME_HINT.search(profile.name or ""):
            continue
        if _is_bare_post_outcome_name(profile.name):
            continue  # this is the label itself, not an artefact of it
        signals.append(
                {
                    "column": profile.name,
                    "other": None,
                    "signal": "post_outcome_column_name",
                    "severity": "high",
                    "rationale": (
                        f"The name '{profile.name}' implies the value is recorded after "
                        "an event already happened (closed, cancelled, refunded ...). Such "
                        "a column does not exist at prediction time."
                    ),
                    "evidence": {
                        "unique_values": profile.unique_count,
                        "non_null_count": profile.non_null_count,
                    },
                }
            )

    # --- rule 3: target-named columns (advisory only) ------------------------
    for profile in usable:
        if profile.unique_count <= 50 and _TARGET_NAME_HINT.search(profile.name or ""):
            signals.append(
                {
                    "column": profile.name,
                    "other": None,
                    "signal": "target_named_column",
                    "severity": "low",
                    "rationale": (
                        f"The name '{profile.name}' suggests it may be the prediction "
                        "target. Keep it out of the feature set unless you are building "
                        "an unsupervised model."
                    ),
                    "evidence": {
                        "unique_values": profile.unique_count,
                        "inferred_kind": profile.inferred_kind.value,
                    },
                }
            )
            if len(signals) >= MAX_LEAKAGE_SIGNALS:
                break

    return signals