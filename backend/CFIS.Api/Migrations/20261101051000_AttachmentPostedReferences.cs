using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CFIS.Api.Migrations;
[DbContext(typeof(AppDb))][Migration("20261101051000_AttachmentPostedReferences")]
public sealed class AttachmentPostedReferences:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE FUNCTION cfis_attachment_posted_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
         IF NEW."ResourceKind"='Journal' AND NOT EXISTS(SELECT 1 FROM journal_entries WHERE "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."ResourceId" AND "Status"='Posted') THEN RAISE EXCEPTION 'Journal attachments require a posted entry' USING ERRCODE='23514';END IF;
         RETURN NEW;
        END $$;
        CREATE TRIGGER attachment_posted_guard BEFORE INSERT ON attachments FOR EACH ROW EXECUTE FUNCTION cfis_attachment_posted_guard();
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("DROP FUNCTION cfis_attachment_posted_guard() CASCADE;");
}
