"""Tests for the deterministic profiling and scoring pipeline."""

from __future__ import annotations

import pytest

from app.schemas.profile import ColumnKind, IssueSeverity
from app.services.analyzer import profile_dataset
from app.services.ingestion import UnsupportedFileError
from app.services.quality import detect_duplicate_columns
from app.services.profiling import profile_columns
from app.services.scoring import COMPONENT_WEIGHTS
from tests.fixtures import (
    clean_frame,
    frame_to_csv,
    leaky_frame,
    messy_frame,
)


# --------------------------------------------------------------------------- #
# happy path
# --------------------------------------------------------------------------- #
def test_clean_dataset_scores_highly() -> None:
    profile = profile_dataset(frame_to_csv(clean_frame()))
    readiness = profile.ml_readiness

    assert readiness is not None
    assert readiness.overall >= 85
    assert readiness.grade in ("A", "B")
    assert readiness.components[0].score == 100.0  # completeness
    assert readiness.components[3].score == 100.0  # uniqueness


def test_row_and_column_counts_are_correct() -> None:
    frame = clean_frame(rows=57)
    profile = profile_dataset(frame_to_csv(frame))
    assert profile.row_count == 57
    assert profile.column_count == len(frame.columns)
    assert profile.total_cells == 57 * len(frame.columns)


def test_all_component_weights_are_positive_and_sum_to_one() -> None:
    assert all(w > 0 for w in COMPONENT_WEIGHTS.values())
    assert round(sum(COMPONENT_WEIGHTS.values()), 6) == 1.0


def test_every_component_reports_a_score_and_rationale() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    for component in profile.ml_readiness.components:
        assert 0.0 <= component.score <= 100.0
        assert component.rationale.strip()


# --------------------------------------------------------------------------- #
# type inference
# --------------------------------------------------------------------------- #
def test_type_inference_covers_expected_kinds() -> None:
    profile = profile_dataset(frame_to_csv(clean_frame()))
    kinds = {c.name: c.inferred_kind for c in profile.columns}
    assert kinds["age"] == ColumnKind.INTEGER
    assert kinds["income"] == ColumnKind.NUMERIC
    assert kinds["plan_type"] == ColumnKind.CATEGORICAL
    assert kinds["is_active"] == ColumnKind.BOOLEAN
    assert kinds["signup_date"] == ColumnKind.DATETIME
    assert kinds["notes"] == ColumnKind.TEXT


def test_continuous_columns_are_not_flagged_as_identifiers() -> None:
    """A float measured to the cent is unique per row; that is not a defect."""
    profile = profile_dataset(frame_to_csv(clean_frame()))
    identifier_columns = {
        i.column for i in profile.quality_issues if i.category == "identifiers"
    }
    assert "income" not in identifier_columns
    assert "age" not in identifier_columns
    assert "signup_date" not in identifier_columns
    assert "record_id" in identifier_columns


def test_id_named_column_is_flagged_as_identifier() -> None:
    profile = profile_dataset(frame_to_csv(clean_frame()))
    assert any("'record_id'" in i.title for i in profile.quality_issues)


# --------------------------------------------------------------------------- #
# quality issue detection
# --------------------------------------------------------------------------- #
def test_empty_column_is_critical() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    critical = [
        i for i in profile.quality_issues if i.severity == IssueSeverity.CRITICAL
    ]
    assert any(i.column == "empty_col" for i in critical)


def test_constant_column_is_detected() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    assert any("constant" in i.category for i in profile.quality_issues)


def test_duplicate_rows_are_counted() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    assert profile.duplicate_row_count == 12
    assert any(i.category == "duplicates" for i in profile.quality_issues)


def test_outliers_are_detected_on_numeric_columns() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    score_column = next(c for c in profile.columns if c.name == "score")
    assert score_column.outliers, "extreme values must be reported"
    assert any(i.category == "outliers" for i in profile.quality_issues)


def test_every_issue_carries_evidence_and_a_recommendation() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    assert profile.quality_issues
    for issue in profile.quality_issues:
        assert issue.title.strip()
        assert issue.description.strip()
        assert issue.recommendation.strip()
        assert issue.code


def test_issues_are_sorted_by_severity() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    order = {
        IssueSeverity.CRITICAL: 0,
        IssueSeverity.HIGH: 1,
        IssueSeverity.MEDIUM: 2,
        IssueSeverity.LOW: 3,
        IssueSeverity.INFO: 4,
    }
    severities = [order[i.severity] for i in profile.quality_issues]
    assert severities == sorted(severities)


# --------------------------------------------------------------------------- #
# correlations
# --------------------------------------------------------------------------- #
def test_correlation_matrix_is_symmetric_with_unit_diagonal() -> None:
    profile = profile_dataset(frame_to_csv(clean_frame()))
    matrix = profile.correlations
    assert matrix is not None
    for i in range(len(matrix.columns)):
        assert matrix.values[i][i] == pytest.approx(1.0, abs=1e-6)
        for j in range(len(matrix.columns)):
            assert matrix.values[i][j] == pytest.approx(matrix.values[j][i], abs=1e-6)


def test_correlations_omit_non_numeric_columns() -> None:
    profile = profile_dataset(frame_to_csv(clean_frame()))
    assert "plan_type" not in profile.correlations.columns


# --------------------------------------------------------------------------- #
# leakage
# --------------------------------------------------------------------------- #
def test_duplicate_column_is_detected_regardless_of_name() -> None:
    profile = profile_dataset(frame_to_csv(leaky_frame()))
    signals = profile.ml_readiness.leakage_signals
    assert any(s.signal == "duplicate_column" for s in signals)


def test_leakage_caps_the_readiness_score() -> None:
    clean = profile_dataset(frame_to_csv(clean_frame()))
    leaky = profile_dataset(frame_to_csv(leaky_frame()))
    assert leaky.ml_readiness.overall < clean.ml_readiness.overall
    assert leaky.ml_readiness.overall <= 45.0
    assert leaky.ml_readiness.gates, "a capped score must explain the cap"


def test_data_quality_score_stays_high_for_a_leaky_dataset() -> None:
    """The product point: the data can be clean while the experiment is invalid."""
    profile = profile_dataset(frame_to_csv(leaky_frame()))
    assert profile.ml_readiness.data_quality_score > 90


def test_duplicate_column_detection_ignores_partial_overlap() -> None:
    frame = clean_frame(rows=40)
    frame["copy_of_age"] = frame["age"]
    frame.loc[0, "copy_of_age"] = None
    profiles, _ = profile_columns(frame, detect_outliers=False)
    pairs = detect_duplicate_columns(frame, profiles)
    assert all({"copy_of_age", "age"} not in set(p) for p in pairs)


# --------------------------------------------------------------------------- #
# class balance
# --------------------------------------------------------------------------- #
def test_class_imbalance_is_detected_on_a_skewed_target() -> None:
    profile = profile_dataset(frame_to_csv(messy_frame()))
    balance = profile.ml_readiness.class_balance
    assert balance is not None
    assert balance.minority_percentage < balance.majority_percentage


# --------------------------------------------------------------------------- #
# resilience
# --------------------------------------------------------------------------- #
def test_malformed_upload_raises_a_typed_error() -> None:
    with pytest.raises(UnsupportedFileError):
        profile_dataset(b"\x00\x01\x02\x03")


def test_single_column_csv_is_handled() -> None:
    profile = profile_dataset(b"value\n1\n2\n3\n")
    assert profile.row_count == 3
    assert profile.column_count == 1
    assert profile.ml_readiness is not None


def test_wide_dataset_is_flagged() -> None:
    header = ",".join(f"c{i}" for i in range(120))
    rows = "\n".join(",".join(str(i + j) for j in range(120)) for i in range(3))
    profile = profile_dataset(f"{header}\n{rows}\n".encode())
    assert any(i.category == "high_dimensionality" for i in profile.quality_issues)


def test_score_is_deterministic() -> None:
    payload = frame_to_csv(messy_frame())
    first = profile_dataset(payload)
    second = profile_dataset(payload)
    assert first.ml_readiness.overall == second.ml_readiness.overall
    assert [i.code for i in first.quality_issues] == [i.code for i in second.quality_issues]