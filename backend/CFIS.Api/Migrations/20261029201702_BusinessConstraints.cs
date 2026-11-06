using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class BusinessConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_document",
                table: "transactions",
                sql: "\"ReceivableId\" IS NULL OR \"PayableId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receivable_amount",
                table: "receivables",
                sql: "\"Amount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receivable_dates",
                table: "receivables",
                sql: "\"DueDate\" >= \"IssueDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payable_amount",
                table: "payables",
                sql: "\"Amount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payable_dates",
                table: "payables",
                sql: "\"DueDate\" >= \"IssueDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_budget_dates",
                table: "budgets",
                sql: "\"EndDate\" >= \"StartDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_budget_item_amount",
                table: "budget_items",
                sql: "\"Amount\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_document",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receivable_amount",
                table: "receivables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receivable_dates",
                table: "receivables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payable_amount",
                table: "payables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payable_dates",
                table: "payables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_budget_dates",
                table: "budgets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_budget_item_amount",
                table: "budget_items");
        }
    }
}
