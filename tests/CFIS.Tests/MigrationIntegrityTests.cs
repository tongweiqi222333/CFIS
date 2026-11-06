using CFIS.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
public class MigrationIntegrityTests {
 [Fact]public async Task LegacyDateAndStatusAreBackfilled(){
 await using var database=new TestDatabase();await database.InitializeAsync();
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 var migrator=db.GetService<IMigrator>();
 await migrator.MigrateAsync("20261029194450_CoreFinance");
 var org=Guid.NewGuid();var account=Guid.NewGuid();var transaction=Guid.NewGuid();var request=Guid.NewGuid();
 await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO organizations (\"Id\",\"Name\",\"CreatedAt\") VALUES ({org},'legacy',now())");
 await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO financial_accounts (\"Id\",\"OrganizationId\",\"Name\",\"Currency\",\"Kind\",\"Balance\") VALUES ({account},{org},'legacy','CNY','Bank',1)");
 await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO transactions (\"Id\",\"OrganizationId\",\"RequestId\",\"Kind\",\"AccountId\",\"Amount\",\"Description\",\"ActorId\",\"CreatedAt\") VALUES ({transaction},{org},{request},'Income',{account},1,'legacy','legacy','2026-10-01 16:30:00+00')");
 await migrator.MigrateAsync();
 Assert.Equal(new DateOnly(2026,10,2),await db.Set<FinancialTransaction>().Select(x=>x.BusinessDate).SingleAsync());
 Assert.Equal("Active",await db.Set<FinancialAccount>().Select(x=>x.Status).SingleAsync());
 Assert.Equal("Asia/Shanghai",await db.Organizations.Select(x=>x.TimeZone).SingleAsync());
 }
}
