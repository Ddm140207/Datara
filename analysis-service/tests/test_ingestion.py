"""Tests for hardened CSV ingestion: malformed payloads must fail loudly."""

from __future__ import annotations

import pytest

from app.services.ingestion import (
    DatasetTooLargeError,
    UnsupportedFileError,
    load_csv,
)


def test_parses_clean_csv() -> None:
    result = load_csv(b"a,b,c\n1,x,2\n3,y,4\n")
    assert list(result.frame.columns) == ["a", "b", "c"]
    assert len(result.frame) == 2


def test_detects_semicolon_delimiter() -> None:
    result = load_csv(b"a;b;c\n1;x;2\n3;y;4\n")
    assert result.delimiter == ";"
    assert list(result.frame.columns) == ["a", "b", "c"]


def test_handles_utf8_bom() -> None:
    result = load_csv("name;age\nJosé;30\n".encode("utf-8-sig"))
    assert list(result.frame.columns) == ["name", "age"]


def test_renames_duplicate_headers() -> None:
    result = load_csv(b"col,col,col\n1,2,3\n")
    assert list(result.frame.columns) == ["col", "col_1", "col_2"]
    assert any("Duplicate column names" in w for w in result.warnings)


def test_trims_and_fills_blank_headers() -> None:
    result = load_csv(b" a , b ,\n1,2\n")
    assert list(result.frame.columns) == ["a", "b"]


def test_empty_file_is_rejected() -> None:
    with pytest.raises(UnsupportedFileError):
        load_csv(b"")


def test_whitespace_only_file_is_rejected() -> None:
    with pytest.raises(UnsupportedFileError):
        load_csv(b"   \n\n  \n")


def test_binary_payload_is_rejected() -> None:
    with pytest.raises(UnsupportedFileError):
        load_csv(b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR")


def test_oversized_upload_is_rejected() -> None:
    from app.core.config import settings

    payload = b"a,b\n" + b"1,2\n" * 100
    original = settings.max_upload_bytes
    try:
        settings.max_upload_bytes = 10
        with pytest.raises(DatasetTooLargeError):
            load_csv(payload)
    finally:
        settings.max_upload_bytes = original


def test_ragged_rows_are_salvaged_with_a_warning() -> None:
    payload = b"a,b,c\n1,2,3\n4,5\n6,7,8,9\n"
    result = load_csv(payload)
    assert len(result.frame.columns) == 3
    assert result.parse_errors, "a ragged file must be reported as degraded"
    assert any("Malformed rows" in w for w in result.warnings)


def test_textual_null_markers_become_missing() -> None:
    payload = b"segment,income\nN/A,10\nNULL,20\n-,30\n0,40\n"
    result = load_csv(payload)
    assert int(result.frame["segment"].isna().sum()) == 3
    assert int(result.frame["income"].isna().sum()) == 0