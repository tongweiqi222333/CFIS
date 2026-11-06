using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CFIS.Api.Migrations;

[DbContext(typeof(AppDb))]
[Migration("20261101050000_V1Guards")]
public sealed class V1Guards : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        using var s = typeof(AppDb).Assembly.GetManifestResourceStream("CFIS.Api.Database.v1-guards.sql")!;
        using var r = new StreamReader(s);
        m.Sql(r.ReadToEnd());
    }
    protected override void Down(MigrationBuilder m) => m.Sql("DROP VIEW category_transactions;DROP FUNCTION cfis_allocation_check() CASCADE;DROP FUNCTION cfis_allocation_insert() CASCADE;DROP FUNCTION cfis_v1_immutable() CASCADE;DROP FUNCTION cfis_attachment_ref() CASCADE;ALTER TABLE attachments DROP CONSTRAINT ck_attachment_size;ALTER TABLE attachments DROP CONSTRAINT ck_attachment_hash;");
}
