DO $$
BEGIN
 IF EXISTS (
 SELECT 1 FROM financial_accounts a
 LEFT JOIN (
 SELECT "OrganizationId", "AccountId" AS id, sum(CASE WHEN "Kind" IN ('Income','Opening') THEN "Amount" ELSE -"Amount" END) AS delta FROM transactions GROUP BY 1,2
 UNION ALL
 SELECT "OrganizationId", "DestinationAccountId", sum("Amount") FROM transactions WHERE "Kind"='Transfer' GROUP BY 1,2
 ) t ON t."OrganizationId"=a."OrganizationId" AND t.id=a."Id"
 GROUP BY a."OrganizationId",a."Id",a."Balance" HAVING a."Balance"<>coalesce(sum(t.delta),0)
 ) THEN RAISE EXCEPTION 'Restored account balance differs from ledger'; END IF;
 IF EXISTS (
 SELECT 1 FROM (
 SELECT d."Amount",d."Cancelled",coalesce(sum(CASE WHEN t."Kind"='Income' THEN t."Amount" ELSE -t."Amount" END),0) paid FROM receivables d LEFT JOIN transactions t ON t."OrganizationId"=d."OrganizationId" AND t."ReceivableId"=d."Id" GROUP BY d."Id",d."Amount",d."Cancelled"
 UNION ALL
 SELECT d."Amount",d."Cancelled",coalesce(sum(CASE WHEN t."Kind"='Expense' THEN t."Amount" ELSE -t."Amount" END),0) FROM payables d LEFT JOIN transactions t ON t."OrganizationId"=d."OrganizationId" AND t."PayableId"=d."Id" GROUP BY d."Id",d."Amount",d."Cancelled"
 ) d WHERE paid<0 OR paid>"Amount" OR ("Cancelled" AND paid<>0)
 ) THEN RAISE EXCEPTION 'Restored document settlement is inconsistent'; END IF;
 IF EXISTS (SELECT 1 FROM transactions t JOIN transactions original ON original."OrganizationId"=t."OrganizationId" AND original."Id"=t."ReversalOfId" WHERE original."Amount"<>t."Amount" OR original."ReversalOfId" IS NOT NULL)
 THEN RAISE EXCEPTION 'Restored reversal relationship is inconsistent'; END IF;
 IF EXISTS (SELECT 1 FROM budget_items i LEFT JOIN budgets b ON b."OrganizationId"=i."OrganizationId" AND b."Id"=i."BudgetId" LEFT JOIN categories c ON c."OrganizationId"=i."OrganizationId" AND c."Id"=i."CategoryId" WHERE b."Id" IS NULL OR c."Id" IS NULL OR c."Kind"<>'Expense' OR i."Amount"<=0)
 THEN RAISE EXCEPTION 'Restored budget lines are inconsistent'; END IF;
END $$;
DO $$ BEGIN
 IF to_regclass('public.journal_entries') IS NOT NULL THEN
  IF EXISTS(SELECT 1 FROM journal_entries e LEFT JOIN (SELECT "OrganizationId","EntryId",sum("Debit") debit,sum("Credit") credit,count(*) n FROM journal_lines GROUP BY 1,2) l ON l."OrganizationId"=e."OrganizationId" AND l."EntryId"=e."Id" WHERE e."Status"='Posted' AND (l.n IS NULL OR l.n<2 OR l.debit<>l.credit OR l.debit<>e."DebitTotal" OR l.credit<>e."CreditTotal")) THEN RAISE EXCEPTION 'Restored posted journals do not balance'; END IF;
  IF EXISTS(SELECT 1 FROM journal_entries e JOIN fiscal_periods p ON p."OrganizationId"=e."OrganizationId" AND p."Id"=e."PeriodId" WHERE e."BusinessDate"<p."StartDate" OR e."BusinessDate">p."EndDate" OR p."Status"='Closed' AND e."Status"='Draft') THEN RAISE EXCEPTION 'Restored fiscal periods inconsistent'; END IF;
  IF EXISTS(SELECT 1 FROM journal_entries e JOIN transactions t ON t."OrganizationId"=e."OrganizationId" AND t."Id"=e."SourceTransactionId" JOIN financial_accounts a ON a."OrganizationId"=t."OrganizationId" AND a."Id"=t."AccountId" WHERE e."Status"='Posted' AND (e."DebitTotal"<>t."Amount" OR e."CreditTotal"<>t."Amount" OR e."Currency"<>a."Currency" OR e."BusinessDate"<>t."BusinessDate"))
  OR EXISTS(SELECT 1 FROM journal_entries e JOIN receivables d ON d."OrganizationId"=e."OrganizationId" AND d."Id"=e."SourceReceivableId" WHERE e."Status"='Posted' AND (e."DebitTotal"<>d."Amount" OR e."CreditTotal"<>d."Amount" OR e."Currency"<>d."Currency" OR e."BusinessDate"<>d."IssueDate"))
  OR EXISTS(SELECT 1 FROM journal_entries e JOIN payables d ON d."OrganizationId"=e."OrganizationId" AND d."Id"=e."SourcePayableId" WHERE e."Status"='Posted' AND (e."DebitTotal"<>d."Amount" OR e."CreditTotal"<>d."Amount" OR e."Currency"<>d."Currency" OR e."BusinessDate"<>d."IssueDate"))
  THEN RAISE EXCEPTION 'Restored journal differs from source'; END IF;
 END IF;
END $$;

DO $$ BEGIN
 IF to_regclass('public.transaction_allocations') IS NOT NULL THEN
  IF EXISTS(SELECT 1 FROM transaction_allocations a JOIN transactions t ON t."OrganizationId"=a."OrganizationId" AND t."Id"=a."TransactionId" GROUP BY t."Id",t."Amount" HAVING count(*)<2 OR sum(a."Amount")<>t."Amount") THEN RAISE EXCEPTION 'Restored allocations do not total transaction amount';END IF;
 END IF;
 IF to_regclass('public.attachments') IS NOT NULL THEN
  IF EXISTS(SELECT 1 FROM attachments WHERE "Sha256"<>upper(encode(sha256("Content"),'hex'))) THEN RAISE EXCEPTION 'Restored attachment hash mismatch';END IF;
 END IF;
END $$;
