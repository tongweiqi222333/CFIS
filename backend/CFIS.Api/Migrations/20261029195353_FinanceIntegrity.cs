using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class FinanceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "transactions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalOfId",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "organizations",
                type: "text",
                nullable: false,
                defaultValue: "Asia/Shanghai");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "financial_accounts",
                type: "text",
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "categories",
                type: "text",
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.Sql("""UPDATE transactions SET "BusinessDate" = ("CreatedAt" AT TIME ZONE 'Asia/Shanghai')::date""");
            migrationBuilder.AddUniqueConstraint(
                name: "AK_transactions_OrganizationId_Id",
                table: "transactions",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_BusinessDate_Id",
                table: "transactions",
                columns: new[] { "OrganizationId", "BusinessDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_ReversalOfId",
                table: "transactions",
                columns: new[] { "OrganizationId", "ReversalOfId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_transactions_OrganizationId_ReversalOfId",
                table: "transactions",
                columns: new[] { "OrganizationId", "ReversalOfId" },
                principalTable: "transactions",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_transactions_OrganizationId_ReversalOfId",
                table: "transactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_transactions_OrganizationId_Id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_OrganizationId_BusinessDate_Id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_OrganizationId_ReversalOfId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ReversalOfId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "financial_accounts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "categories");
        }
    }
}

