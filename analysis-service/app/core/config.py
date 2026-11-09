from __future__ import annotations

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Runtime configuration for the analysis service."""

    model_config = SettingsConfigDict(env_prefix="ANALYSIS_", env_file=".env", extra="ignore")

    service_name: str = "datara-analysis"
    log_level: str = "INFO"

    # Hard safety rails. The .NET API validates first, but this service must never
    # be the only line of defence against a hostile upload.
    max_upload_bytes: int = 200 * 1024 * 1024
    max_rows: int = 2_000_000
    max_columns: int = 2_000

    # Row count above which we stop materialising per-column frequency tables.
    full_categorical_profile_row_limit: int = 250_000

    # Number of rows handed to the AI context builder as representative samples.
    sample_row_limit: int = 25

    # Outlier detection configuration.
    iqr_multiplier: float = 1.5
    zscore_threshold: float = 3.5


@lru_cache
def get_settings() -> Settings:
    return Settings()


settings = get_settings()