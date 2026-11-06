using CFIS.Api;
using Microsoft.EntityFrameworkCore;
using Npgsql;
public sealed class TestDatabase:IAsyncDisposable {
 readonly string name="cfis_test_"+Guid.NewGuid().ToString("N");
 readonly string admin=Environment.GetEnvironmentVariable("CFIS_TEST_ADMIN")??throw new InvalidOperationException("Set CFIS_TEST_ADMIN to a local PostgreSQL connection with CREATEDB privilege");
 public string ConnectionString {get;private set;}="";
 public async Task InitializeAsync(){
 var builder=new NpgsqlConnectionStringBuilder(admin){Database="postgres"};
 await using var c=new NpgsqlConnection(builder.ConnectionString);await c.OpenAsync();
 await using(var cmd=new NpgsqlCommand($"CREATE DATABASE {name}",c))await cmd.ExecuteNonQueryAsync();
 builder.Database=name;ConnectionString=builder.ConnectionString;
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(ConnectionString).Options);
 await db.Database.MigrateAsync();
 }
 public async ValueTask DisposeAsync(){
 NpgsqlConnection.ClearAllPools();
 var b=new NpgsqlConnectionStringBuilder(admin){Database="postgres"};
 await using var c=new NpgsqlConnection(b.ConnectionString);await c.OpenAsync();
 await using var cmd=new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)",c);
 await cmd.ExecuteNonQueryAsync();
 }
}
