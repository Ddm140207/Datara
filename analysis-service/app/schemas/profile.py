from __future__ import annotations

from enum import Enum

from pydantic import BaseModel, Field


class ColumnKind(str, Enum):
    NUMERIC = "numeric"
    INTEGER = "integer"
    BOOLEAN = "boolean"
    CATEGORICAL = "categorical"
    TEXT = "text"
    DATETIME = "datetime"
    CONSTANT = "constant"
    UNKNOWN = "unknown"


class IssueSeverity(str, Enum):
    INFO = "info"
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"
    CRITICAL = "critical"


class IssueCategory(str, Enum):
    MISSING_VALUES = "missing_values"
    DUPLICATES = "duplicates"
    TYPE_VALIDITY = "type_validity"
    CONSISTENCY = "consistency"
    OUTLIERS = "outliers"
    CARDINALITY = "cardinality"
    CONSTANT_COLUMN = "constant_column"
    LEAKAGE = "leakage"
    CLASS_IMBALANCE = "class_imbalance"
    HIGH_DIMENSIONALITY = "high_dimensionality"
    SMALL_SAMPLE = "small_sample"
    DISTRIBUTION = "distribution"
    IDENTIFIERS = "identifiers"
    DUPLICATE_COLUMN = "duplicate_column"


class ConfidenceBand(str, Enum):
    HIGH = "high"
    MEDIUM = "medium"
    LOW = "low"


class NumericSummary(BaseModel):
    mean: float | None = None
    median: float | None = None
    std: float | None = None
    min: float | None = None
    max: float | None = None
    q1: float | None = None
    q3: float | None = None
    p05: float | None = None
    p25: float | None = None
    p75: float | None = None
    p95: float | None = None
    zeros_count: int = 0
    negative_count: int = 0
    skewness: float | None = None
    kurtosis: float | None = None
    sum: float | None = None
    variance: float | None = None
    range: float | None = None
    coefficient_of_variation: float | None = None


class CategoryFrequency(BaseModel):
    value: str
    count: int
    percentage: float


class OutlierSummary(BaseModel):
    method: str
    lower_bound: float | None = None
    upper_bound: float | None = None
    count: int = 0
    percentage: float = 0.0
    confidence: ConfidenceBand = ConfidenceBand.MEDIUM


class Anomaly(BaseModel):
    column: str
    kind: str
    description: str
    severity: IssueSeverity = IssueSeverity.MEDIUM
    evidence: dict = Field(default_factory=dict)


class NumericHistogram(BaseModel):
    bin_edges: list[float]
    counts: list[int]
    density: bool = False


class ColumnProfile(BaseModel):
    name: str
    ordinal: int
    inferred_kind: ColumnKind
    pandas_dtype: str
    non_null_count: int
    null_count: int
    missing_percentage: float
    unique_count: int
    cardinality_ratio: float
    is_constant: bool
    memory_size_bytes: int | None = None
    numeric: NumericSummary | None = None
    top_categories: list[CategoryFrequency] = Field(default_factory=list)
    category_percentage_covered: float = 0.0
    histogram: NumericHistogram | None = None
    outliers: list[OutlierSummary] = Field(default_factory=list)
    leading_whitespace_count: int = 0
    mixed_type_count: int = 0
    date_min: str | None = None
    date_max: str | None = None
    sample_values: list[str] = Field(default_factory=list)
    notes: list[str] = Field(default_factory=list)


class CorrelationPair(BaseModel):
    column_a: str
    column_b: str
    coefficient: float
    method: str


class CorrelationMatrix(BaseModel):
    method: str
    columns: list[str]
    values: list[list[float | None]]
    pairs: list[CorrelationPair] = Field(default_factory=list)
    note: str | None = None


class QualityIssue(BaseModel):
    code: str
    severity: IssueSeverity
    category: IssueCategory
    column: str | None = None
    title: str
    description: str
    evidence: dict = Field(default_factory=dict)
    recommendation: str
    score_impact: float = 0.0


class ScoreComponent(BaseModel):
    key: str
    label: str
    score: float
    weight: float
    rationale: str
    penalties: list[dict] = Field(default_factory=list)


class ClassBalance(BaseModel):
    column: str
    distinct_classes: int
    minority_percentage: float
    majority_percentage: float
    imbalance_ratio: float | None = None
    is_imbalanced: bool = False


class LeakageSignal(BaseModel):
    column: str
    signal: str
    severity: IssueSeverity
    rationale: str
    evidence: dict = Field(default_factory=dict)


class MlReadinessScore(BaseModel):
    overall: float
    grade: str
    verdict: str
    components: list[ScoreComponent] = Field(default_factory=list)
    data_quality_score: float = 0.0
    class_balance: ClassBalance | None = None
    leakage_signals: list[LeakageSignal] = Field(default_factory=list)
    strengths: list[str] = Field(default_factory=list)
    risks: list[str] = Field(default_factory=list)
    # Explicit reasons the overall score was capped regardless of weighted average.
    gates: list[str] = Field(default_factory=list)


class SampleRows(BaseModel):
    columns: list[str] = Field(default_factory=list)
    rows: list[list[str]] = Field(default_factory=list)
    truncated: bool = False


class DatasetProfile(BaseModel):
    row_count: int
    column_count: int
    total_cells: int
    total_missing: int
    overall_missing_percentage: float
    duplicate_row_count: int
    duplicate_percentage: float
    fully_empty_columns: list[str] = Field(default_factory=list)
    memory_usage_bytes: int = 0
    ingested_at_utc: str
    warnings: list[str] = Field(default_factory=list)
    parse_errors: list[str] = Field(default_factory=list)
    columns: list[ColumnProfile] = Field(default_factory=list)
    correlations: CorrelationMatrix | None = None
    anomalies: list[Anomaly] = Field(default_factory=list)
    quality_issues: list[QualityIssue] = Field(default_factory=list)
    ml_readiness: MlReadinessScore | None = None
    samples: SampleRows | None = None
    target_candidates: list[str] = Field(default_factory=list)


class ProfileRequest(BaseModel):
    """Options the .NET API can pass through to tune an analysis run."""

    include_samples: bool = True
    include_correlations: bool = True
    detect_outliers: bool = True
    sample_row_limit: int | None = None
    target_column: str | None = None


class HealthResponse(BaseModel):
    status: str
    service: str
    version: str
    pandas_version: str
    numpy_version: str
    sklearn_version: str
    uptime_seconds: float