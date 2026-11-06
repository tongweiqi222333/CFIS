using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_OrganizationId_AccountId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_OrganizationId_DestinationAccountId",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_account_date_cover",
                table: "transactions",
                columns: new[] { "OrganizationId", "AccountId", "BusinessDate", "Id" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Kind" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_destination_date_cover",
                table: "transactions",
                columns: new[] { "OrganizationId", "DestinationAccountId", "BusinessDate", "Id" },
                filter: "\"DestinationAccountId\" IS NOT NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Kind" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_entry_cover",
                table: "journal_lines",
                columns: new[] { "OrganizationId", "EntryId" })
                .Annotation("Npgsql:IndexInclude", new[] { "LedgerAccountId", "Debit", "Credit" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_posted_date_cover",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "Currency", "BusinessDate", "Id" },
                filter: "\"Status\" = 'Posted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_account_date_cover",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_destination_date_cover",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_entry_cover",
                table: "journal_lines");

            migrationBuilder.DropIndex(
                name: "ix_journal_posted_date_cover",
                table: "journal_entries");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_AccountId",
                table: "transactions",
                columns: new[] { "OrganizationId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_DestinationAccountId",
                table: "transactions",
                columns: new[] { "OrganizationId", "DestinationAccountId" });
        }
    }
}
