using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Datara.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_external_identity = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "datasets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    column_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ml_readiness_score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    data_quality_score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    analyzed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    target_column = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_datasets", x => x.id);
                    table.ForeignKey(
                        name: "FK_datasets_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "analysis_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    trigger = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_analysis_runs_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dataset_columns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    inferred_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    pandas_dtype = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    non_null_count = table.Column<int>(type: "integer", nullable: false),
                    null_count = table.Column<int>(type: "integer", nullable: false),
                    missing_percentage = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    unique_count = table.Column<int>(type: "integer", nullable: false),
                    cardinality_ratio = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    is_constant = table.Column<bool>(type: "boolean", nullable: false),
                    memory_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    leading_whitespace_count = table.Column<int>(type: "integer", nullable: false),
                    mixed_type_count = table.Column<int>(type: "integer", nullable: false),
                    mean = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    median = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    std_dev = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    min = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    max = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    q1 = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    q3 = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    p05 = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    p25 = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    p75 = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    p95 = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    skewness = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    kurtosis = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    zeros_count = table.Column<int>(type: "integer", nullable: true),
                    negative_count = table.Column<int>(type: "integer", nullable: true),
                    top_categories = table.Column<string>(type: "jsonb", nullable: false),
                    category_percentage_covered = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    histogram_bin_edges = table.Column<List<double>>(type: "double precision[]", nullable: false),
                    histogram_counts = table.Column<List<int>>(type: "integer[]", nullable: false),
                    outliers = table.Column<string>(type: "jsonb", nullable: false),
                    date_min = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    date_max = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sample_values = table.Column<List<string>>(type: "text[]", nullable: false),
                    notes = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dataset_columns", x => x.id);
                    table.ForeignKey(
                        name: "FK_dataset_columns_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dataset_analyses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    column_count = table.Column<int>(type: "integer", nullable: false),
                    total_cells = table.Column<int>(type: "integer", nullable: false),
                    total_missing = table.Column<int>(type: "integer", nullable: false),
                    overall_missing_percentage = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    duplicate_row_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_percentage = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    fully_empty_columns = table.Column<List<string>>(type: "text[]", nullable: false),
                    memory_usage_bytes = table.Column<long>(type: "bigint", nullable: false),
                    ml_readiness_score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    data_quality_score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    grade = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    verdict = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    score_components = table.Column<string>(type: "jsonb", nullable: false),
                    strengths = table.Column<List<string>>(type: "text[]", nullable: false),
                    risks = table.Column<List<string>>(type: "text[]", nullable: false),
                    gates = table.Column<List<string>>(type: "text[]", nullable: false),
                    correlations = table.Column<string>(type: "jsonb", nullable: true),
                    anomalies = table.Column<string>(type: "jsonb", nullable: false),
                    class_balance = table.Column<string>(type: "jsonb", nullable: true),
                    leakage_signals = table.Column<string>(type: "jsonb", nullable: false),
                    target_candidates = table.Column<List<string>>(type: "text[]", nullable: false),
                    SampleRows = table.Column<List<string>>(type: "text[]", nullable: false),
                    SampleColumns = table.Column<List<string>>(type: "text[]", nullable: false),
                    warnings = table.Column<List<string>>(type: "text[]", nullable: false),
                    parse_errors = table.Column<List<string>>(type: "text[]", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dataset_analyses", x => x.id);
                    table.ForeignKey(
                        name: "FK_dataset_analyses_analysis_runs_analysis_run_id",
                        column: x => x.analysis_run_id,
                        principalTable: "analysis_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dataset_analyses_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_quality_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_analysis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    column = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    recommendation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    score_impact = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_quality_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_data_quality_issues_dataset_analyses_dataset_analysis_id",
                        column: x => x.dataset_analysis_id,
                        principalTable: "dataset_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_data_quality_issues_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analysis_runs_dataset_started",
                table: "analysis_runs",
                columns: new[] { "dataset_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_data_quality_issues_dataset_analysis_id",
                table: "data_quality_issues",
                column: "dataset_analysis_id");

            migrationBuilder.CreateIndex(
                name: "ix_issues_dataset_column",
                table: "data_quality_issues",
                columns: new[] { "dataset_id", "column" });

            migrationBuilder.CreateIndex(
                name: "ix_issues_dataset_severity_category",
                table: "data_quality_issues",
                columns: new[] { "dataset_id", "severity", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_dataset_analyses_analysis_run_id",
                table: "dataset_analyses",
                column: "analysis_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_dataset_analyses_dataset_created",
                table: "dataset_analyses",
                columns: new[] { "dataset_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_dataset_columns_dataset_name",
                table: "dataset_columns",
                columns: new[] { "dataset_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_datasets_owner_uploaded",
                table: "datasets",
                columns: new[] { "owner_id", "uploaded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_quality_issues");

            migrationBuilder.DropTable(
                name: "dataset_columns");

            migrationBuilder.DropTable(
                name: "dataset_analyses");

            migrationBuilder.DropTable(
                name: "analysis_runs");

            migrationBuilder.DropTable(
                name: "datasets");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
