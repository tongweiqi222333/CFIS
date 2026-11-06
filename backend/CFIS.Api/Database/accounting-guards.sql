CREATE OR REPLACE FUNCTION cfis_period_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('cfis-accounting:'||NEW."OrganizationId"::text,0));
 IF TG_OP='UPDATE' THEN
  IF OLD."OrganizationId"<>NEW."OrganizationId" OR OLD."Id"<>NEW."Id" OR OLD."Code"<>NEW."Code" OR OLD."StartDate"<>NEW."StartDate" OR OLD."EndDate"<>NEW."EndDate" THEN RAISE EXCEPTION 'Fiscal period identity and boundaries are immutable' USING ERRCODE='23514'; END IF;
  IF OLD."Status"='Closed' AND NEW."Status"<>'Closed' THEN RAISE EXCEPTION 'Closed fiscal period cannot reopen' USING ERRCODE='23514'; END IF;
 END IF;
 IF EXISTS(SELECT 1 FROM fiscal_periods p WHERE p."OrganizationId"=NEW."OrganizationId" AND p."Id"<>NEW."Id" AND p."StartDate"<=NEW."EndDate" AND p."EndDate">=NEW."StartDate") THEN RAISE EXCEPTION 'Overlapping fiscal period' USING ERRCODE='23514'; END IF;
 IF NEW."Status"='Closed' THEN
  IF EXISTS(SELECT 1 FROM journal_entries e WHERE e."OrganizationId"=NEW."OrganizationId" AND e."PeriodId"=NEW."Id" AND e."Status"='Draft') THEN RAISE EXCEPTION 'Unposted drafts remain' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM transactions t WHERE t."OrganizationId"=NEW."OrganizationId" AND t."BusinessDate" BETWEEN NEW."StartDate" AND NEW."EndDate" AND NOT EXISTS(SELECT 1 FROM journal_entries e WHERE e."OrganizationId"=t."OrganizationId" AND e."SourceTransactionId"=t."Id" AND e."Status"='Posted')) THEN RAISE EXCEPTION 'Unposted cash sources remain' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM receivables d WHERE d."OrganizationId"=NEW."OrganizationId" AND NOT d."Cancelled" AND d."IssueDate" BETWEEN NEW."StartDate" AND NEW."EndDate" AND NOT EXISTS(SELECT 1 FROM journal_entries e WHERE e."OrganizationId"=d."OrganizationId" AND e."SourceReceivableId"=d."Id" AND e."Status"='Posted')) OR EXISTS(SELECT 1 FROM payables d WHERE d."OrganizationId"=NEW."OrganizationId" AND NOT d."Cancelled" AND d."IssueDate" BETWEEN NEW."StartDate" AND NEW."EndDate" AND NOT EXISTS(SELECT 1 FROM journal_entries e WHERE e."OrganizationId"=d."OrganizationId" AND e."SourcePayableId"=d."Id" AND e."Status"='Posted')) THEN RAISE EXCEPTION 'Unposted document sources remain' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER fiscal_period_guard BEFORE INSERT OR UPDATE ON fiscal_periods FOR EACH ROW EXECUTE FUNCTION cfis_period_guard();

CREATE OR REPLACE FUNCTION cfis_line_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE entry_status text; org uuid; entry uuid;
BEGIN
 IF TG_OP='DELETE' THEN org=OLD."OrganizationId";entry=OLD."EntryId"; ELSE org=NEW."OrganizationId";entry=NEW."EntryId";END IF;
 IF TG_OP='UPDATE' AND (NEW."OrganizationId"<>OLD."OrganizationId" OR NEW."EntryId"<>OLD."EntryId") THEN RAISE EXCEPTION 'Cannot move journal line' USING ERRCODE='23514'; END IF;
 SELECT "Status" INTO entry_status FROM journal_entries WHERE "OrganizationId"=org AND "Id"=entry FOR UPDATE;
 IF entry_status IS DISTINCT FROM 'Draft' THEN RAISE EXCEPTION 'Posted journal lines are immutable' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD;END IF;RETURN NEW;
END $$;
CREATE TRIGGER journal_line_guard BEFORE INSERT OR UPDATE OR DELETE ON journal_lines FOR EACH ROW EXECUTE FUNCTION cfis_line_guard();

CREATE OR REPLACE FUNCTION cfis_journal_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE debit numeric;credit numeric;line_count integer;p fiscal_periods;original journal_entries;
BEGIN
 IF TG_OP<>'INSERT' AND OLD."Status"='Posted' THEN RAISE EXCEPTION 'Posted journals are immutable' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 IF TG_OP='INSERT' AND NEW."Status"<>'Draft' THEN RAISE EXCEPTION 'Journal must start as draft' USING ERRCODE='23514'; END IF;
 IF NEW."Status"='Posted' THEN
  PERFORM pg_advisory_xact_lock(hashtextextended('cfis-accounting:'||NEW."OrganizationId"::text,0));
  SELECT * INTO p FROM fiscal_periods WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."PeriodId" FOR UPDATE;
  IF p."Status" IS DISTINCT FROM 'Open' OR NEW."BusinessDate"<p."StartDate" OR NEW."BusinessDate">p."EndDate" THEN RAISE EXCEPTION 'Fiscal period not open' USING ERRCODE='23514'; END IF;
  SELECT count(*),coalesce(sum("Debit"),0),coalesce(sum("Credit"),0) INTO line_count,debit,credit FROM journal_lines WHERE "OrganizationId"=NEW."OrganizationId" AND "EntryId"=NEW."Id";
  IF line_count<2 OR line_count>200 OR debit<>credit OR debit<=0 OR debit<>NEW."DebitTotal" OR credit<>NEW."CreditTotal" OR NEW."Number" IS NULL OR NEW."PostedAt" IS NULL THEN RAISE EXCEPTION 'Unbalanced journal or invalid posting metadata' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM journal_lines l JOIN chart_of_accounts a ON a."OrganizationId"=l."OrganizationId" AND a."Id"=l."LedgerAccountId" WHERE l."OrganizationId"=NEW."OrganizationId" AND l."EntryId"=NEW."Id" AND (NOT a."Active" OR a."Currency"<>NEW."Currency" OR EXISTS(SELECT 1 FROM chart_of_accounts child WHERE child."OrganizationId"=a."OrganizationId" AND child."ParentId"=a."Id"))) THEN RAISE EXCEPTION 'Invalid ledger account on journal' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM receivables d WHERE d."OrganizationId"=NEW."OrganizationId" AND d."Id"=NEW."SourceReceivableId" AND d."Cancelled") OR EXISTS(SELECT 1 FROM payables d WHERE d."OrganizationId"=NEW."OrganizationId" AND d."Id"=NEW."SourcePayableId" AND d."Cancelled") THEN RAISE EXCEPTION 'Source document cancelled' USING ERRCODE='23514'; END IF;
  IF NEW."ReversalOfId" IS NOT NULL THEN
   SELECT * INTO original FROM journal_entries WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."ReversalOfId";
   IF original."Status" IS DISTINCT FROM 'Posted' OR original."ReversalOfId" IS NOT NULL OR NEW."BusinessDate"<original."BusinessDate" OR NEW."Currency"<>original."Currency" THEN RAISE EXCEPTION 'Invalid reversal header' USING ERRCODE='23514'; END IF;
   IF line_count<>(SELECT count(*) FROM journal_lines WHERE "OrganizationId"=NEW."OrganizationId" AND "EntryId"=original."Id") OR EXISTS(SELECT 1 FROM journal_lines l LEFT JOIN journal_lines o ON o."OrganizationId"=l."OrganizationId" AND o."EntryId"=original."Id" AND o."Position"=l."Position" WHERE l."OrganizationId"=NEW."OrganizationId" AND l."EntryId"=NEW."Id" AND (o."Id" IS NULL OR o."LedgerAccountId"<>l."LedgerAccountId" OR o."Debit"<>l."Credit" OR o."Credit"<>l."Debit")) THEN RAISE EXCEPTION 'Reversal lines must exactly negate original' USING ERRCODE='23514'; END IF;
   IF original."SourceTransactionId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM transactions t WHERE t."OrganizationId"=NEW."OrganizationId" AND t."Id"=NEW."SourceTransactionId" AND t."ReversalOfId"=original."SourceTransactionId") THEN RAISE EXCEPTION 'Cash journal reversal must match cash reversal' USING ERRCODE='23514'; END IF;
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER journal_header_guard BEFORE INSERT OR UPDATE OR DELETE ON journal_entries FOR EACH ROW EXECUTE FUNCTION cfis_journal_guard();

CREATE OR REPLACE FUNCTION cfis_cash_period_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_advisory_xact_lock_shared(hashtextextended('cfis-accounting:'||NEW."OrganizationId"::text,0));
 IF EXISTS(SELECT 1 FROM fiscal_periods WHERE "OrganizationId"=NEW."OrganizationId") AND NOT EXISTS(SELECT 1 FROM fiscal_periods WHERE "OrganizationId"=NEW."OrganizationId" AND "Status"='Open' AND NEW."BusinessDate" BETWEEN "StartDate" AND "EndDate") THEN RAISE EXCEPTION 'Cash date outside open fiscal periods' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER transaction_fiscal_guard BEFORE INSERT ON transactions FOR EACH ROW EXECUTE FUNCTION cfis_cash_period_guard();

CREATE OR REPLACE FUNCTION cfis_document_accounting_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_advisory_xact_lock_shared(hashtextextended('cfis-accounting:'||NEW."OrganizationId"::text,0));
 IF TG_OP='INSERT' AND EXISTS(SELECT 1 FROM fiscal_periods WHERE "OrganizationId"=NEW."OrganizationId") AND NOT EXISTS(SELECT 1 FROM fiscal_periods WHERE "OrganizationId"=NEW."OrganizationId" AND "Status"='Open' AND NEW."IssueDate" BETWEEN "StartDate" AND "EndDate") THEN RAISE EXCEPTION 'Document date outside open fiscal periods' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' AND NEW."Cancelled" AND NOT OLD."Cancelled" AND EXISTS(SELECT 1 FROM journal_entries e WHERE e."OrganizationId"=NEW."OrganizationId" AND e."Status"='Posted' AND (CASE WHEN TG_TABLE_NAME='receivables' THEN e."SourceReceivableId"=NEW."Id" ELSE e."SourcePayableId"=NEW."Id" END) AND NOT EXISTS(SELECT 1 FROM journal_entries r WHERE r."OrganizationId"=e."OrganizationId" AND r."ReversalOfId"=e."Id" AND r."Status"='Posted')) THEN RAISE EXCEPTION 'Reverse recognition journal before cancelling document' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER receivable_accounting_guard BEFORE INSERT OR UPDATE ON receivables FOR EACH ROW EXECUTE FUNCTION cfis_document_accounting_guard();
CREATE TRIGGER payable_accounting_guard BEFORE INSERT OR UPDATE ON payables FOR EACH ROW EXECUTE FUNCTION cfis_document_accounting_guard();
