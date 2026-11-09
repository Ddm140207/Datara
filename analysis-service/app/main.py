from __future__ import annotations

import logging
import time

from fastapi import FastAPI, File, Form, HTTPException, UploadFile, status
from fastapi.responses import JSONResponse

from app.core.config import settings
from app.core.logging_config import configure_logging
from app.schemas.profile import (
    DatasetProfile,
    HealthResponse,
    ProfileRequest,
)
from app.services.analyzer import profile_dataset
from app.services.ingestion import DatasetTooLargeError, UnsupportedFileError

configure_logging()
logger = logging.getLogger(__name__)

STARTED_AT = time.perf_counter()

app = FastAPI(
    title="Datara Analysis Service",
    version="1.0.0",
    description=(
        "Deterministic dataset profiling, data quality detection and ML readiness "
        "scoring. Invoked by the Datara .NET API; not intended for direct end-user use."
    ),
    docs_url="/docs",
    openapi_url="/openapi.json",
)


def _error(code: str, message: str, status_code: int, **extra) -> JSONResponse:
    payload = {"error": {"code": code, "message": message}}
    if extra:
        payload["error"].update(extra)
    return JSONResponse(status_code=status_code, content=payload)


@app.exception_handler(DatasetTooLargeError)
async def _too_large_handler(_request, exc: DatasetTooLargeError) -> JSONResponse:
    return _error("dataset_too_large", str(exc), status.HTTP_413_REQUEST_ENTITY_TOO_LARGE)


@app.exception_handler(UnsupportedFileError)
async def _unsupported_handler(_request, exc: UnsupportedFileError) -> JSONResponse:
    return _error("unsupported_file", str(exc), status.HTTP_422_UNPROCESSABLE_ENTITY)


@app.get("/health", response_model=HealthResponse, tags=["system"])
async def health() -> HealthResponse:
    import numpy
    import pandas
    import sklearn

    return HealthResponse(
        status="ok",
        service=settings.service_name,
        version=app.version,
        pandas_version=pandas.__version__,
        numpy_version=numpy.__version__,
        sklearn_version=sklearn.__version__,
        uptime_seconds=round(time.perf_counter() - STARTED_AT, 3),
    )


@app.post(
    "/api/v1/profile",
    response_model=DatasetProfile,
    tags=["analysis"],
    summary="Profile an uploaded CSV and score its ML readiness",
)
async def profile(
    file: UploadFile = File(..., description="CSV or TSV payload"),
    include_samples: bool = Form(default=True),
    include_correlations: bool = Form(default=True),
    detect_outliers: bool = Form(default=True),
    target_column: str | None = Form(default=None),
) -> DatasetProfile:
    # 1) Enforce the size ceiling while streaming so a hostile upload cannot
    #    force the process to buffer an unbounded amount of memory.
    chunks: list[bytes] = []
    total = 0
    while True:
        chunk = await file.read(1024 * 1024)
        if not chunk:
            break
        total += len(chunk)
        if total > settings.max_upload_bytes:
            raise HTTPException(
                status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                detail=(
                    f"Upload exceeds the {settings.max_upload_bytes // (1024 * 1024)} MB limit."
                ),
            )
        chunks.append(chunk)

    payload = b"".join(chunks)
    if not payload:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="The uploaded file is empty.",
        )

    logger.info(
        "Received upload: filename=%r declared_bytes=%s",
        file.filename or "<none>",
        total,
    )

    request = ProfileRequest(
        include_samples=include_samples,
        include_correlations=include_correlations,
        detect_outliers=detect_outliers,
        target_column=target_column,
    )

    try:
        return await _run_in_threadpool(payload, request)
    except UnsupportedFileError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail=str(exc)
        ) from exc
    except DatasetTooLargeError as exc:
        raise HTTPException(
            status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE, detail=str(exc)
        ) from exc


async def _run_in_threadpool(payload: bytes, request: ProfileRequest) -> DatasetProfile:
    """pandas releases the GIL intermittently but this is CPU-bound and blocking."""
    from starlette.concurrency import run_in_threadpool

    return await run_in_threadpool(profile_dataset, payload, request)


@app.get("/api/v1/limits", tags=["system"])
async def limits() -> dict:
    return {
        "max_upload_bytes": settings.max_upload_bytes,
        "max_rows": settings.max_rows,
        "max_columns": settings.max_columns,
        "sample_row_limit": settings.sample_row_limit,
        "accepted_media_types": ["text/csv", "application/csv", "text/tab-separated-values"],
    }