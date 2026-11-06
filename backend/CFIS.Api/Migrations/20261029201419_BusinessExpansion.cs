using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class BusinessExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayableId",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivableId",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budgets", x => x.Id);
                    table.UniqueConstraint("AK_budgets_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_budgets_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "counterparties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Contact = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparties", x => x.Id);
                    table.UniqueConstraint("AK_counterparties_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_counterparties_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_budget_items_budgets_OrganizationId_BudgetId",
                        columns: x => new { x.OrganizationId, x.BudgetId },
                        principalTable: "budgets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_budget_items_categories_OrganizationId_CategoryId",
                        columns: x => new { x.OrganizationId, x.CategoryId },
                        principalTable: "categories",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payables", x => x.Id);
                    table.UniqueConstraint("AK_payables_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_payables_categories_OrganizationId_CategoryId",
                        columns: x => new { x.OrganizationId, x.CategoryId },
                        principalTable: "categories",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payables_counterparties_OrganizationId_CounterpartyId",
                        columns: x => new { x.OrganizationId, x.CounterpartyId },
                        principalTable: "counterparties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receivables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receivables", x => x.Id);
                    table.UniqueConstraint("AK_receivables_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_receivables_categories_OrganizationId_CategoryId",
                        columns: x => new { x.OrganizationId, x.CategoryId },
                        principalTable: "categories",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receivables_counterparties_OrganizationId_CounterpartyId",
                        columns: x => new { x.OrganizationId, x.CounterpartyId },
                        principalTable: "counterparties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_PayableId",
                table: "transactions",
                columns: new[] { "OrganizationId", "PayableId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OrganizationId_ReceivableId",
                table: "transactions",
                columns: new[] { "OrganizationId", "ReceivableId" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_items_OrganizationId_BudgetId_CategoryId",
                table: "budget_items",
                columns: new[] { "OrganizationId", "BudgetId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_items_OrganizationId_CategoryId",
                table: "budget_items",
                columns: new[] { "OrganizationId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_counterparties_OrganizationId_Kind_Name",
                table: "counterparties",
                columns: new[] { "OrganizationId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payables_OrganizationId_CategoryId",
                table: "payables",
                columns: new[] { "OrganizationId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_payables_OrganizationId_CounterpartyId",
                table: "payables",
                columns: new[] { "OrganizationId", "CounterpartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_payables_OrganizationId_Number",
                table: "payables",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receivables_OrganizationId_CategoryId",
                table: "receivables",
                columns: new[] { "OrganizationId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_receivables_OrganizationId_CounterpartyId",
                table: "receivables",
                columns: new[] { "OrganizationId", "CounterpartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_receivables_OrganizationId_Number",
                table: "receivables",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_payables_OrganizationId_PayableId",
                table: "transactions",
                columns: new[] { "OrganizationId", "PayableId" },
                principalTable: "payables",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_receivables_OrganizationId_ReceivableId",
                table: "transactions",
                columns: new[] { "OrganizationId", "ReceivableId" },
                principalTable: "receivables",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_payables_OrganizationId_PayableId",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_receivables_OrganizationId_ReceivableId",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "budget_items");

            migrationBuilder.DropTable(
                name: "payables");

            migrationBuilder.DropTable(
                name: "receivables");

            migrationBuilder.DropTable(
                name: "budgets");

            migrationBuilder.DropTable(
                name: "counterparties");

            migrationBuilder.DropIndex(
                name: "IX_transactions_OrganizationId_PayableId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_OrganizationId_ReceivableId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PayableId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ReceivableId",
                table: "transactions");
        }
    }
}
