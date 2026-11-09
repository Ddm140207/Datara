# Datara

Datara is a dataset quality and machine-learning readiness platform for teams that need to validate raw data before training models or shipping AI features. It profiles uploaded CSV / TSV data, surfaces data-quality issues, estimates ML readiness, and stores the results so teams can review issues, columns, samples, and correlations in a structured workflow.

This repository contains the server-side components for Datara:

- .NET API for dataset management and orchestration
- Python analysis service for deterministic profiling and scoring
- PostgreSQL database for persisted metadata and analysis results
- Docker Compose setup for local development

## What Datara does

- Uploads tabular datasets (CSV/TSV)
- Profiles each column for nulls, cardinality, type inference, ranges, and category distributions
- Detects data quality issues such as missing values, duplicates, high-cardinality fields, constant columns, and suspicious categorical patterns
- Identifies likely target columns and computes an ML readiness score
- Exposes correlations, sample rows, and analysis runs for troubleshooting and review
- Optionally integrates with AI-backed summaries when enabled

## Architecture

Datara is split into a few focused services:

- `backend/` — ASP.NET Core API and domain/application infrastructure
- `analysis-service/` — FastAPI application that calculates dataset profiles and ML readiness
- `docker-compose.yml` — local environment with Postgres and the service stack
- `samples/` — example datasets for testing and demos

At a high level:

1. A user uploads a dataset through the API.
2. The API stores metadata and the raw file.
3. The analysis service reads the uploaded file and computes profiling results.
4. The backend persists the analysis output and exposes it via REST endpoints.

## Repository layout

```text
DATARA/
├── backend/                 # .NET API
│   ├── src/
│   └── tests/
├── analysis-service/        # Python profiling service
│   ├── app/
│   └── tests/
├── docker-compose.yml       # Local orchestration
├── docs/                    # Project docs and notes
├── samples/                 # Sample CSV files
├── scripts/                 # Helper scripts
├── .gitignore
└── README.md
```

## Prerequisites

- Docker Desktop or Docker Engine with Compose
- Git
- Optional: .NET SDK 9 for local backend development
- Optional: Python 3.12 for local analysis-service development

## Quick start with Docker Compose

From the repository root:

```bash
docker compose up --build
```

This starts the stack defined in `docker-compose.yml`:

- PostgreSQL: `localhost:5432`
- API: `http://localhost:5000`
- Swagger UI: `http://localhost:5000/swagger`
- Analysis service: `http://localhost:8000`
- Analysis service docs: `http://localhost:8000/docs`

To stop the stack:

```bash
docker compose down
```

To remove volumes created for local data:

```bash
docker compose down -v
```

## Configuration

The application reads environment variables from the Compose file and the appsettings files in `backend/src/Datara.Api/`.

Key settings include:

- `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD`
- `API_PORT` and `WEB_PORT`
- `ANALYSIS_PORT`
- `AI_ENABLED`, `AI_PROVIDER`, `AI_MODEL`, `AI_API_KEY`
- `ConnectionStrings__Default`
- `AnalysisService__BaseUrl`

The default local configuration expects:

- API at `http://localhost:5000`
- Analysis service at `http://localhost:8000`
- Postgres at `localhost:5432`

## API overview

The Datara API exposes endpoints for:

- listing datasets
- uploading datasets and target column hints
- reviewing dataset details and column metadata
- reading quality issues and their severity categories
- checking correlation data and sample rows
- re-running analysis jobs
- retrieving analysis history and runs

Swagger is enabled in development and can be used directly to explore the API at `/swagger`.

## Local development

### Backend

```bash
cd backend
dotnet restore Datara.sln
dotnet test Datara.sln
```

### Analysis service

```bash
cd analysis-service
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt
pytest
```

## Notes

- The repository currently contains the API and analysis engine. A browser client can be added in a `frontend/` directory if you want to pair Datara with a separate web application.
- The compose file includes a `web` service for that scenario, and the application is configured with `FRONTEND_ORIGIN` / `Cors__AllowedOrigins` for local front-end access.
- AI features are disabled by default and can be enabled with a valid provider configuration.

## License

This repository does not currently declare a license file. If you intend to make the project public, add a license before publishing or distributing it more broadly.

## Contributing

Contributions are welcome. The expected workflow is:

1. Open an issue or propose a change.
2. Work in a focused branch.
3. Validate the relevant backend or analysis-service tests.
4. Submit a pull request with a clear summary of the change.
