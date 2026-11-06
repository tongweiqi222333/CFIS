using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var mode=args.ElementAtOrDefault(0)??"seed";
var resultDirectory=Path.GetFullPath(args.ElementAtOrDefault(1)??"performance/results");Directory.CreateDirectory(resultDirectory);
var contextPath=Path.Combine(resultDirectory,"context.json");
var adminText=Environment.GetEnvironmentVariable("CFIS_TEST_ADMIN")??throw new InvalidOperationException("CFIS_TEST_ADMIN required; only use the local test server");
var adminBuilder=new NpgsqlConnectionStringBuilder(adminText){Database="postgres"};
if(adminBuilder.Host is not("127.0.0.1" or "localhost"))throw new InvalidOperationException("Performance harness only permits local PostgreSQL");
if(mode=="seed"){
 var database="cfis_perf_"+Guid.NewGuid().ToString("N");var org=Guid.NewGuid();var other=Guid.NewGuid();var user=new IdentityUser{Id=Guid.NewGuid().ToString(),UserName="perf-"+Guid.NewGuid().ToString("N")+"@example.test",EmailConfirmed=true,SecurityStamp=Guid.NewGuid().ToString(),ConcurrencyStamp=Guid.NewGuid().ToString()};user.Email=user.UserName;user.NormalizedUserName=user.UserName.ToUpperInvariant();user.NormalizedEmail=user.NormalizedUserName;var password="Perf-"+Guid.NewGuid().ToString("N")+"!aA1";user.PasswordHash=new PasswordHasher<IdentityUser>().HashPassword(user,password);
 var bank=Guid.NewGuid();var revenue=Guid.NewGuid();var expense=Guid.NewGuid();var period=Guid.NewGuid();var incomeCategory=Guid.NewGuid();var expenseCategory=Guid.NewGuid();var accountId=Guid.Parse(Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("cfis-perf-bank-0")))) ;
 var context=new {Database=database,OrganizationId=org,OtherOrganizationId=other,AccountId=accountId,Email=user.Email,Password=password,LedgerAccountId=bank};
 await File.WriteAllTextAsync(contextPath,JsonSerializer.Serialize(context,new JsonSerializerOptions{WriteIndented=true}));
 await using var admin=new NpgsqlConnection(adminBuilder.ConnectionString);await admin.OpenAsync();await using(var create=new NpgsqlCommand("CREATE DATABASE "+database,admin))await create.ExecuteNonQueryAsync();
 Console.WriteLine("Created isolated database: "+database);
 var builder=new NpgsqlConnectionStringBuilder(adminText){Database=database,Timeout=10,CommandTimeout=600};
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(builder.ConnectionString).Options);await db.Database.MigrateAsync();
 db.Users.Add(user);db.Organizations.AddRange(new Organization{Id=org,Name="Phase5性能验收测试（非业务数据）"},new Organization{Id=other,Name="Phase5背景数据企业（非业务数据）"});db.Members.Add(new(){OrganizationId=org,UserId=user.Id,Role=MemberRole.Owner});
 db.AddRange(new Category{Id=incomeCategory,OrganizationId=org,Name="性能收入",Kind="Income"},new Category{Id=expenseCategory,OrganizationId=org,Name="性能费用",Kind="Expense"});
 db.AddRange(new LedgerAccount{Id=bank,OrganizationId=org,Code="1002",Name="性能银行",Kind="Asset"},new LedgerAccount{Id=revenue,OrganizationId=org,Code="6001",Name="性能收入",Kind="Revenue"},new LedgerAccount{Id=expense,OrganizationId=org,Code="6401",Name="性能费用",Kind="Expense"});
 db.Add(new FiscalPeriod{Id=period,OrganizationId=org,Code="PERF2026",StartDate=new(2026,1,1),EndDate=new(2026,12,31)});await db.SaveChangesAsync();
 await using var connection=new NpgsqlConnection(builder.ConnectionString);await connection.OpenAsync();
 async Task Execute(string sql){await using var command=new NpgsqlCommand(sql,connection){CommandTimeout=600};command.Parameters.AddWithValue("org",org);command.Parameters.AddWithValue("other",other);command.Parameters.AddWithValue("actor",user.Id);command.Parameters.AddWithValue("bank",bank);command.Parameters.AddWithValue("revenue",revenue);command.Parameters.AddWithValue("expense",expense);command.Parameters.AddWithValue("period",period);command.Parameters.AddWithValue("incomeCategory",incomeCategory);command.Parameters.AddWithValue("expenseCategory",expenseCategory);await command.ExecuteNonQueryAsync();}
 Console.WriteLine("Seeding 100,000 target transactions plus 100,000 background transactions...");
 await Execute("""
 INSERT INTO financial_accounts ("Id","OrganizationId","Name","Currency","Status","Kind","Balance") SELECT md5('cfis-perf-bank-'||i)::uuid,@org,'性能银行'||i,'CNY','Active','Bank',2500 FROM generate_series(0,99) i;
 INSERT INTO financial_accounts ("Id","OrganizationId","Name","Currency","Status","Kind","Balance") VALUES (md5('cfis-perf-other-bank')::uuid,@other,'背景银行','CNY','Active','Bank',100000);
 INSERT INTO transactions ("Id","OrganizationId","RequestId","Kind","AccountId","CategoryId","Amount","Description","ActorId","BusinessDate","CreatedAt")
 SELECT md5('cfis-perf-tx-'||i)::uuid,@org,md5('cfis-perf-request-'||i)::uuid,CASE WHEN ((i-1)/100)%2=0 THEN 'Income' ELSE 'Expense' END,md5('cfis-perf-bank-'||((i-1)%100))::uuid,CASE WHEN ((i-1)/100)%2=0 THEN @incomeCategory ELSE @expenseCategory END,CASE WHEN ((i-1)/100)%2=0 THEN 10 ELSE 5 END,'性能验收流水 '||i,@actor,DATE '2026-01-01'+(((i-1)/100)%365)::integer,now()-interval '1 day'+i*interval '0.1 second' FROM generate_series(1,100000) i;
 INSERT INTO transactions ("Id","OrganizationId","RequestId","Kind","AccountId","Amount","Description","ActorId","BusinessDate","CreatedAt") SELECT md5('cfis-perf-other-tx-'||i)::uuid,@other,gen_random_uuid(),'Income',md5('cfis-perf-other-bank')::uuid,1,'背景测试数据',@actor,DATE '2026-01-01'+(i%365)::integer,now()-interval '1 day' FROM generate_series(1,100000) i;
 INSERT INTO accounting_mappings ("Id","OrganizationId","Kind","ResourceId","Currency","LedgerAccountId") SELECT gen_random_uuid(),@org,'FinancialAccount',"Id",'CNY',@bank FROM financial_accounts WHERE "OrganizationId"=@org;
 INSERT INTO audit_logs ("Id","OrganizationId","ActorId","Action","ResourceId","Detail","CreatedAt") SELECT gen_random_uuid(),"OrganizationId",@actor,'PerformanceSeed',"Id",'明确标识性能验收测试数据',now() FROM transactions;
 """);
 Console.WriteLine("Seeding 100,000 balanced posted journals and 200,000 lines (database guards enabled)...");
 await Execute("""
 INSERT INTO journal_entries ("Id","OrganizationId","Version","RequestId","RequestHash","PeriodId","BusinessDate","Currency","Description","Status","ActorId","CreatedAt","DebitTotal","CreditTotal","SourceTransactionId") SELECT md5('cfis-perf-je-'||t."Id"::text)::uuid,@org,gen_random_uuid(),gen_random_uuid(),md5(t."Id"::text)||md5(t."Id"::text),@period,t."BusinessDate",'CNY','性能来源凭证','Draft',@actor,t."CreatedAt",0,0,t."Id" FROM transactions t WHERE t."OrganizationId"=@org;
 INSERT INTO journal_lines ("Id","OrganizationId","EntryId","LedgerAccountId","Position","Debit","Credit") SELECT gen_random_uuid(),@org,e."Id",CASE WHEN t."Kind"='Income' THEN @bank ELSE @expense END,1,t."Amount",0 FROM journal_entries e JOIN transactions t ON t."OrganizationId"=e."OrganizationId" AND t."Id"=e."SourceTransactionId" WHERE e."OrganizationId"=@org;
 INSERT INTO journal_lines ("Id","OrganizationId","EntryId","LedgerAccountId","Position","Debit","Credit") SELECT gen_random_uuid(),@org,e."Id",CASE WHEN t."Kind"='Income' THEN @revenue ELSE @bank END,2,0,t."Amount" FROM journal_entries e JOIN transactions t ON t."OrganizationId"=e."OrganizationId" AND t."Id"=e."SourceTransactionId" WHERE e."OrganizationId"=@org;
 UPDATE journal_entries e SET "Status"='Posted',"Number"='PERF-'||e."Id"::text,"PostedAt"=now(),"DebitTotal"=t."Amount","CreditTotal"=t."Amount" FROM transactions t WHERE t."OrganizationId"=@org AND e."OrganizationId"=@org AND t."Id"=e."SourceTransactionId";
 UPDATE fiscal_periods SET "NextNumber"=100000 WHERE "Id"=@period;
 INSERT INTO audit_logs ("Id","OrganizationId","ActorId","Action","ResourceId","Detail","CreatedAt") SELECT gen_random_uuid(),@org,@actor,'PerformanceJournalPosted',"Id",'性能凭证已验证借贷平衡',now() FROM journal_entries WHERE "OrganizationId"=@org;
 """);
 await Execute("VACUUM (ANALYZE) transactions");await Execute("VACUUM (ANALYZE) journal_entries");await Execute("VACUUM (ANALYZE) journal_lines");
 Console.WriteLine("Performance fixture ready; credentials saved only under ignored results directory.");
}else if(mode=="plan"){
 var context=JsonDocument.Parse(await File.ReadAllTextAsync(contextPath)).RootElement;var name=context.GetProperty("Database").GetString()!;if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^cfis_perf_[a-f0-9]{32}$"))throw new InvalidOperationException("Unexpected database");var builder=new NpgsqlConnectionStringBuilder(adminText){Database=name,CommandTimeout=120};await using var connection=new NpgsqlConnection(builder.ConnectionString);await connection.OpenAsync();var org=context.GetProperty("OrganizationId").GetGuid();var account=context.GetProperty("AccountId").GetGuid();var label=args.ElementAtOrDefault(2)??"current";
 var queries=new Dictionary<string,string>{["account-transactions"]="""
 SELECT "Id","BusinessDate","Amount","Kind" FROM transactions WHERE "OrganizationId"=@org AND "AccountId"=@account AND "BusinessDate" BETWEEN DATE '2026-10-01' AND DATE '2026-10-31' ORDER BY "BusinessDate" DESC,"Id" DESC LIMIT 50
 """,["trial-balance"]="""
 SELECT l."LedgerAccountId",sum(l."Debit"),sum(l."Credit") FROM journal_lines l JOIN journal_entries e ON e."OrganizationId"=l."OrganizationId" AND e."Id"=l."EntryId" WHERE l."OrganizationId"=@org AND e."Currency"='CNY' AND e."Status"='Posted' AND e."BusinessDate"<=DATE '2026-12-31' GROUP BY l."LedgerAccountId"
 """};
 foreach(var pair in queries){await using var command=new NpgsqlCommand("EXPLAIN (ANALYZE,BUFFERS,FORMAT JSON) "+pair.Value,connection);command.Parameters.AddWithValue("org",org);command.Parameters.AddWithValue("account",account);var plan=(string)(await command.ExecuteScalarAsync())!;await File.WriteAllTextAsync(Path.Combine(resultDirectory,label+"-"+pair.Key+".json"),plan);Console.WriteLine("Saved actual EXPLAIN: "+label+"-"+pair.Key);}
}else throw new ArgumentException("Use seed or plan");
