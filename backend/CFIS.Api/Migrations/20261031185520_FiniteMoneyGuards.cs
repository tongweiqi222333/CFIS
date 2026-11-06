using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class FiniteMoneyGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE financial_accounts ADD CONSTRAINT ck_financial_accounts_finite CHECK ("Balance" <> 'NaN'::numeric);
                ALTER TABLE transactions ADD CONSTRAINT ck_transactions_finite CHECK ("Amount" <> 'NaN'::numeric);
                ALTER TABLE receivables ADD CONSTRAINT ck_receivables_finite CHECK ("Amount" <> 'NaN'::numeric);
                ALTER TABLE payables ADD CONSTRAINT ck_payables_finite CHECK ("Amount" <> 'NaN'::numeric);
                ALTER TABLE budget_items ADD CONSTRAINT ck_budget_items_finite CHECK ("Amount" <> 'NaN'::numeric);
                ALTER TABLE journal_entries ADD CONSTRAINT ck_journal_entries_finite CHECK ("DebitTotal" <> 'NaN'::numeric AND "CreditTotal" <> 'NaN'::numeric);
                ALTER TABLE journal_lines ADD CONSTRAINT ck_journal_lines_finite CHECK ("Debit" <> 'NaN'::numeric AND "Credit" <> 'NaN'::numeric);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE financial_accounts DROP CONSTRAINT ck_financial_accounts_finite;
                ALTER TABLE transactions DROP CONSTRAINT ck_transactions_finite;
                ALTER TABLE receivables DROP CONSTRAINT ck_receivables_finite;
                ALTER TABLE payables DROP CONSTRAINT ck_payables_finite;
                ALTER TABLE budget_items DROP CONSTRAINT ck_budget_items_finite;
                ALTER TABLE journal_entries DROP CONSTRAINT ck_journal_entries_finite;
                ALTER TABLE journal_lines DROP CONSTRAINT ck_journal_lines_finite;
                """);

        }
    }
}
