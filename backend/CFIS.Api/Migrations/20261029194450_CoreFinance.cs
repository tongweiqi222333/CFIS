using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class CoreFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_logs_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.UniqueConstraint("AK_categories_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_categories_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_accounts", x => x.Id);
                    table.UniqueConstraint("AK_financial_accounts_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_account_balance", "\"Balance\" >= 0");
                    table.ForeignKey(
                        name: "FK_financial_accounts_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                    table.CheckConstraint("ck_transaction_amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_transactions_categories_OrganizationId_CategoryId",
                        columns: x => new { x.OrganizationId, x.CategoryId },
                        principalTable: "categories",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_financial_accounts_OrganizationId_AccountId",
                        columns: x => new { x.OrganizationId, x.AccountId },
                        principalTable: "financial_accounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_financial_accounts_OrganizationId_DestinationA~",
                        columns: x => new { x.OrganizationId, x.DestinationAccountId },
                        principalTable: "financial_accounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_OrganizationId_CreatedAt_Id",
                table: "audit_logs",
                columns: new[] { "OrganizationId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_OrganizationId_Kind_Name",
                table: "categories",
                columns: new[] { "OrganizationId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_AccountId",
                table: "transactions",
                columns: new[] { "OrganizationId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_CategoryId",
                table: "transactions",
                columns: new[] { "OrganizationId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_CreatedAt_Id",
                table: "transactions",
                columns: new[] { "OrganizationId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_DestinationAccountId",
                table: "transactions",
                columns: new[] { "OrganizationId", "DestinationAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_RequestId",
                table: "transactions",
                columns: new[] { "OrganizationId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "financial_accounts");
        }
    }
}
