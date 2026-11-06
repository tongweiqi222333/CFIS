CREATE VIEW category_transactions AS
 SELECT t."Id",t."OrganizationId",t."AccountId",t."CategoryId",t."Kind",t."Amount",t."BusinessDate",t."CreatedAt" FROM transactions t
 WHERE t."CategoryId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM transaction_allocations a WHERE a."OrganizationId"=t."OrganizationId" AND a."TransactionId"=t."Id")
 UNION ALL SELECT t."Id",t."OrganizationId",t."AccountId",a."CategoryId",t."Kind",a."Amount",t."BusinessDate",t."CreatedAt" FROM transactions t JOIN transaction_allocations a ON a."OrganizationId"=t."OrganizationId" AND a."TransactionId"=t."Id";

CREATE FUNCTION cfis_allocation_insert() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM transactions t WHERE t."OrganizationId"=NEW."OrganizationId" AND t."Id"=NEW."TransactionId" AND pg_xact_status(t.xmin::text::xid8)='in progress') THEN RAISE EXCEPTION 'Allocations must be recorded with the original transaction' USING ERRCODE='23514';END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER allocation_insert BEFORE INSERT ON transaction_allocations FOR EACH ROW EXECUTE FUNCTION cfis_allocation_insert();
CREATE FUNCTION cfis_allocation_check() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE org uuid;id uuid;t transactions; n integer;amount numeric;original_kind text;
BEGIN
 org=NEW."OrganizationId";IF TG_TABLE_NAME='transactions' THEN id=NEW."Id";ELSE id=NEW."TransactionId";END IF;
 SELECT * INTO t FROM transactions WHERE "OrganizationId"=org AND "Id"=id;
 SELECT count(*),sum("Amount") INTO n,amount FROM transaction_allocations WHERE "OrganizationId"=org AND "TransactionId"=id;
 IF n=0 THEN RETURN NULL;END IF;
 original_kind=t."Kind";IF t."ReversalOfId" IS NOT NULL THEN SELECT "Kind" INTO original_kind FROM transactions WHERE "OrganizationId"=org AND "Id"=t."ReversalOfId";END IF;
 IF n<2 OR n>100 OR amount<>t."Amount" OR original_kind NOT IN ('Income','Expense') OR t."ReceivableId" IS NOT NULL OR t."PayableId" IS NOT NULL OR NOT EXISTS(SELECT 1 FROM transaction_allocations WHERE "OrganizationId"=org AND "TransactionId"=id AND "CategoryId"=t."CategoryId") OR EXISTS(SELECT 1 FROM transaction_allocations a JOIN categories c ON c."OrganizationId"=a."OrganizationId" AND c."Id"=a."CategoryId" WHERE a."OrganizationId"=org AND a."TransactionId"=id AND c."Kind"<>original_kind) THEN RAISE EXCEPTION 'Invalid allocation total, category or source' USING ERRCODE='23514';END IF;
 IF t."ReversalOfId" IS NOT NULL AND EXISTS((SELECT "CategoryId","Amount" FROM transaction_allocations WHERE "OrganizationId"=org AND "TransactionId"=id EXCEPT SELECT "CategoryId","Amount" FROM transaction_allocations WHERE "OrganizationId"=org AND "TransactionId"=t."ReversalOfId") UNION ALL (SELECT "CategoryId","Amount" FROM transaction_allocations WHERE "OrganizationId"=org AND "TransactionId"=t."ReversalOfId" EXCEPT SELECT "CategoryId","Amount" FROM transaction_allocations WHERE "OrganizationId"=org AND "TransactionId"=id)) THEN RAISE EXCEPTION 'Reversal allocations differ' USING ERRCODE='23514';END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER allocation_total AFTER INSERT ON transaction_allocations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cfis_allocation_check();
CREATE CONSTRAINT TRIGGER transaction_allocation_total AFTER INSERT ON transactions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cfis_allocation_check();
CREATE FUNCTION cfis_v1_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Historical records are append-only' USING ERRCODE='23514';END $$;
CREATE TRIGGER allocation_immutable BEFORE UPDATE OR DELETE ON transaction_allocations FOR EACH ROW EXECUTE FUNCTION cfis_v1_immutable();
CREATE TRIGGER attachment_immutable BEFORE UPDATE OR DELETE ON attachments FOR EACH ROW EXECUTE FUNCTION cfis_v1_immutable();
ALTER TABLE attachments ADD CONSTRAINT ck_attachment_size CHECK (octet_length("Content") BETWEEN 1 AND 5242880);
ALTER TABLE attachments ADD CONSTRAINT ck_attachment_hash CHECK ("Sha256"=upper(encode(sha256("Content"),'hex')));
CREATE FUNCTION cfis_attachment_ref() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NOT (NEW."ResourceKind"='Transaction' AND EXISTS(SELECT 1 FROM transactions WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."ResourceId") OR NEW."ResourceKind"='Receivable' AND EXISTS(SELECT 1 FROM receivables WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."ResourceId") OR NEW."ResourceKind"='Payable' AND EXISTS(SELECT 1 FROM payables WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."ResourceId") OR NEW."ResourceKind"='Journal' AND EXISTS(SELECT 1 FROM journal_entries WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."ResourceId")) THEN RAISE EXCEPTION 'Attachment business reference invalid' USING ERRCODE='23514';END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachment_reference BEFORE INSERT ON attachments FOR EACH ROW EXECUTE FUNCTION cfis_attachment_ref();
