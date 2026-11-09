from __future__ import annotations

import re

import pandas as pd

from app.schemas.profile import (
    ColumnKind,
    ColumnProfile,
    CorrelationMatrix,
    QualityIssue,
)

# Column names that almost always hold contact details or PII.
_PII_NAME_HINT = re.compile(
    r"(^|_)(email|e_mail|phone|mobile|ssn|social_security|passport|address|street|"
    r"postcode|zip|ip|ip_address|dob|date_of_birth|full_name|first_name|last_name|"
    r"latitude|longitude|lat|lon)(_|$)",
    re.IGNORECASE,
)

PLACEHOLDER_VALUES = {
    "n/a",
    "na",
    "none",
    "null",
    "-",
    "--",
    "tbd",
    "todo",
    "test",
    "sample",
    "dummy",
    "placeholder",
    "xxx",
    "xxxx",
    "unknown",
    "?",
    "0",
    "0000-00-00",
}


def _issue(**kwargs) -> QualityIssue:
    return QualityIssue(**kwargs)


def detect_issues(
    frame: pd.DataFrame,
    profiles: list[ColumnProfile],
    *,
    duplicate_row_count: int,
    row_count: int,
    correlations: CorrelationMatrix | None,
    target_column: str | None = None,
) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    issues.extend(_missing_value_issues(profiles, row_count))
    issues.extend(_duplicate_issues(duplicate_row_count, row_count))
    issues.extend(_column_shape_issues(profiles, row_count))
    issues.extend(_type_consistency_issues(profiles))
    issues.extend(_outlier_issues(profiles))
    issues.extend(_cardinality_issues(profiles))
    issues.extend(_target_issues(frame, profiles, target_column))
    issues.extend(_dimensionality_issues(profiles, row_count))
    issues.extend(_pii_issues(profiles))

    order = {"critical": 0, "high": 1, "medium": 2, "low": 3, "info": 4}
    issues.sort(key=lambda i: (order.get(i.severity.value, 5), -abs(i.score_impact)))
    return issues


# --------------------------------------------------------------------------- #
# Missing values
# --------------------------------------------------------------------------- #
def _missing_value_issues(profiles: list[ColumnProfile], row_count: int) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    for profile in profiles:
        pct = profile.missing_percentage
        if pct <= 0:
            continue

        column = profile.name
        if pct >= 90:
            severity = "critical"
            recommendation = (
                f"Drop `{column}`. It is {pct:.1f}% empty, so any imputation would be "
                "inventing more than 90% of the column."
            )
            impact = 12.0
        elif pct >= 50:
            severity = "high"
            recommendation = (
                f"Investigate whether missingness in `{column}` is systematic before "
                f"imputing. At {pct:.1f}% missing, consider dropping the column if it "
                "is not predictive."
            )
            impact = 9.0
        elif pct >= 20:
            severity = "medium"
            recommendation = (
                f"Consider imputation for `{column}` (median for numeric, \"Unknown\" "
                "bucket or mode for categorical) and add a missingness indicator feature."
            )
            impact = 5.0
        elif pct >= 5:
            severity = "low"
            recommendation = (
                f"{column} has {pct:.1f}% missing values. Impute before training a "
                "predictive model."
            )
            impact = 2.0
        else:
            severity = "info"
            recommendation = (
                f"{column} has {pct:.1f}% missing values. A small amount of missingness "
                "is usually tolerable."
            )
            impact = 0.5

        issues.append(
            _issue(
                code=f"missing_values::{column}",
                severity=severity,
                category="missing_values",
                column=column,
                title=f"{pct:.1f}% missing values in '{column}'",
                description=(
                    f"Column '{column}' has {profile.null_count:,} missing values out of "
                    f"{row_count:,} rows ({pct:.1f}%)."
                ),
                evidence={
                    "missing_count": profile.null_count,
                    "total_rows": row_count,
                    "missing_percentage": pct,
                    "non_null_count": profile.non_null_count,
                },
                recommendation=recommendation,
                score_impact=impact,
            )
        )

    # A dataset that is mostly empty is a structural problem, not a per-column one.
    overall_missing = (
        sum(p.null_count for p in profiles) / max(1, row_count * len(profiles)) * 100
        if profiles
        else 0
    )
    if overall_missing > 30:
        issues.append(
            _issue(
                code="missing_values::dataset",
                severity="high",
                category="missing_values",
                title=f"{overall_missing:.1f}% of all cells are empty",
                description=(
                    f"Across {len(profiles):,} columns and {row_count:,} rows, "
                    f"{overall_missing:.1f}% of values are missing. This will degrade most "
                    "models and any manual analysis."
                ),
                evidence={
                    "overall_missing_percentage": round(overall_missing, 4),
                    "column_count": len(profiles),
                    "row_count": row_count,
                },
                recommendation=(
                    "Start with the highest-missingness columns: decide for each whether "
                    "to drop it, impute it, or recover the source system."
                ),
                score_impact=10.0,
            )
        )
    return issues


# --------------------------------------------------------------------------- #
# Duplicates
# --------------------------------------------------------------------------- #
def _duplicate_issues(duplicate_row_count: int, row_count: int) -> list[QualityIssue]:
    if duplicate_row_count <= 0:
        return []
    pct = 100.0 * duplicate_row_count / row_count if row_count else 0.0

    if pct >= 20:
        severity, impact = "high", 10.0
    elif pct >= 5:
        severity, impact = "medium", 5.0
    else:
        severity, impact = "low", 2.0

    return [
        _issue(
            code="duplicates::full_row",
            severity=severity,
            category="duplicates",
            title=f"{duplicate_row_count:,} duplicate rows ({pct:.1f}%)",
            description=(
                f"{duplicate_row_count:,} of {row_count:,} rows are exact duplicates of "
                "another row. This inflates group counts and biases models toward the "
                "over-represented records."
            ),
            evidence={
                "duplicate_rows": duplicate_row_count,
                "total_rows": row_count,
                "duplicate_percentage": round(pct, 4),
            },
            recommendation=(
                "De-duplicate before modelling. Confirm first whether repeated rows are "
                "legitimate (for example one row per transaction) or an artefact of "
                "concatenating extracts."
            ),
            score_impact=impact,
        )
    ]


# --------------------------------------------------------------------------- #
# Column shape: empty, constant, whitespace
# --------------------------------------------------------------------------- #
def _column_shape_issues(profiles: list[ColumnProfile], row_count: int) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    for profile in profiles:
        if profile.null_count and profile.non_null_count == 0:
            issues.append(
                _issue(
                    code=f"constant_column::empty::{profile.name}",
                    severity="critical",
                    category="constant_column",
                    column=profile.name,
                    title=f"Column '{profile.name}' is entirely empty",
                    description=(
                        f"All {row_count:,} rows are null in '{profile.name}'. The column "
                        "contributes no information."
                    ),
                    evidence={"null_count": profile.null_count, "total_rows": row_count},
                    recommendation=f"Drop '{profile.name}' from the dataset.",
                    score_impact=8.0,
                )
            )
        elif profile.is_constant:
            issues.append(
                _issue(
                    code=f"constant_column::value::{profile.name}",
                    severity="medium",
                    category="constant_column",
                    column=profile.name,
                    title=f"Column '{profile.name}' has a single constant value",
                    description=(
                        f"'{profile.name}' contains only {profile.unique_count} distinct "
                        "value, so it has zero variance and cannot influence a model."
                    ),
                    evidence={
                        "unique_count": profile.unique_count,
                        "non_null_count": profile.non_null_count,
                    },
                    recommendation=(
                        f"Drop '{profile.name}'. If the constant encodes a segment, "
                        "re-collect it as a variable."
                    ),
                    score_impact=4.0,
                )
            )

        if profile.leading_whitespace_count:
            issues.append(
                _issue(
                    code=f"consistency::whitespace::{profile.name}",
                    severity="low",
                    category="consistency",
                    column=profile.name,
                    title=f"Leading/trailing whitespace in '{profile.name}'",
                    description=(
                        f"At least {profile.leading_whitespace_count:,} sampled values in "
                        f"'{profile.name}' start with whitespace, which splits what should "
                        "be single categories."
                    ),
                    evidence={"affected_rows": profile.leading_whitespace_count},
                    recommendation=(
                        f"Trim whitespace and normalise case on '{profile.name}' during "
                        "ingestion."
                    ),
                    score_impact=1.5,
                )
            )
    return issues


# --------------------------------------------------------------------------- #
# Type validity / consistency
# --------------------------------------------------------------------------- #
def _type_consistency_issues(profiles: list[ColumnProfile]) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    for profile in profiles:
        if profile.mixed_type_count > 0:
            ratio = profile.mixed_type_count / max(1, profile.non_null_count)
            issues.append(
                _issue(
                    code=f"type_validity::mixed::{profile.name}",
                    severity="medium" if ratio > 0.02 else "low",
                    category="type_validity",
                    column=profile.name,
                    title=f"Mixed types in '{profile.name}'",
                    description=(
                        f"{profile.mixed_type_count:,} values in '{profile.name}' could not "
                        "be interpreted consistently with the rest of the column."
                    ),
                    evidence={"mixed_type_count": profile.mixed_type_count},
                    recommendation=(
                        f"Coerce '{profile.name}' explicitly and log the values that fail "
                        "the coercion so the source system can be fixed."
                    ),
                    score_impact=4.0,
                )
            )
    return issues


# --------------------------------------------------------------------------- #
# Outliers
# --------------------------------------------------------------------------- #
def _outlier_issues(profiles: list[ColumnProfile]) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    for profile in profiles:
        if profile.inferred_kind not in (ColumnKind.NUMERIC, ColumnKind.INTEGER):
            continue
        primary = next((o for o in profile.outliers if o.method == "IQR"), None)
        if primary is None or primary.count == 0:
            continue

        if primary.percentage >= 15:
            severity, impact = "medium", 6.0
            recommendation = (
                f"Over 15% of '{profile.name}' sits outside the IQR fence. This usually "
                "means the column mixes populations or contains a scale error rather than "
                "true outliers. Verify the source before clipping."
            )
        elif primary.percentage >= 5:
            severity, impact = "medium", 4.0
            recommendation = (
                f"Inspect the {primary.count:,} outlying values in '{profile.name}'. If "
                "they are genuine, consider a robust scaler instead of removing them."
            )
        else:
            severity, impact = "low", 1.5
            recommendation = (
                f"A small number of values in '{profile.name}' fall outside the IQR "
                "fence. Usually harmless, but confirm they are not data-entry errors."
            )

        issues.append(
            _issue(
                code=f"outliers::iqr::{profile.name}",
                severity=severity,
                category="outliers",
                column=profile.name,
                title=f"{primary.percentage:.1f}% outliers in '{profile.name}'",
                description=(
                    f"{primary.count:,} values ({primary.percentage:.1f}%) in "
                    f"'{profile.name}' fall outside [{primary.lower_bound}, "
                    f"{primary.upper_bound}] using the 1.5xIQR rule."
                ),
                evidence=primary.model_dump(),
                recommendation=recommendation,
                score_impact=impact,
            )
        )
    return issues


# --------------------------------------------------------------------------- #
# Cardinality / identifiers
# --------------------------------------------------------------------------- #
def _cardinality_issues(profiles: list[ColumnProfile]) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    for profile in profiles:
        if profile.non_null_count < 20:
            continue
        if (
            profile.cardinality_ratio >= 0.98
            and profile.unique_count >= 50
            and profile.inferred_kind not in (ColumnKind.NUMERIC, ColumnKind.DATETIME)
        ):
            issues.append(
                _issue(
                    code=f"cardinality::unique::{profile.name}",
                    severity="medium",
                    category="identifiers",
                    column=profile.name,
                    title=f"'{profile.name}' is unique for every row",
                    description=(
                        f"'{profile.name}' has {profile.unique_count:,} distinct values "
                        f"across {profile.non_null_count:,} rows "
                        f"({profile.cardinality_ratio:.0%} unique). It is almost certainly "
                        "an identifier."
                    ),
                    evidence={
                        "unique_count": profile.unique_count,
                        "non_null_count": profile.non_null_count,
                        "cardinality_ratio": profile.cardinality_ratio,
                    },
                    recommendation=(
                        f"Exclude '{profile.name}' from the feature set unless you are "
                        "joining on it. High-cardinality strings cause severe overfitting "
                        "in tree and linear models."
                    ),
                    score_impact=5.0,
                )
            )
        elif profile.inferred_kind == ColumnKind.CATEGORICAL and profile.unique_count > 100:
            issues.append(
                _issue(
                    code=f"cardinality::high::{profile.name}",
                    severity="low",
                    category="cardinality",
                    column=profile.name,
                    title=f"'{profile.name}' is high-cardinality categorical",
                    description=(
                        f"'{profile.name}' is treated as categorical but has "
                        f"{profile.unique_count:,} distinct values. One-hot encoding this "
                        "column would create an unwieldy feature space."
                    ),
                    evidence={"unique_count": profile.unique_count},
                    recommendation=(
                        f"Reduce the cardinality of '{profile.name}' (bucket rare values "
                        "into \"Other\"), use target/frequency encoding, or hash it."
                    ),
                    score_impact=2.0,
                )
            )
    return issues


# --------------------------------------------------------------------------- #
# Target column concerns
# --------------------------------------------------------------------------- #
def _target_issues(
    frame: pd.DataFrame, profiles: list[ColumnProfile], target_column: str | None
) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    if not target_column or target_column not in frame.columns:
        return issues

    series = frame[target_column]
    profile = next((p for p in profiles if p.name == target_column), None)
    if profile is None:
        return issues

    if profile.null_count > 0:
        pct = profile.missing_percentage
        issues.append(
            _issue(
                code=f"missing_values::target::{target_column}",
                severity="high" if pct > 5 else "medium",
                category="missing_values",
                column=target_column,
                title=f"Target column '{target_column}' has {pct:.1f}% missing values",
                description=(
                    f"Rows with a null target cannot be used for supervised learning. "
                    f"{profile.null_count:,} rows will be dropped or need relabelling."
                ),
                evidence={"missing_count": profile.null_count, "missing_percentage": pct},
                recommendation=(
                    f"Drop rows where '{target_column}' is null, or define an explicit "
                    "\"unknown\" class if the missingness is meaningful."
                ),
                score_impact=7.0,
            )
        )

    counts = series.value_counts(dropna=True)
    if len(counts) >= 2 and series.notna().sum() > 0:
        total = float(series.notna().sum())
        majority = float(counts.iloc[0]) / total * 100
        minority = float(counts.iloc[-1]) / total * 100
        ratio = float(counts.iloc[0] / counts.iloc[-1])
        if minority < 5:
            issues.append(
                _issue(
                    code=f"class_imbalance::{target_column}",
                    severity="high" if minority < 1 else "medium",
                    category="class_imbalance",
                    column=target_column,
                    title=f"Severe class imbalance in '{target_column}'",
                    description=(
                        f"The rarest class in '{target_column}' covers only "
                        f"{minority:.2f}% of rows; the most common covers {majority:.2f}%. "
                        "Accuracy will be misleading on this target."
                    ),
                    evidence={
                        "majority_percentage": round(majority, 4),
                        "minority_percentage": round(minority, 4),
                        "imbalance_ratio": round(ratio, 4),
                        "distinct_classes": int(len(counts)),
                    },
                    recommendation=(
                        f"Use stratified splitting and class weighting (for example "
                        "`class_weight='balanced'`), or resample the minority class in "
                        f"'{target_column}'. Evaluate with ROC-AUC, PR-AUC or F1 instead "
                        "of accuracy."
                    ),
                    score_impact=8.0,
                )
            )
    return issues


# --------------------------------------------------------------------------- #
# Dataset-level structure
# --------------------------------------------------------------------------- #
def _dimensionality_issues(profiles: list[ColumnProfile], row_count: int) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    if not profiles or row_count == 0:
        return issues

    ratio = len(profiles) / row_count
    if len(profiles) > 500 or (ratio > 1 and len(profiles) > 50):
        issues.append(
            _issue(
                code="high_dimensionality::columns_to_rows",
                severity="medium",
                category="high_dimensionality",
                title=f"{len(profiles):,} columns for only {row_count:,} rows",
                description=(
                    "There are more features than observations, which invites overfitting "
                    "and makes most statistical estimates unstable."
                ),
                evidence={"column_count": len(profiles), "row_count": row_count},
                recommendation=(
                    "Apply dimensionality reduction (PCA), aggressive feature selection, "
                    "or collect more rows before modelling."
                ),
                score_impact=5.0,
            )
        )

    if row_count < 100:
        issues.append(
            _issue(
                code="small_sample::rows",
                severity="medium" if row_count < 50 else "low",
                category="small_sample",
                title=f"Only {row_count:,} rows available",
                description=(
                    f"{row_count:,} rows is a small sample. Cross-validated estimates will "
                    "have wide confidence intervals and results will be sensitive to the "
                    "train/test split."
                ),
                evidence={"row_count": row_count, "column_count": len(profiles)},
                recommendation=(
                    "Use k-fold cross-validation rather than a single split, report "
                    "confidence intervals, and avoid drawing strong conclusions from small "
                    "differences in score."
                ),
                score_impact=6.0,
            )
        )
    return issues


def _pii_issues(profiles: list[ColumnProfile]) -> list[QualityIssue]:
    issues: list[QualityIssue] = []
    for profile in profiles:
        if _PII_NAME_HINT.search(profile.name or ""):
            issues.append(
                _issue(
                    code=f"identifiers::pii::{profile.name}",
                    severity="high",
                    category="identifiers",
                    column=profile.name,
                    title=f"'{profile.name}' may contain personally identifiable information",
                    description=(
                        f"The column name '{profile.name}' matches common PII patterns. "
                        "If these are real values, they can create privacy and compliance "
                        "risk when the dataset is shared or logged."
                    ),
                    evidence={"column_name": profile.name},
                    recommendation=(
                        f"Hash, tokenise or drop '{profile.name}' before sharing the "
                        "dataset, and confirm you have a lawful basis to process it."
                    ),
                    score_impact=0.0,
                )
            )
    return issues


def detect_duplicate_columns(frame: pd.DataFrame, profiles: list[ColumnProfile]) -> list[QualityIssue]:
    """Flag columns that are byte-for-byte copies of another column."""
    from app.services.correlations import find_duplicate_columns

    issues: list[QualityIssue] = []
    for a, b in find_duplicate_columns(frame, profiles):
        issues.append(
            _issue(
                code=f"duplicate_column::{a}",
                severity="medium",
                category="duplicate_column",
                column=a,
                title=f"Columns '{a}' and '{b}' contain identical data",
                description=(
                    f"'{a}' and '{b}' hold the same value in every one of the "
                    f"{len(frame):,} rows. One is redundant and may also be a target "
                    "leakage path if either column is the label."
                ),
                evidence={"duplicate_of": b, "row_count": int(len(frame))},
                recommendation=f"Drop '{a}' or '{b}' and keep a single source of truth.",
                score_impact=3.0,
            )
        )
    return issues


def detect_placeholder_categories(profiles: list[ColumnProfile]) -> list[QualityIssue]:
    """Flag categorical columns where the most frequent value is a placeholder."""
    issues: list[QualityIssue] = []
    for profile in profiles:
        if not profile.top_categories or profile.inferred_kind not in (
            ColumnKind.CATEGORICAL,
            ColumnKind.TEXT,
        ):
            continue
        top = profile.top_categories[0]
        if top.value.strip().lower() in PLACEHOLDER_VALUES and top.percentage >= 20:
            issues.append(
                _issue(
                    code=f"consistency::placeholder::{profile.name}",
                    severity="low",
                    category="consistency",
                    column=profile.name,
                    title=f"'{profile.name}' is dominated by the placeholder '{top.value}'",
                    description=(
                        f"{top.percentage:.1f}% of '{profile.name}' is the placeholder value "
                        f"'{top.value}', which is rarely the real category."
                    ),
                    evidence={"value": top.value, "percentage": top.percentage},
                    recommendation=(
                        f"Treat '{top.value}' as a missing-value bucket rather than a real "
                        "category."
                    ),
                    score_impact=1.5,
                )
            )
    return issues