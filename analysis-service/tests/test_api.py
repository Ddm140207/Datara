"""Contract tests for the FastAPI surface exposed to the .NET API."""

from __future__ import annotations

import io

import pytest
from fastapi.testclient import TestClient

from app.main import app

client = TestClient(app)

VALID_CSV = b"age,income,city,is_active\n34,72000,Lisbon,True\n41,55000,Porto,False\n29,91000,Lisbon,True\n"


def test_health_reports_dependency_versions() -> None:
    response = client.get("/health")
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["pandas_version"]
    assert body["sklearn_version"]


def test_profile_returns_a_complete_payload() -> None:
    response = client.post(
        "/api/v1/profile",
        files={"file": ("customers.csv", io.BytesIO(VALID_CSV), "text/csv")},
    )
    assert response.status_code == 200
    body = response.json()

    assert body["row_count"] == 3
    assert body["column_count"] == 4
    assert len(body["columns"]) == 4
    assert body["ml_readiness"]["overall"] > 0
    assert body["ml_readiness"]["components"]
    assert "columns" in body["samples"]


def test_empty_upload_returns_422() -> None:
    response = client.post(
        "/api/v1/profile",
        files={"file": ("empty.csv", io.BytesIO(b""), "text/csv")},
    )
    assert response.status_code == 422


def test_binary_upload_returns_422() -> None:
    response = client.post(
        "/api/v1/profile",
        files={"file": ("image.png", io.BytesIO(b"\x89PNG\r\n\x1a\n\x00\x00"), "image/png")},
    )
    assert response.status_code == 422


def test_correlations_can_be_disabled() -> None:
    response = client.post(
        "/api/v1/profile",
        files={"file": ("customers.csv", io.BytesIO(VALID_CSV), "text/csv")},
        data={"include_correlations": "false"},
    )
    assert response.status_code == 200
    assert response.json()["correlations"] is None


def test_samples_can_be_disabled() -> None:
    response = client.post(
        "/api/v1/profile",
        files={"file": ("customers.csv", io.BytesIO(VALID_CSV), "text/csv")},
        data={"include_samples": "false"},
    )
    assert response.status_code == 200
    assert response.json()["samples"] is None


def test_malformed_csv_is_salvaged_with_a_warning() -> None:
    payload = b"a,b,c\n1,2,3\n4,5\n6,7,8,9\n"
    response = client.post(
        "/api/v1/profile",
        files={"file": ("ragged.csv", io.BytesIO(payload), "text/csv")},
    )
    assert response.status_code == 200
    body = response.json()
    assert body["parse_errors"]
    assert any("Malformed rows" in w for w in body["warnings"])


def test_openapi_document_is_served() -> None:
    response = client.get("/openapi.json")
    assert response.status_code == 200
    assert "/api/v1/profile" in response.json()["paths"]


@pytest.mark.parametrize("path", ["/health", "/api/v1/limits"])
def test_system_endpoints_are_reachable(path: str) -> None:
    assert client.get(path).status_code == 200