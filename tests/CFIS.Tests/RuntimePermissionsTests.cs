using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
public class RuntimePermissionsTests {
 [Fact]public async Task AppendOnlyRuntimeCanReverseButCannotTamper(){
 await using var database=new TestDatabase();await database.InitializeAsync();
 var role="cfis_test_role_"+Guid.NewGuid().ToString("N");
 var adminBuilder=new NpgsqlConnectionStringBuilder(database.ConnectionString);
 await using var admin=new NpgsqlConnection(adminBuilder.ConnectionString);await admin.OpenAsync();
 await using(var sql=new NpgsqlCommand($"CREATE ROLE {role} LOGIN PASSWORD 'Test-Role!123456'; GRANT USAGE ON SCHEMA public TO {role}; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO {role}; REVOKE UPDATE,DELETE,TRUNCATE ON transactions,audit_logs,receivables,payables,budgets,budget_items FROM {role}; GRANT UPDATE (\"Cancelled\",\"CancelledAt\") ON receivables,payables TO {role}; GRANT UPDATE (\"Status\",\"ActivatedAt\",\"ClosedAt\") ON budgets TO {role};",admin))await sql.ExecuteNonQueryAsync();
 try {
 var runtimeBuilder=new NpgsqlConnectionStringBuilder(database.ConnectionString){Username=role,Password="Test-Role!123456"};
 await using(var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(w=>w.UseSetting("ConnectionStrings:Database",runtimeBuilder.ConnectionString))){
 using var client=factory.CreateClient();
 var body=new{email=Guid.NewGuid()+"@test.local",password="Runtime-Test!123456"};
 (await client.PostAsJsonAsync("/api/v1/auth/register",body)).EnsureSuccessStatusCode();
 var token=await(await client.PostAsJsonAsync("/api/v1/auth/login",body)).Content.ReadFromJsonAsync<JsonElement>();
 client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.GetProperty("accessToken").GetString());
 var org=await(await client.PostAsJsonAsync("/api/v1/organizations",new{name="运行角色"})).Content.ReadFromJsonAsync<JsonElement>();
 var root="/api/v1/organizations/"+org.GetProperty("id").GetGuid();
 var account=await(await client.PostAsJsonAsync(root+"/accounts",new{name="运行账户",currency="CNY",kind="Cash"})).Content.ReadFromJsonAsync<JsonElement>();
 var posted=await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account.GetProperty("id").GetGuid(),kind="Opening",amount="10.0001",description="测试来源",businessDate="2026-10-01"});
 posted.EnsureSuccessStatusCode();var transaction=await posted.Content.ReadFromJsonAsync<JsonElement>();
 var reversed=await client.PostAsJsonAsync(root+"/transactions/"+transaction.GetProperty("id").GetGuid()+"/reverse",new{requestId=Guid.NewGuid(),reason="运行权限验证",businessDate="2026-10-02"});
 Assert.Equal(HttpStatusCode.Created,reversed.StatusCode);
 var partyResponse=await client.PostAsJsonAsync(root+"/counterparties",new{name="受限客户",kind="Customer"});
 partyResponse.EnsureSuccessStatusCode();var party=await partyResponse.Content.ReadFromJsonAsync<JsonElement>();
 var categoryResponse=await client.PostAsJsonAsync(root+"/categories",new{name="受限收入",kind="Income"});
 categoryResponse.EnsureSuccessStatusCode();var category=await categoryResponse.Content.ReadFromJsonAsync<JsonElement>();
 var docResponse=await client.PostAsJsonAsync(root+"/receivables",new{number="RUN-AR",counterpartyId=party.GetProperty("id").GetGuid(),categoryId=category.GetProperty("id").GetGuid(),currency="CNY",amount="1",issueDate="2026-10-01",dueDate="2026-10-02"});
 docResponse.EnsureSuccessStatusCode();var doc=await docResponse.Content.ReadFromJsonAsync<JsonElement>();var docId=doc.GetProperty("id").GetGuid();
 var settlementResponse=await client.PostAsJsonAsync(root+"/receivables/"+docId+"/settlements",new{requestId=Guid.NewGuid(),accountId=account.GetProperty("id").GetGuid(),amount="1",businessDate="2026-10-02"});
 settlementResponse.EnsureSuccessStatusCode();var settlement=await settlementResponse.Content.ReadFromJsonAsync<JsonElement>();
 (await client.PostAsJsonAsync(root+"/transactions/"+settlement.GetProperty("id").GetGuid()+"/reverse",new{requestId=Guid.NewGuid(),reason="受限结算回退",businessDate="2026-10-02"})).EnsureSuccessStatusCode();
 (await client.PostAsJsonAsync(root+"/receivables/"+docId+"/cancel",new{})).EnsureSuccessStatusCode();
 var expenseResponse=await client.PostAsJsonAsync(root+"/categories",new{name="受限费用",kind="Expense"});expenseResponse.EnsureSuccessStatusCode();var expenseId=(await expenseResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
 var budgetResponse=await client.PostAsJsonAsync(root+"/budgets",new{name="受限预算",currency="CNY",startDate="2026-10-01",endDate="2026-10-31",items=new[]{new{categoryId=expenseId,amount="10"}}});budgetResponse.EnsureSuccessStatusCode();var budgetId=(await budgetResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
 (await client.PostAsJsonAsync(root+"/budgets/"+budgetId+"/status",new{status="Active"})).EnsureSuccessStatusCode();
 (await client.PostAsJsonAsync(root+"/budgets/"+budgetId+"/status",new{status="Closed"})).EnsureSuccessStatusCode();
 await using var connection=new NpgsqlConnection(runtimeBuilder.ConnectionString);await connection.OpenAsync();
 foreach(var command in new[]{"UPDATE audit_logs SET \"Detail\"='tampered' WHERE false","DELETE FROM transactions WHERE false","UPDATE receivables SET \"Amount\"=1 WHERE false"}){
 await using var sql=new NpgsqlCommand(command,connection);
 var exception=await Assert.ThrowsAsync<PostgresException>(async()=>await sql.ExecuteNonQueryAsync());Assert.Equal("42501",exception.SqlState);
 }
 }
 }finally{
 NpgsqlConnection.ClearAllPools();
 await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",admin);await cleanup.ExecuteNonQueryAsync();
 }
 }
}

