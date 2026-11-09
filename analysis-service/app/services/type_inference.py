from __future__ import annotations

import re
from dataclasses import dataclass

import numpy as np
import pandas as pd

from app.schemas.profile import ColumnKind
_DATE_HINT = re.compile(
    r"^\s*(\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}[-/]\d{1,2}[-/]\d{2,4}"
    r"|\d{4}-\d{1,2}-\d{1,2}[T ]\d{1,2}:\d{2}"
    r"|[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4})\s*$"
)

_BOOL_TOKENS = {"true", "false", "t", "f", "yes", "no", "y", "n", "1", "0", "0.0", "1.0"}

_ID_NAME_HINT = re.compile(
    r"(^|_)(id|uuid|guid|key|code|index|idx|no|num|number|sn|serial|hash)(_|$)", re.IGNORECASE
)

_BOOL_NAME_HINT = re.compile(r"(^|_)(is|has|can|should|was|were|does|did|flags?)(_|$)", re.IGNORECASE)

_NUMERIC_NAME_HINT = re.compile(
    r"(^|_)(rate|ratio|score|amount|price|cost|total|count|age|value|level|pct|percent)(_|$)",
    re.IGNORECASE,
)

_CATEGORICAL_NAME_HINT = re.compile(
    r"(^|_)(type|kind|category|class|status|group|segment|channel|region|country|city|"
    r"gender|plan|tier|source|device|state|role|industry)(_|$)",
    re.IGNORECASE,
)

# Names that usually denote the supervised prediction target.
_TARGET_NAME_HINT = re.compile(
    r"(^|_)(target|label|outcome|churn|result|response|converted|purchase|default|"
    r"fraud|failure|y_true|ground_truth)(_|$)|^(is|has|was)_",
    re.IGNORECASE,
)

_TEXT_NAME_HINT = re.compile(r"(^|_)(name|description|comment|notes?|text|email|url|address|message)(_|$)", re.IGNORECASE)


@dataclass(frozen=True)
class TypeDecision:
    kind: ColumnKind
    confidence: float
    reason: str


def _sample_non_null(series: pd.Series, size: int = 500) -> pd.Series:
    return series.dropna().head(size)


def _looks_boolean(series: pd.Series) -> bool:
    values = _sample_non_null(series)
    if values.empty:
        return False
    try:
        tokens = values.astype("string").str.strip().str.lower()
    except (TypeError, ValueError):
        return False
    if tokens.isin(_BOOL_TOKENS | {"true", "false"}).all():
        return tokens.nunique() <= 2
    return False


def _looks_date(sample: pd.Series) -> bool:
    if sample.empty:
        return False
    try:
        as_text = sample.astype("string").str.strip()
    except (TypeError, ValueError):
        return False
    hint_ratio = as_text.str.match(_DATE_HINT).mean()
    if hint_ratio < 0.8:
        return False
    parsed = pd.to_datetime(as_text, errors="coerce", format="mixed")
    return parsed.notna().mean() >= 0.9


def infer_column_kind(series: pd.Series, name: str = "") -> TypeDecision:
    """Classify a column as numeric / boolean / categorical / text / datetime.

    The name of the column is used only as a tie-breaker after the observed values
    have been inspected, which keeps inference driven by data rather than headers.
    """
    non_null = series.dropna()
    total = len(series)
    present = len(non_null)

    if present == 0:
        return TypeDecision(ColumnKind.UNKNOWN, 1.0, "Column contains no values.")

    distinct = non_null.nunique(dropna=True)
    null_ratio = 1 - (present / total) if total else 0.0

    # --- native pandas dtypes -------------------------------------------------
    if pd.api.types.is_bool_dtype(series):
        return TypeDecision(ColumnKind.BOOLEAN, 1.0, "Native boolean dtype.")
    if pd.api.types.is_datetime64_any_dtype(series):
        return TypeDecision(ColumnKind.DATETIME, 1.0, "Native datetime dtype.")
    if pd.api.types.is_integer_dtype(series):
        return TypeDecision(ColumnKind.INTEGER, 1.0, "Native integer dtype.")
    if pd.api.types.is_float_dtype(series):
        # The nullable float backend widens whole numbers to float64. Recover the
        # integer intent so consumers do not have to special-case float columns
        # that only hold whole values.
        try:
            numeric_view = series.dropna().astype("float64")
            if len(numeric_view) and np.all(np.mod(numeric_view.to_numpy(), 1) == 0):
                return TypeDecision(
                    ColumnKind.INTEGER, 0.98, "Float storage holding whole numbers."
                )
        except (TypeError, ValueError):
            pass
        return TypeDecision(ColumnKind.NUMERIC, 1.0, "Native floating point dtype.")

    if pd.api.types.is_string_dtype(series) or series.dtype == object:
        sample = _sample_non_null(series)

        if _looks_boolean(series):
            return TypeDecision(
                ColumnKind.BOOLEAN, 0.9, "Values contain only boolean-like tokens."
            )

        if _looks_date(sample):
            return TypeDecision(
                ColumnKind.DATETIME, 0.95, "Values match common date/time formats."
            )

        # Attempt numeric coercion, but only accept it when almost everything parses.
        try:
            numeric = pd.to_numeric(series, errors="coerce")
        except (TypeError, ValueError):
            numeric = None

        if numeric is not None and present > 0:
            non_null_numeric = numeric.dropna()
            coerced_ratio = len(non_null_numeric) / present
            # Require a high bar: a single stray token (e.g. "1,234" mixed with
            # "unknown") should not demote a genuinely numeric column.
            if coerced_ratio >= 0.98 and len(non_null_numeric) >= max(2, int(0.02 * total)):
                is_int = bool(
                    np.all(np.mod(non_null_numeric.to_numpy(dtype="float64"), 1) == 0)
                )
                if is_int and _ID_NAME_HINT.search(name or ""):
                    return TypeDecision(
                        ColumnKind.INTEGER,
                        0.75,
                        "Integer values, but the column name suggests an identifier.",
                    )
                kind = ColumnKind.INTEGER if is_int else ColumnKind.NUMERIC
                return TypeDecision(
                    kind, coerced_ratio, "All non-null values parsed as numbers."
                )

        if distinct <= 1:
            return TypeDecision(ColumnKind.CONSTANT, 1.0, "Only one distinct value.")

        ratio = distinct / present
        if ratio <= 0.5 or distinct <= 50:
            if _TEXT_NAME_HINT.search(name or "") and distinct > 25:
                return TypeDecision(
                    ColumnKind.TEXT,
                    0.7,
                    "Column name suggests free text and cardinality is high.",
                )
            return TypeDecision(
                ColumnKind.CATEGORICAL,
                min(1.0, 0.6 + ratio),
                f"{distinct} distinct values across {present} rows.",
            )

        return TypeDecision(
            ColumnKind.TEXT,
            min(1.0, ratio),
            f"High cardinality ({distinct} distinct across {present} rows).",
        )

    # --- fallback ------------------------------------------------------------
    if pd.api.types.is_numeric_dtype(series):
        return TypeDecision(ColumnKind.NUMERIC, 0.8, "Numeric dtype.")

    name = name or ""
    if _CATEGORICAL_NAME_HINT.search(name):
        return TypeDecision(ColumnKind.CATEGORICAL, 0.6, "Inferred from column name.")
    if _NUMERIC_NAME_HINT.search(name):
        return TypeDecision(ColumnKind.NUMERIC, 0.55, "Inferred from column name.")
    if _TEXT_NAME_HINT.search(name):
        return TypeDecision(ColumnKind.TEXT, 0.6, "Inferred from column name.")
    if _BOOL_NAME_HINT.search(name) and distinct <= 2:
        return TypeDecision(ColumnKind.BOOLEAN, 0.6, "Inferred from column name.")
    if null_ratio > 0.5:
        return TypeDecision(ColumnKind.UNKNOWN, 0.4, "Column is mostly empty.")
    return TypeDecision(ColumnKind.UNKNOWN, 0.3, "Type could not be determined.")


def looks_like_identifier(name: str, kind: ColumnKind, non_null_count: int, unique_count: int) -> bool:
    """True when a column is a row key rather than a usable modelling feature.

    Continuous numeric columns are excluded by design: a float measured to the cent
    is essentially always unique per row, and treating that as an identifier problem
    produces false positives on essentially every real dataset.
    """
    if non_null_count < 2:
        return False

    if unique_count < non_null_count:
        return False  # not unique per row, so not a key

    if kind in (ColumnKind.NUMERIC, ColumnKind.DATETIME):
        return False

    if _ID_NAME_HINT.search(name or ""):
        return True

    # A fully-unique string is a natural key (email, URL, free-text id).
    return kind == ColumnKind.TEXT


def looks_like_target(name: str, kind: ColumnKind, distinct: int) -> bool:
    """Heuristic guess at whether a column could serve as a supervised target."""
    if kind == ColumnKind.BOOLEAN:
        return True
    if kind not in (ColumnKind.CATEGORICAL, ColumnKind.INTEGER):
        return False
    if not (_CATEGORICAL_NAME_HINT.search(name or "") or _TARGET_NAME_HINT.search(name or "")):
        return False
    return 2 <= distinct <= 50