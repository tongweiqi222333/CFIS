using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReportingHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "receivables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "receivables",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "payables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "payables",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAt",
                table: "budgets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "budgets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "budgets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
            migrationBuilder.Sql("""
                CREATE TABLE report_history_boundary (started_at timestamptz NOT NULL);
                INSERT INTO report_history_boundary VALUES (clock_timestamp());
                UPDATE receivables SET "CreatedAt"=now(), "CancelledAt"=CASE WHEN "Cancelled" THEN now() END;
                UPDATE payables SET "CreatedAt"=now(), "CancelledAt"=CASE WHEN "Cancelled" THEN now() END;
                UPDATE budgets SET "CreatedAt"=now(), "ActivatedAt"=CASE WHEN "Status" IN ('Active','Closed') THEN now() END, "ClosedAt"=CASE WHEN "Status"='Closed' THEN now() END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE report_history_boundary");
            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "budgets");
        }
    }
}
