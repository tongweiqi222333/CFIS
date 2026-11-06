using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CFIS.Api.Migrations;
[DbContext(typeof(AppDb))]
[Migration("20261101040000_AcceptanceGuards")]
public sealed class AcceptanceGuards : Migration {
 protected override void Up(MigrationBuilder migrationBuilder){
  using var stream=typeof(AccountingModel).Assembly.GetManifestResourceStream("CFIS.Api.Database.acceptance-guards.sql")!;
  using var reader=new System.IO.StreamReader(stream);migrationBuilder.Sql(reader.ReadToEnd());
 }
 protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("DROP FUNCTION cfis_source_guard() CASCADE; DROP FUNCTION cfis_close_guard() CASCADE;");
}
