CREATE INDEX IF NOT EXISTS ix_transactions_account_date_cover ON transactions ("OrganizationId","AccountId","BusinessDate","Id") INCLUDE ("Amount","Kind");
CREATE INDEX IF NOT EXISTS ix_transactions_destination_date_cover ON transactions ("OrganizationId","DestinationAccountId","BusinessDate","Id") INCLUDE ("Amount","Kind") WHERE "DestinationAccountId" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_journal_posted_date_cover ON journal_entries ("OrganizationId","Currency","BusinessDate","Id") WHERE "Status"='Posted';
CREATE INDEX IF NOT EXISTS ix_journal_lines_entry_cover ON journal_lines ("OrganizationId","EntryId") INCLUDE ("LedgerAccountId","Debit","Credit");
