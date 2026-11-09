from __future__ import annotations

import logging
import sys

from app.core.config import settings


def configure_logging() -> None:
    """Emit structured-ish logs to stdout so Docker/K8s can collect them."""
    level = getattr(logging, settings.log_level.upper(), logging.INFO)

    root = logging.getLogger()
    root.handlers.clear()
    handler = logging.StreamHandler(stream=sys.stdout)
    handler.setFormatter(
        logging.Formatter(
            fmt="%(asctime)s %(levelname)-8s %(name)s :: %(message)s",
            datefmt="%Y-%m-%dT%H:%M:%S",
        )
    )
    root.addHandler(handler)
    root.setLevel(level)

    # pandas/numpy are extremely chatty at INFO level.
    logging.getLogger("pandas").setLevel(logging.WARNING)
    logging.getLogger("numpy").setLevel(logging.WARNING)
    logging.getLogger("matplotlib").setLevel(logging.WARNING)