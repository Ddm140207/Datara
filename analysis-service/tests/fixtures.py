"""Small, reproducible fixtures for the analysis-service test-suite."""

from __future__ import annotations

import numpy as np
import pandas as pd


def clean_frame(rows: int = 120, seed: int = 7) -> pd.DataFrame:
    """A well-formed dataset: no missing values, no duplicates, healthy numerics."""
    rng = np.random.default_rng(seed)
    return pd.DataFrame(
        {
            "record_id": np.arange(1000, 1000 + rows),
            "age": rng.integers(18, 80, rows).astype("float64"),
            "income": rng.normal(65_000, 14_000, rows).round(2),
            "plan_type": rng.choice(["Basic", "Standard", "Premium"], rows, p=[0.4, 0.4, 0.2]),
            "is_active": rng.choice([True, False], rows, p=[0.75, 0.25]),
            "signup_date": pd.date_range("2024-01-01", periods=rows, freq="7D").astype(str),
            "notes": [f"record note {i}" for i in range(rows)],
        }
    )


def messy_frame(rows: int = 200, seed: int = 11) -> pd.DataFrame:
    """Every known quality problem in one frame."""
    rng = np.random.default_rng(seed)
    frame = pd.DataFrame(
        {
            "customer_id": [f"C{i:05d}" for i in range(rows)],
            "income": rng.normal(60_000, 12_000, rows).round(2),
            "score": rng.integers(0, 100, rows).astype("float64"),
            "segment": rng.choice(["a", "b", "c", "d"], rows),
            "churned": rng.choice([0, 1], rows, p=[0.86, 0.14]),
            "constant_col": ["same"] * rows,
            "empty_col": [None] * rows,
        }
    )

    # 35% missing in income
    frame.loc[rng.random(rows) < 0.35, "income"] = np.nan
    # 12% missing in score
    frame.loc[rng.random(rows) < 0.12, "score"] = np.nan
    # text markers that must be treated as missing
    frame.loc[rng.random(rows) < 0.08, "segment"] = "N/A"
    # whitespace padding
    frame.loc[rng.random(rows) < 0.06, "segment"] = frame.loc[rng.random(rows) < 0.06, "segment"].map(
        lambda v: f" {v} " if isinstance(v, str) else v
    )
    # extreme outliers in score
    outlier_idx = rng.choice(rows, size=4, replace=False)
    frame.loc[outlier_idx, "score"] = [5000.0, -3200.0, 4100.0, 4800.0]
    # duplicated rows
    dupes = frame.head(12).copy()
    frame = pd.concat([frame, dupes], ignore_index=True)
    return frame


def leaky_frame(rows: int = 150, seed: int = 3) -> pd.DataFrame:
    """A frame where one feature is a copy of the target."""
    rng = np.random.default_rng(seed)
    target = rng.choice([0, 1], rows, p=[0.8, 0.2])
    return pd.DataFrame(
        {
            "feature_a": rng.normal(0, 1, rows).round(4),
            "feature_b": rng.normal(0, 1, rows).round(4),
            "churned": target,
            "account_closed_flag": target.astype(float),  # post-outcome event
        }
    )


def frame_to_csv(frame: pd.DataFrame) -> bytes:
    return frame.to_csv(index=False).encode("utf-8")