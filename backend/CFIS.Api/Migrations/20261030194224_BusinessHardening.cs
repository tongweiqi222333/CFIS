using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class BusinessHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrectionOfId",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "receivables",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "payables",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "counterparties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousVersionId",
                table: "budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "budgets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "budgets",
                type: "text",
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_CorrectionOfId",
                table: "transactions",
                columns: new[] { "OrganizationId", "CorrectionOfId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budgets_OrganizationId_PreviousVersionId",
                table: "budgets",
                columns: new[] { "OrganizationId", "PreviousVersionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_budgets_budgets_OrganizationId_PreviousVersionId",
                table: "budgets",
                columns: new[] { "OrganizationId", "PreviousVersionId" },
                principalTable: "budgets",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_transactions_OrganizationId_CorrectionOfId",
                table: "transactions",
                columns: new[] { "OrganizationId", "CorrectionOfId" },
                principalTable: "transactions",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_budgets_budgets_OrganizationId_PreviousVersionId",
                table: "budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_transactions_OrganizationId_CorrectionOfId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_OrganizationId_CorrectionOfId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_budgets_OrganizationId_PreviousVersionId",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "CorrectionOfId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "counterparties");

            migrationBuilder.DropColumn(
                name: "PreviousVersionId",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "budgets");
        }
    }
}
