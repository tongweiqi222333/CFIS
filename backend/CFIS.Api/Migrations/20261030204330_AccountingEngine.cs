using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chart_of_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chart_of_accounts", x => x.Id);
                    table.UniqueConstraint("AK_chart_of_accounts_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_ledger_kind", "\"Kind\" IN ('Asset','Liability','Equity','Revenue','Expense')");
                    table.CheckConstraint("ck_ledger_parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_chart_of_accounts_chart_of_accounts_OrganizationId_ParentId",
                        columns: x => new { x.OrganizationId, x.ParentId },
                        principalTable: "chart_of_accounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chart_of_accounts_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_periods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_periods", x => x.Id);
                    table.UniqueConstraint("AK_fiscal_periods_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_period_dates", "\"StartDate\" <= \"EndDate\"");
                    table.CheckConstraint("ck_period_status", "\"Status\" IN ('Open','Closed')");
                    table.ForeignKey(
                        name: "FK_fiscal_periods_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_mappings_chart_of_accounts_OrganizationId_Ledger~",
                        columns: x => new { x.OrganizationId, x.LedgerAccountId },
                        principalTable: "chart_of_accounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalOfId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceReceivableId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePayableId = table.Column<Guid>(type: "uuid", nullable: true),
                    DebitTotal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CreditTotal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.Id);
                    table.UniqueConstraint("AK_journal_entries_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("ck_journal_source", "num_nonnulls(\"SourceTransactionId\",\"SourceReceivableId\",\"SourcePayableId\") <= 1");
                    table.CheckConstraint("ck_journal_status", "\"Status\" IN ('Draft','Posted')");
                    table.CheckConstraint("ck_journal_totals", "\"DebitTotal\" >= 0 AND \"CreditTotal\" >= 0 AND (\"Status\" <> 'Posted' OR (\"DebitTotal\" = \"CreditTotal\" AND \"DebitTotal\" > 0))");
                    table.ForeignKey(
                        name: "FK_journal_entries_fiscal_periods_OrganizationId_PeriodId",
                        columns: x => new { x.OrganizationId, x.PeriodId },
                        principalTable: "fiscal_periods",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_journal_entries_OrganizationId_ReversalOfId",
                        columns: x => new { x.OrganizationId, x.ReversalOfId },
                        principalTable: "journal_entries",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_payables_OrganizationId_SourcePayableId",
                        columns: x => new { x.OrganizationId, x.SourcePayableId },
                        principalTable: "payables",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_receivables_OrganizationId_SourceReceivable~",
                        columns: x => new { x.OrganizationId, x.SourceReceivableId },
                        principalTable: "receivables",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_transactions_OrganizationId_SourceTransacti~",
                        columns: x => new { x.OrganizationId, x.SourceTransactionId },
                        principalTable: "transactions",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => x.Id);
                    table.CheckConstraint("ck_line_sides", "(\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0)");
                    table.ForeignKey(
                        name: "FK_journal_lines_chart_of_accounts_OrganizationId_LedgerAccoun~",
                        columns: x => new { x.OrganizationId, x.LedgerAccountId },
                        principalTable: "chart_of_accounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_OrganizationId_EntryId",
                        columns: x => new { x.OrganizationId, x.EntryId },
                        principalTable: "journal_entries",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_mappings_OrganizationId_Kind_ResourceId_Currency",
                table: "accounting_mappings",
                columns: new[] { "OrganizationId", "Kind", "ResourceId", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_mappings_OrganizationId_LedgerAccountId",
                table: "accounting_mappings",
                columns: new[] { "OrganizationId", "LedgerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_chart_of_accounts_OrganizationId_Code_Currency",
                table: "chart_of_accounts",
                columns: new[] { "OrganizationId", "Code", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chart_of_accounts_OrganizationId_ParentId",
                table: "chart_of_accounts",
                columns: new[] { "OrganizationId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_periods_OrganizationId_Code",
                table: "fiscal_periods",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_Number",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_PeriodId",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_RequestId",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_ReversalOfId",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "ReversalOfId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_SourcePayableId",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "SourcePayableId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_SourceReceivableId",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "SourceReceivableId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_SourceTransactionId",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "SourceTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_OrganizationId_Status_BusinessDate_Id",
                table: "journal_entries",
                columns: new[] { "OrganizationId", "Status", "BusinessDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_OrganizationId_EntryId_Position",
                table: "journal_lines",
                columns: new[] { "OrganizationId", "EntryId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_OrganizationId_LedgerAccountId_EntryId",
                table: "journal_lines",
                columns: new[] { "OrganizationId", "LedgerAccountId", "EntryId" });
            using var sqlStream=typeof(AccountingModel).Assembly.GetManifestResourceStream("CFIS.Api.Database.accounting-guards.sql")!;
            using var reader=new System.IO.StreamReader(sqlStream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS cfis_document_accounting_guard() CASCADE; DROP TRIGGER IF EXISTS transaction_fiscal_guard ON transactions; DROP FUNCTION IF EXISTS cfis_cash_period_guard(); DROP FUNCTION IF EXISTS cfis_journal_guard() CASCADE; DROP FUNCTION IF EXISTS cfis_line_guard() CASCADE; DROP FUNCTION IF EXISTS cfis_period_guard() CASCADE;");
            migrationBuilder.DropTable(
                name: "accounting_mappings");

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "chart_of_accounts");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "fiscal_periods");
        }
    }
}
