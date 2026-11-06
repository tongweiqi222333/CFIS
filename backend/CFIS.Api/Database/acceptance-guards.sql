-- Additional defence for callers that bypass the API. Never rewrites posted history.
CREATE OR REPLACE FUNCTION cfis_source_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE amount numeric; currency text; business_date date;
BEGIN
 IF NEW."Status" <> 'Posted' THEN RETURN NEW; END IF;
 IF NEW."SourceTransactionId" IS NOT NULL THEN
  SELECT t."Amount",a."Currency",t."BusinessDate" INTO amount,currency,business_date
  FROM transactions t JOIN financial_accounts a ON a."OrganizationId"=t."OrganizationId" AND a."Id"=t."AccountId"
  WHERE t."OrganizationId"=NEW."OrganizationId" AND t."Id"=NEW."SourceTransactionId";
 ELSIF NEW."SourceReceivableId" IS NOT NULL THEN
  SELECT "Amount","Currency","IssueDate" INTO amount,currency,business_date FROM receivables WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."SourceReceivableId";
 ELSIF NEW."SourcePayableId" IS NOT NULL THEN
  SELECT "Amount","Currency","IssueDate" INTO amount,currency,business_date FROM payables WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."SourcePayableId";
 ELSE RETURN NEW;
 END IF;
 IF amount IS NULL OR amount<>NEW."DebitTotal" OR amount<>NEW."CreditTotal" OR currency<>NEW."Currency" OR business_date<>NEW."BusinessDate" THEN
  RAISE EXCEPTION 'Journal amount, currency or date differs from source' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER journal_source_guard BEFORE INSERT OR UPDATE ON journal_entries FOR EACH ROW EXECUTE FUNCTION cfis_source_guard();

CREATE OR REPLACE FUNCTION cfis_close_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW."Status"<>'Closed' THEN RETURN NEW; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('cfis-accounting:'||NEW."OrganizationId"::text,0));
 IF EXISTS(SELECT 1 FROM transactions t WHERE t."OrganizationId"=NEW."OrganizationId" AND t."BusinessDate"<=NEW."EndDate" AND NOT EXISTS(SELECT 1 FROM journal_entries e WHERE e."OrganizationId"=t."OrganizationId" AND e."SourceTransactionId"=t."Id" AND e."Status"='Posted')) THEN
  RAISE EXCEPTION 'Unposted historical cash sources remain' USING ERRCODE='23514';
 END IF;
 IF EXISTS(SELECT 1 FROM (SELECT "OrganizationId","Id","IssueDate","Cancelled",'AR' AS kind FROM receivables UNION ALL SELECT "OrganizationId","Id","IssueDate","Cancelled",'AP' FROM payables) d
 WHERE d."OrganizationId"=NEW."OrganizationId" AND NOT d."Cancelled" AND d."IssueDate"<=NEW."EndDate" AND NOT EXISTS(
 SELECT 1 FROM journal_entries e WHERE e."OrganizationId"=d."OrganizationId" AND (CASE WHEN d.kind='AR' THEN e."SourceReceivableId"=d."Id" ELSE e."SourcePayableId"=d."Id" END) AND e."Status"='Posted' AND NOT EXISTS(SELECT 1 FROM journal_entries r WHERE r."OrganizationId"=e."OrganizationId" AND r."ReversalOfId"=e."Id" AND r."Status"='Posted'))) THEN
  RAISE EXCEPTION 'Unrecognised active documents remain' USING ERRCODE='23514';
 END IF;
 IF EXISTS(SELECT 1 FROM financial_accounts a WHERE a."OrganizationId"=NEW."OrganizationId" AND NOT EXISTS(SELECT 1 FROM accounting_mappings m WHERE m."OrganizationId"=a."OrganizationId" AND m."ResourceId"=a."Id" AND m."Kind"='FinancialAccount' AND m."Currency"=a."Currency")) THEN
  RAISE EXCEPTION 'Unmapped financial accounts remain' USING ERRCODE='23514';
 END IF;
 IF EXISTS(
 WITH cash AS (
  SELECT t."AccountId" AS account,sum(CASE WHEN t."Kind" IN ('Income','Opening') THEN t."Amount" ELSE -t."Amount" END) AS amount FROM transactions t WHERE t."OrganizationId"=NEW."OrganizationId" AND t."BusinessDate"<=NEW."EndDate" GROUP BY t."AccountId"
  UNION ALL SELECT t."DestinationAccountId",sum(t."Amount") FROM transactions t WHERE t."OrganizationId"=NEW."OrganizationId" AND t."BusinessDate"<=NEW."EndDate" AND t."DestinationAccountId" IS NOT NULL GROUP BY t."DestinationAccountId"
 ), expected AS (
  SELECT m."LedgerAccountId" AS ledger,coalesce(sum(c.amount),0) AS amount FROM accounting_mappings m LEFT JOIN cash c ON c.account=m."ResourceId" WHERE m."OrganizationId"=NEW."OrganizationId" AND m."Kind"='FinancialAccount' GROUP BY m."LedgerAccountId"
 ), actual AS (
  SELECT l."LedgerAccountId" AS ledger,sum(l."Debit"-l."Credit") AS amount FROM journal_lines l JOIN journal_entries e ON e."OrganizationId"=l."OrganizationId" AND e."Id"=l."EntryId" WHERE l."OrganizationId"=NEW."OrganizationId" AND e."Status"='Posted' AND e."BusinessDate"<=NEW."EndDate" GROUP BY l."LedgerAccountId"
 ) SELECT 1 FROM expected e LEFT JOIN actual a ON a.ledger=e.ledger WHERE e.amount<>coalesce(a.amount,0)
 ) THEN RAISE EXCEPTION 'Cash and general ledger differ' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER fiscal_close_guard BEFORE INSERT OR UPDATE ON fiscal_periods FOR EACH ROW EXECUTE FUNCTION cfis_close_guard();
