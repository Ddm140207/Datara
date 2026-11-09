from __future__ import annotations

import pandas as pd

from app.schemas.profile import (
    ClassBalance,
    ColumnKind,
    ColumnProfile,
    CorrelationMatrix,
    LeakageSignal,
    MlReadinessScore,
    ScoreComponent,
)
from app.services.type_inference import looks_like_target

from app.services.correlations import find_target_leakage

# Relative weights. They are normalised at scoring time, so they express intent
# rather than having to sum to exactly 1.0.
COMPONENT_WEIGHTS: dict[str, float] = {
    "completeness": 0.22,
    "validity": 0.18,
    "consistency": 0.15,
    "uniqueness": 0.12,
    "outliers": 0.10,
    "feature_quality": 0.13,
    "cardinality": 0.05,
    "leakage": 0.05,
}

# Leakage never reports below this, so one bad column cannot zero the dimension.
LEAKAGE_SCORE_FLOOR = 25.0

# A leaked target produces a model that looks excellent offline and fails in
# production, so it is treated as a gating failure rather than a small deduction.
READINESS_CAP_ON_HIGH_LEAKAGE = 45.0
READINESS_CAP_ON_CRITICAL_ISSUE = 60.0
READINESS_CAP_ON_SMALL_SAMPLE = 70.0

GRADE_THRESHOLDS = (
    (90, "A"),
    (80, "B"),
    (70, "C"),
    (60, "D"),
    (0, "F"),
)

VERDICTS = (
    (85, "Ready for machine learning with standard preprocessing."),
    (70, "Usable after targeted cleanup. Address the high severity issues first."),
    (55, "Workable, but meaningful preprocessing is required before modelling."),
    (40, "Significant data quality problems. Clean the dataset before investing in models."),
    (0, "Not ready for machine learning. Substantial cleaning and re-collection needed."),
)


def _grade(score: float) -> str:
    for threshold, letter in GRADE_THRESHOLDS:
        if score >= threshold:
            return letter
    return "F"


def _verdict(score: float) -> str:
    for threshold, text in VERDICTS:
        if score >= threshold:
            return text
    return VERDICTS[-1][1]


def _clamp(value: float) -> float:
    return max(0.0, min(100.0, value))


def _penalty(points: float, reason: str, detail: str, evidence: dict | None = None) -> dict:
    return {"points": round(points, 3), "reason": reason, "detail": detail, "evidence": evidence or {}}


def _score_completeness(profiles: list[ColumnProfile], row_count: int) -> ScoreComponent:
    penalties: list[dict] = []
    score = 100.0

    for profile in profiles:
        pct = profile.missing_percentage
        if pct <= 1:
            continue
        if profile.non_null_count == 0:
            loss = 100.0 / max(1, len(profiles))
            penalties.append(
                _penalty(loss, "empty_column", f"'{profile.name}' is entirely empty.")
            )
            continue
        # Cost is proportional but sublinear, so a 60%-empty column does not
        # destroy the whole score on its own.
        loss = (pct / 100.0) ** 0.7 * 100.0 / max(1, len(profiles))
        penalties.append(
            _penalty(
                loss,
                "missing_values",
                f"'{profile.name}' is {pct:.1f}% missing.",
                {"column": profile.name, "missing_percentage": pct},
            )
        )

    score = _clamp(score - sum(p["points"] for p in penalties))
    total_missing = sum(p.null_count for p in profiles)
    detail = (
        f"{total_missing:,} empty cells out of {row_count * max(1, len(profiles)):,} "
        f"({len(profiles)} columns x {row_count:,} rows)."
        if penalties
        else f"No missing values across {len(profiles)} columns."
    )
    return ScoreComponent(
        key="completeness",
        label="Completeness",
        score=round(score, 2),
        weight=COMPONENT_WEIGHTS["completeness"],
        rationale=detail,
        penalties=penalties,
    )


def _score_validity(profiles: list[ColumnProfile]) -> ScoreComponent:
    penalties: list[dict] = []
    score = 100.0

    for profile in profiles:
        if profile.inferred_kind == ColumnKind.UNKNOWN:
            penalties.append(
                _penalty(
                    100.0 / max(1, len(profiles)) * 1.5,
                    "undetermined_type",
                    f"'{profile.name}' has a type that could not be resolved.",
                    {"column": profile.name},
                )
            )
        if profile.mixed_type_count:
            ratio = profile.mixed_type_count / max(1, profile.non_null_count)
            penalties.append(
                _penalty(
                    min(20.0, ratio * 100.0),
                    "mixed_types",
                    f"'{profile.name}' mixes {profile.mixed_type_count:,} inconsistent values.",
                    {"column": profile.name, "mixed_type_count": profile.mixed_type_count},
                )
            )
        if profile.inferred_kind in (ColumnKind.NUMERIC, ColumnKind.INTEGER) and profile.numeric:
            numeric = profile.numeric
            if numeric.std is not None and numeric.std == 0 and profile.unique_count == 1:
                penalties.append(
                    _penalty(
                        100.0 / max(1, len(profiles)),
                        "zero_variance",
                        f"'{profile.name}' has zero variance.",
                        {"column": profile.name},
                    )
                )
            if numeric.range is not None and numeric.min is not None and numeric.min < 0:
                # Negative counts where negatives are implausible are a classic sign
                # of an encoding error, but the domain is unknown so this stays light.
                penalties.append(
                    _penalty(
                        1.5,
                        "negative_values",
                        f"'{profile.name}' contains {numeric.negative_count:,} negative values.",
                        {"column": profile.name, "negative_count": numeric.negative_count},
                    )
                )

    score = _clamp(score - sum(p["points"] for p in penalties))
    return ScoreComponent(
        key="validity",
        label="Validity",
        score=round(score, 2),
        weight=COMPONENT_WEIGHTS["validity"],
        rationale=(
            "All column types resolved cleanly."
            if not penalties
            else f"{len(penalties)} validity concern(s) detected."
        ),
        penalties=penalties,
    )


def _score_consistency(profiles: list[ColumnProfile], row_count: int) -> ScoreComponent:
    penalties: list[dict] = []
    score = 100.0

    for profile in profiles:
        if profile.leading_whitespace_count:
            ratio = profile.leading_whitespace_count / max(1, profile.non_null_count)
            penalties.append(
                _penalty(
                    min(10.0, ratio * 100.0 * 0.5),
                    "whitespace",
                    f"'{profile.name}' has whitespace-padded values.",
                    {"column": profile.name},
                )
            )
        if profile.top_categories and profile.inferred_kind == ColumnKind.CATEGORICAL:
            covered = profile.category_percentage_covered
            if covered < 99.0 and profile.unique_count > 20:
                # A long tail is normal; an extreme one usually signals dirty categories.
                penalties.append(
                    _penalty(
                        min(8.0, (100.0 - covered) / 12.0),
                        "long_tail_categories",
                        (
                            f"'{profile.name}' has {profile.unique_count:,} categories; the "
                            f"top values cover only {covered:.1f}% of rows."
                        ),
                        {"column": profile.name, "unique_count": profile.unique_count},
                    )
                )

    if row_count < 50:
        penalties.append(
            _penalty(
                10.0,
                "small_sample",
                f"Only {row_count:,} rows to establish consistency across.",
                {"row_count": row_count},
            )
        )

    score = _clamp(score - sum(p["points"] for p in penalties))
    return ScoreComponent(
        key="consistency",
        label="Consistency",
        score=round(score, 2),
        weight=COMPONENT_WEIGHTS["consistency"],
        rationale=(
            "Values follow consistent formatting across columns."
            if not penalties
            else f"{len(penalties)} consistency concern(s) detected."
        ),
        penalties=penalties,
    )


def _score_uniqueness(row_count: int, duplicate_row_count: int) -> ScoreComponent:
    score = 100.0
    penalties: list[dict] = []
    pct = 100.0 * duplicate_row_count / row_count if row_count else 0.0
    if duplicate_row_count:
        penalties.append(
            _penalty(
                pct,
                "duplicate_rows",
                f"{duplicate_row_count:,} duplicate rows ({pct:.1f}%).",
                {"duplicate_rows": duplicate_row_count, "duplicate_percentage": round(pct, 4)},
            )
        )
    return ScoreComponent(
        key="uniqueness",
        label="Uniqueness",
        score=round(_clamp(score - sum(p["points"] for p in penalties)), 2),
        weight=COMPONENT_WEIGHTS["uniqueness"],
        rationale=(
            "Every row is distinct."
            if not duplicate_row_count
            else f"{pct:.1f}% of rows are duplicates."
        ),
        penalties=penalties,
    )


def _score_outliers(profiles: list[ColumnProfile]) -> ScoreComponent:
    penalties: list[dict] = []
    score = 100.0
    numeric_columns = [
        p for p in profiles if p.inferred_kind in (ColumnKind.NUMERIC, ColumnKind.INTEGER)
    ]
    if not numeric_columns:
        return ScoreComponent(
            key="outliers",
            label="Outliers",
            score=100.0,
            weight=COMPONENT_WEIGHTS["outliers"],
            rationale="No numeric columns to assess for outliers.",
            penalties=[],
        )

    for profile in numeric_columns:
        primary = next((o for o in profile.outliers if o.method == "IQR"), None)
        if primary is None or primary.count == 0:
            continue
        # Saturating penalty: the first 10% of outliers matters, the next 40% much less.
        loss = min(100.0, (primary.percentage / 10.0) * (100.0 / len(numeric_columns)) * 1.6)
        penalties.append(
            _penalty(
                loss,
                "outliers_iqr",
                f"'{profile.name}' has {primary.percentage:.1f}% values outside 1.5xIQR.",
                {"column": profile.name, "percentage": primary.percentage},
            )
        )

    score = _clamp(score - sum(p["points"] for p in penalties))
    return ScoreComponent(
        key="outliers",
        label="Outliers",
        score=round(score, 2),
        weight=COMPONENT_WEIGHTS["outliers"],
        rationale=(
            f"No significant outliers across {len(numeric_columns)} numeric column(s)."
            if not penalties
            else f"Outliers found in {len(penalties)} of {len(numeric_columns)} numeric columns."
        ),
        penalties=penalties,
    )


def _score_feature_quality(profiles: list[ColumnProfile], row_count: int) -> ScoreComponent:
    """Penalise columns that cannot be used as features without any transformation.

    Uniqueness alone is not a defect: continuous measurements are unique per row by
    nature. Only natural keys (unique strings / id-named integers) and unhandled long
    free text are penalised here.
    """
    from app.services.type_inference import looks_like_identifier

    penalties: list[dict] = []
    score = 100.0
    denom = max(1, len(profiles))

    for profile in profiles:
        if profile.is_constant:
            penalties.append(
                _penalty(
                    100.0 / denom * 2.0,
                    "constant_column",
                    f"'{profile.name}' is constant and cannot be a useful feature.",
                    {"column": profile.name},
                )
            )
            continue

        if profile.non_null_count and looks_like_identifier(
            profile.name,
            profile.inferred_kind,
            profile.non_null_count,
            profile.unique_count,
        ):
            penalties.append(
                _penalty(
                    min(22.0, 100.0 / denom * 1.2),
                    "identifier_column",
                    f"'{profile.name}' is unique per row and behaves like a key, so it "
                    "will not generalise as a feature.",
                    {"column": profile.name, "cardinality_ratio": profile.cardinality_ratio},
                )
            )
            continue

        if profile.inferred_kind == ColumnKind.TEXT and profile.unique_count > 500:
            penalties.append(
                _penalty(
                    100.0 / denom,
                    "unhandled_text",
                    f"'{profile.name}' is long free text that needs vectorisation.",
                    {"column": profile.name, "unique_count": profile.unique_count},
                )
            )

    if row_count and len(profiles) / row_count > 0.5:
        penalties.append(
            _penalty(
                8.0,
                "wide_dataset",
                f"{len(profiles):,} columns against {row_count:,} rows is a wide dataset.",
                {"column_count": len(profiles), "row_count": row_count},
            )
        )

    score = _clamp(score - sum(p["points"] for p in penalties))
    return ScoreComponent(
        key="feature_quality",
        label="Feature Quality",
        score=round(score, 2),
        weight=COMPONENT_WEIGHTS["feature_quality"],
        rationale=(
            "All columns can plausibly be used as features."
            if not penalties
            else f"{len(penalties)} column(s) are unsuitable as-is features."
        ),
        penalties=penalties,
    )


def _score_cardinality(profiles: list[ColumnProfile]) -> ScoreComponent:
    penalties: list[dict] = []
    score = 100.0
    for profile in profiles:
        if profile.inferred_kind == ColumnKind.CATEGORICAL and profile.unique_count > 100:
            losses = 100.0 / max(1, len(profiles)) * min(3.0, profile.unique_count / 100.0)
            penalties.append(
                _penalty(
                    losses,
                    "high_cardinality",
                    f"'{profile.name}' has {profile.unique_count:,} categorical levels.",
                    {"column": profile.name, "unique_count": profile.unique_count},
                )
            )
    score = _clamp(score - sum(p["points"] for p in penalties))
    return ScoreComponent(
        key="cardinality",
        label="Cardinality",
        score=round(score, 2),
        weight=COMPONENT_WEIGHTS["cardinality"],
        rationale=(
            "Categorical columns have manageable cardinality."
            if not penalties
            else f"{len(penalties)} categorical column(s) exceed a workable number of levels."
        ),
        penalties=penalties,
    )


def _detect_leakage(
    frame: pd.DataFrame, profiles: list[ColumnProfile], correlations: CorrelationMatrix | None
) -> list[LeakageSignal]:
    raw = find_target_leakage(frame, profiles, correlations)
    signals: list[LeakageSignal] = []
    for item in raw:
        signals.append(
            LeakageSignal(
                column=item["column"],
                signal=item["signal"],
                severity=item["severity"],
                rationale=item["rationale"],
                evidence=item.get("evidence", {}),
            )
        )
    return signals


def _score_leakage(signals: list[LeakageSignal]) -> ScoreComponent:
    """Leakage is the one axis where a single serious find invalidates everything.

    Penalties are capped and the component has a floor so the dimension reads as
    "severe risk" rather than collapsing to zero and swamping the weighted average.
    """
    penalties: list[dict] = []
    weights = {"high": 25.0, "medium": 12.0, "low": 3.0, "info": 1.0}
    for signal in signals:
        penalties.append(
            _penalty(
                weights.get(signal.severity.value, 3.0),
                signal.signal,
                f"'{signal.column}': {signal.rationale}",
                {"column": signal.column, "evidence": signal.evidence},
            )
        )

    total = min(75.0, sum(p["points"] for p in penalties))
    score = _clamp(max(LEAKAGE_SCORE_FLOOR, 100.0 - total))
    return ScoreComponent(
        key="leakage",
        label="Target Leakage Risk",
        score=round(score, 2),
        weight=COMPONENT_WEIGHTS["leakage"],
        rationale=(
            "No target leakage signals detected."
            if not penalties
            else f"{len(signals)} leakage signal(s) detected; penalty capped so this "
            "dimension does not dominate the overall score."
        ),
        penalties=penalties,
    )


def _analyse_class_balance(
    frame: pd.DataFrame, profiles: list[ColumnProfile], target_column: str | None
) -> ClassBalance | None:
    if target_column and target_column in frame.columns:
        column, series = target_column, frame[target_column]
    else:
        candidates = [
            p
            for p in profiles
            if looks_like_target(p.name, p.inferred_kind, p.unique_count) and p.non_null_count > 0
        ]
        if not candidates:
            return None
        # Prefer the most balanced-looking candidate so the score reflects the
        # dataset's clearest classification target rather than a numeric flag.
        scored: list[tuple[float, str]] = []
        for candidate in candidates:
            series_c = frame[candidate.name].dropna()
            if len(series_c) < 10:
                continue
            counts = series_c.value_counts()
            if len(counts) < 2:
                continue
            minority = counts.iloc[-1] / counts.sum()
            scored.append((minority, candidate.name))
        if not scored:
            return None
        scored.sort()
        column = scored[len(scored) // 2][1]
        series = frame[column]

    counts = series.value_counts(dropna=True)
    total = float(counts.sum())
    if total == 0 or len(counts) < 2:
        return None

    majority = float(counts.iloc[0]) / total * 100
    minority = float(counts.iloc[-1]) / total * 100
    ratio = float(counts.iloc[0] / counts.iloc[-1]) if counts.iloc[-1] else None
    return ClassBalance(
        column=column,
        distinct_classes=int(len(counts)),
        minority_percentage=round(minority, 4),
        majority_percentage=round(majority, 4),
        imbalance_ratio=round(ratio, 4) if ratio else None,
        is_imbalanced=minority < 10 or (ratio is not None and ratio > 9),
    )


def _apply_class_balance_penalty(
    components: list[ScoreComponent], balance: ClassBalance | None
) -> None:
    """Fold class imbalance into the completeness component.

    Imbalance is only a genuine readiness problem for supervised classification, so
    rather than inventing a tenth weighted axis we apply it where the data itself is
    deficient: the label distribution.
    """
    if balance is None or not balance.is_imbalanced:
        return
    component = next((c for c in components if c.key == "completeness"), None)
    if component is None:
        return
    penalty = _penalty(
        min(35.0, balance.minority_percentage * 1.5),
        "class_imbalance",
        (
            f"'{balance.column}' has {balance.distinct_classes} classes; the rarest is "
            f"{balance.minority_percentage:.2f}% of rows."
        ),
        {
            "column": balance.column,
            "minority_percentage": balance.minority_percentage,
            "majority_percentage": balance.majority_percentage,
        },
    )
    component.penalties.append(penalty)
    component.score = round(_clamp(component.score - penalty["points"]), 2)
    component.rationale = (
        f"{component.rationale} Class imbalance detected in '{balance.column}'."
    )


def compute_ml_readiness(
    frame: pd.DataFrame,
    profiles: list[ColumnProfile],
    *,
    row_count: int,
    duplicate_row_count: int,
    correlations: CorrelationMatrix | None,
    target_column: str | None = None,
) -> MlReadinessScore:
    leakage_signals = _detect_leakage(frame, profiles, correlations)
    balance = _analyse_class_balance(frame, profiles, target_column)

    components = [
        _score_completeness(profiles, row_count),
        _score_validity(profiles),
        _score_consistency(profiles, row_count),
        _score_uniqueness(row_count, duplicate_row_count),
        _score_outliers(profiles),
        _score_feature_quality(profiles, row_count),
        _score_cardinality(profiles),
        _score_leakage(leakage_signals),
    ]
    _apply_class_balance_penalty(components, balance)

    total_weight = sum(c.weight for c in components)
    overall = sum(c.score * c.weight for c in components) / total_weight
    overall = round(_clamp(overall), 1)

    # Data quality is the deterministic half of the picture: it deliberately
    # excludes leakage, which is a target-aware concern.
    quality_components = [c for c in components if c.key != "leakage"]
    quality_weight = sum(c.weight for c in quality_components)
    quality_score = round(
        _clamp(sum(c.score * c.weight for c in quality_components) / quality_weight), 1
    )

    gates: list[str] = []
    if any(s.severity.value in ("high", "critical") for s in leakage_signals):
        overall = min(overall, READINESS_CAP_ON_HIGH_LEAKAGE)
        gates.append(
            "Capped at "
            f"{READINESS_CAP_ON_HIGH_LEAKAGE:.0f}: high-severity target leakage was "
            "detected. No amount of data cleanliness makes a leaked model trustworthy."
        )
    if row_count < 100:
        overall = min(overall, READINESS_CAP_ON_SMALL_SAMPLE)
        gates.append(
            f"Capped at {READINESS_CAP_ON_SMALL_SAMPLE:.0f}: fewer than 100 rows is not "
            "enough to estimate model performance reliably."
        )

    ranked = sorted(components, key=lambda c: c.score)
    strengths = [f"{c.label} scores {c.score:.0f}%" for c in ranked if c.score >= 90][:4]
    risks = [f"{c.label} scores {c.score:.0f}%" for c in ranked if c.score < 80][:4]

    return MlReadinessScore(
        overall=overall,
        grade=_grade(overall),
        verdict=_verdict(overall),
        components=components,
        data_quality_score=quality_score,
        class_balance=balance,
        leakage_signals=leakage_signals,
        strengths=strengths or ["No dimension scored below 90%."],
        risks=risks or ["No dimension scored below 80%."],
        gates=gates,
    )