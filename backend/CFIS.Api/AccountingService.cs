using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static CFIS.Api.Finance;
namespace CFIS.Api;

public record LedgerAccountInput(string Code, string Name, string Kind, string Currency, Guid? ParentId = null);
public record LedgerStatusInput(bool? Active);
public record PeriodInput(string Code, DateOnly StartDate, DateOnly EndDate);
public record MappingInput(string Kind, Guid ResourceId, string Currency, Guid LedgerAccountId);
public record JournalLineInput(Guid LedgerAccountId, string Debit, string Credit);
public record JournalInput(Guid RequestId, DateOnly BusinessDate, string Currency, string Description, JournalLineInput[] Lines);
public record JournalEditInput(Guid Version, DateOnly BusinessDate, string Description, JournalLineInput[] Lines);
public record JournalPostInput(Guid Version);
public record JournalImportInput(Guid RequestId, string SourceKind, Guid SourceId);
public record JournalReverseInput(Guid RequestId, DateOnly BusinessDate, string Reason);
public sealed class AccountingFault(int status, string message) : Exception(message)
{
    public int Status => status;
}
public sealed class AccountingService(AppDb db)
{
    public static Task Lock(AppDb db, Guid org)
    {
        var key = "cfis-accounting:" + org;
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))");
    }
    public static Task LockFinancial(AppDb db, Guid org)
    {
        var key = "cfis-accounting:" + org;
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({key},0))");
    }
    public static async Task<bool> DateOpen(AppDb db, Guid org, DateOnly date)
    {
        var periods = db.Set<FiscalPeriod>().Where(x => x.OrganizationId == org);
        return !await periods.AnyAsync() || await periods.AnyAsync(x => x.StartDate <= date && x.EndDate >= date && x.Status == "Open");
    }
    static void Need(bool test, int status, string message)
    {
        if (!test)
            throw new AccountingFault(status, message);
    }
    static decimal Amount(string? text)
    {
        Need(text != null && System.Text.RegularExpressions.Regex.IsMatch(text, @"^[0-9]{1,15}(\.[0-9]{1,4})?$"), 400, "分录金额须为最多4位小数的非负字符串");
        var value = decimal.Parse(text!, CultureInfo.InvariantCulture);
        Need(value <= Maximum, 400, "分录金额超限");
        return value;
    }
    static string Hash(object input) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(input))));
    async Task<FiscalPeriod> Period(Guid org, DateOnly date)
    {
        Need(date != default, 400, "凭证业务日期无效");
        return await db.Set<FiscalPeriod>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Status == "Open" && x.StartDate <= date && x.EndDate >= date) ?? throw new AccountingFault(409, "请先创建覆盖该日期的开放会计期间");
    }
    public async Task<LedgerAccount> CreateAccount(Guid org, LedgerAccountInput r, ClaimsPrincipal user)
    {
        Need(!string.IsNullOrWhiteSpace(r.Code) && System.Text.RegularExpressions.Regex.IsMatch(r.Code, "^[A-Za-z0-9.-]{1,30}$") && !string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 200 && r.Kind is "Asset" or "Liability" or "Equity" or "Revenue" or "Expense" && System.Text.RegularExpressions.Regex.IsMatch(r.Currency ?? "", "^[A-Z]{3}$"), 400, "科目代码、名称、类别或币种无效");
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        if (r.ParentId is Guid parent)
        {
            Need(await db.Set<LedgerAccount>().AnyAsync(x => x.OrganizationId == org && x.Id == parent && x.Active && x.Kind == r.Kind && x.Currency == r.Currency), 400, "上级科目不存在、停用或类别币种不同");
            Need(!await db.Set<JournalLine>().AnyAsync(x => x.OrganizationId == org && x.LedgerAccountId == parent) && !await db.Set<AccountingMapping>().AnyAsync(x => x.OrganizationId == org && x.LedgerAccountId == parent), 409, "已有明细用途的科目不能增加下级科目");
        }
        Need(!await db.Set<LedgerAccount>().AnyAsync(x => x.OrganizationId == org && x.Code == r.Code && x.Currency == r.Currency), 409, "科目代码已存在");
        var a = new LedgerAccount { OrganizationId = org, Code = r.Code, Name = r.Name.Trim(), Kind = r.Kind, Currency = r.Currency!, ParentId = r.ParentId };
        db.Add(a);
        db.Add(Audit(org, user, "LedgerAccountCreated", a.Id, Hash(r)));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return a;
    }
    public async Task SetAccountStatus(Guid org, Guid id, bool active, ClaimsPrincipal user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var a = await db.Set<LedgerAccount>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "科目不存在");
        if (!active)
            Need(!await db.Set<LedgerAccount>().AnyAsync(x => x.OrganizationId == org && x.ParentId == id && x.Active), 409, "请先停用下级科目");
        else if (a.ParentId is Guid parent)
            Need(await db.Set<LedgerAccount>().AnyAsync(x => x.OrganizationId == org && x.Id == parent && x.Active), 409, "请先启用上级科目");
        a.Active = active;
        db.Add(Audit(org, user, "LedgerAccountStatus", id, active.ToString()));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    public async Task<FiscalPeriod> CreatePeriod(Guid org, PeriodInput r, ClaimsPrincipal user)
    {
        Need(System.Text.RegularExpressions.Regex.IsMatch(r.Code ?? "", "^[A-Za-z0-9-]{1,20}$") && r.StartDate != default && r.EndDate >= r.StartDate && r.EndDate.DayNumber - r.StartDate.DayNumber <= 366, 400, "期间代码无效或范围超过367天");
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        Need(!await db.Set<FiscalPeriod>().AnyAsync(x => x.OrganizationId == org && (x.Code == r.Code || x.StartDate <= r.EndDate && x.EndDate >= r.StartDate)), 409, "会计期间重叠或代码重复");
        var p = new FiscalPeriod { OrganizationId = org, Code = r.Code!, StartDate = r.StartDate, EndDate = r.EndDate };
        db.Add(p);
        db.Add(Audit(org, user, "FiscalPeriodCreated", p.Id, Hash(r)));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return p;
    }
    public async Task<object> PeriodPrecheck(Guid org, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var result = await CheckPeriod(org, id);
        await tx.CommitAsync();
        return result;
    }
    async Task<PeriodCheck> CheckPeriod(Guid org, Guid id)
    {
        var p = await db.Set<FiscalPeriod>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "会计期间不存在");
        var drafts = await db.Set<JournalEntry>().CountAsync(x => x.OrganizationId == org && x.PeriodId == id && x.Status == "Draft");
        var missingCash = await db.Set<FinancialTransaction>().CountAsync(t => t.OrganizationId == org && t.BusinessDate <= p.EndDate && !db.Set<JournalEntry>().Any(e => e.OrganizationId == org && e.SourceTransactionId == t.Id && e.Status == "Posted"));
        var missingAr = await db.Set<Receivable>().CountAsync(d => d.OrganizationId == org && !d.Cancelled && d.IssueDate <= p.EndDate && !db.Set<JournalEntry>().Any(e => e.OrganizationId == org && e.SourceReceivableId == d.Id && e.Status == "Posted" && !db.Set<JournalEntry>().Any(r => r.OrganizationId == org && r.ReversalOfId == e.Id && r.Status == "Posted")));
        var missingAp = await db.Set<Payable>().CountAsync(d => d.OrganizationId == org && !d.Cancelled && d.IssueDate <= p.EndDate && !db.Set<JournalEntry>().Any(e => e.OrganizationId == org && e.SourcePayableId == d.Id && e.Status == "Posted" && !db.Set<JournalEntry>().Any(r => r.OrganizationId == org && r.ReversalOfId == e.Id && r.Status == "Posted")));
        var differences = 0;
        var unmapped = 0;
        foreach (var currency in await db.Set<FinancialAccount>().Where(x => x.OrganizationId == org).Select(x => x.Currency).Distinct().ToListAsync())
        {
            var result = System.Text.Json.JsonSerializer.SerializeToElement(await new AccountingReports(db).Reconcile(org, p.EndDate, currency));
            unmapped += result.GetProperty("unmappedAccounts").GetInt32();
            differences += result.GetProperty("items").EnumerateArray().Count(x => x.GetProperty("difference").GetString() != "0.0000");
        }
        return new(p.Id, p.Status, drafts, missingCash, missingAr, missingAp, unmapped, differences);
    }
    public record PeriodCheck(Guid PeriodId, string Status, int Drafts, int MissingCash, int MissingReceivables, int MissingPayables, int UnmappedAccounts, int Differences)
    {
        public bool CanClose => Status == "Open" && Drafts + MissingCash + MissingReceivables + MissingPayables + UnmappedAccounts + Differences == 0;
    }
    public async Task<object> ClosePeriod(Guid org, Guid id, ClaimsPrincipal user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var p = await db.Set<FiscalPeriod>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "会计期间不存在");
        if (p.Status == "Closed")
            return new
            {
                p.Id,
                p.Status
            };
        var check = await CheckPeriod(org, id);
        Need(check.CanClose, 409, $"结账检查未通过：草稿{check.Drafts}、资金{check.MissingCash}、应收{check.MissingReceivables}、应付{check.MissingPayables}、未映射{check.UnmappedAccounts}、差异{check.Differences}");
        p.Status = "Closed";
        p.ClosedAt = DateTimeOffset.UtcNow;
        db.Add(Audit(org, user, "FiscalPeriodClosed", id, p.Code));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new
        {
            p.Id,
            p.Status
        };
    }
    public async Task<AccountingMapping> SetMapping(Guid org, MappingInput r, ClaimsPrincipal user)
    {
        Need(r.Kind is "FinancialAccount" or "Category" or "Opening" or "Receivable" or "Payable", 400, "映射类型无效");
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var a = await db.Set<LedgerAccount>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == r.LedgerAccountId && x.Active && x.Currency == r.Currency) ?? throw new AccountingFault(400, "映射科目不存在、停用或币种不符");
        Need(!await db.Set<LedgerAccount>().AnyAsync(x => x.OrganizationId == org && x.ParentId == a.Id), 400, "仅可映射明细科目");
        var expected = r.Kind switch
        {
            "Opening" => "Equity",
            "Receivable" => "Asset",
            "Payable" => "Liability",
            _ => "Asset"
        };
        if (r.Kind == "FinancialAccount")
        {
            Need(await db.Set<FinancialAccount>().AnyAsync(x => x.OrganizationId == org && x.Id == r.ResourceId && x.Currency == r.Currency), 400, "资金账户不存在或币种不同");
        }
        else if (r.Kind == "Category")
        {
            var category = await db.Set<Category>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == r.ResourceId) ?? throw new AccountingFault(400, "分类不存在");
            expected = category.Kind == "Income" ? "Revenue" : "Expense";
        }
        else
            Need(r.ResourceId == Guid.Empty, 400, "控制科目映射的资源编号须为全零UUID");
        Need(a.Kind == expected, 400, "科目类别与映射用途不符");
        Need(!await db.Set<AccountingMapping>().AnyAsync(x => x.OrganizationId == org && x.LedgerAccountId == r.LedgerAccountId && ((r.Kind == "FinancialAccount" && x.Kind == "Receivable") || (r.Kind == "Receivable" && x.Kind == "FinancialAccount"))), 409, "资金科目与应收控制科目须分开");
        var existing = await db.Set<AccountingMapping>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Kind == r.Kind && x.ResourceId == r.ResourceId && x.Currency == r.Currency);
        if (existing != null && existing.LedgerAccountId != r.LedgerAccountId && r.Kind == "FinancialAccount")
            Need(!await db.Set<JournalEntry>().AnyAsync(e => e.OrganizationId == org && e.Status == "Posted" && db.Set<FinancialTransaction>().Any(t => t.OrganizationId == org && t.Id == e.SourceTransactionId && (t.AccountId == r.ResourceId || t.DestinationAccountId == r.ResourceId))), 409, "已有过账资金凭证，不能更换该账户映射");
        var mapping = existing ?? new AccountingMapping { OrganizationId = org, Kind = r.Kind, ResourceId = r.ResourceId, Currency = r.Currency };
        if (existing == null)
            db.Add(mapping);
        mapping.LedgerAccountId = r.LedgerAccountId;
        db.Add(Audit(org, user, "AccountingMappingChanged", mapping.Id, System.Text.Json.JsonSerializer.Serialize(r)));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return mapping;
    }
    async Task<List<JournalLine>> ValidateLines(Guid org, Guid entryId, string currency, JournalLineInput[] input)
    {
        Need(input != null && input.Length is >= 2 and <= 200 && input.All(x => x != null), 400, "凭证须有2至200行");
        var lines = new List<JournalLine>();
        foreach (var line in input!)
        {
            var debit = Amount(line.Debit);
            var credit = Amount(line.Credit);
            Need(debit > 0 && credit == 0 || credit > 0 && debit == 0, 400, "每行仅能填写借方或贷方正金额");
            lines.Add(new()
            {
                OrganizationId = org,
                EntryId = entryId,
                LedgerAccountId = line.LedgerAccountId,
                Position = lines.Count + 1,
                Debit = debit,
                Credit = credit
            });
        }
        var total = lines.Sum(x => x.Debit);
        Need(total > 0 && total <= Maximum && total == lines.Sum(x => x.Credit), 400, "凭证借贷不平衡或合计超限");
        var ids = lines.Select(x => x.LedgerAccountId).Distinct().ToArray();
        var accounts = await db.Set<LedgerAccount>().Where(x => x.OrganizationId == org && ids.Contains(x.Id) && x.Active && x.Currency == currency && !db.Set<LedgerAccount>().Any(c => c.OrganizationId == org && c.ParentId == x.Id)).Select(x => x.Id).ToArrayAsync();
        Need(accounts.Length == ids.Length, 400, "分录须引用本企业同币种有效明细科目");
        return lines;
    }
    async Task<JournalEntry> DraftCore(Guid org, JournalInput r, ClaimsPrincipal user, Guid? cash = null, Guid? ar = null, Guid? ap = null, Guid? reversal = null, string? requestHash = null)
    {
        Need(r.RequestId != Guid.Empty && !string.IsNullOrWhiteSpace(r.Description) && r.Description.Length <= 500 && System.Text.RegularExpressions.Regex.IsMatch(r.Currency ?? "", "^[A-Z]{3}$"), 400, "请求编号、摘要或币种无效");
        var hash = requestHash ?? Hash(new
        {
            input = r,
            cash,
            ar,
            ap,
            reversal
        });
        var old = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.RequestId == r.RequestId);
        if (old != null)
        {
            Need(old.RequestHash == hash, 409, "请求编号已用于不同凭证");
            return old;
        }
        var period = await Period(org, r.BusinessDate);
        var entry = new JournalEntry { OrganizationId = org, RequestId = r.RequestId, RequestHash = hash, PeriodId = period.Id, BusinessDate = r.BusinessDate, Currency = r.Currency!, Description = r.Description.Trim(), ActorId = Uid(user), SourceTransactionId = cash, SourceReceivableId = ar, SourcePayableId = ap, ReversalOfId = reversal };
        var lines = await ValidateLines(org, entry.Id, entry.Currency, r.Lines);
        db.Add(entry);
        await db.SaveChangesAsync();
        db.AddRange(lines);
        db.Add(Audit(org, user, "JournalDraftCreated", entry.Id, hash));
        await db.SaveChangesAsync();
        return entry;
    }
    public async Task<JournalEntry> CreateDraft(Guid org, JournalInput r, ClaimsPrincipal user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var e = await DraftCore(org, r, user);
        await tx.CommitAsync();
        return e;
    }
    public async Task<JournalEntry> Edit(Guid org, Guid id, JournalEditInput r, ClaimsPrincipal user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var e = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "凭证不存在");
        Need(e.Status == "Draft" && e.SourceTransactionId == null && e.SourceReceivableId == null && e.SourcePayableId == null && e.ReversalOfId == null, 409, "仅可编辑手工草稿，来源凭证请删除草稿后重新生成");
        Need(e.Version == r.Version, 409, "草稿已变化，请刷新后重试");
        Need(!string.IsNullOrWhiteSpace(r.Description) && r.Description.Length <= 500, 400, "摘要无效");
        var period = await Period(org, r.BusinessDate);
        var lines = await ValidateLines(org, id, e.Currency, r.Lines);
        var previous = await db.Set<JournalLine>().Where(x => x.OrganizationId == org && x.EntryId == id).ToListAsync();
        db.RemoveRange(previous);
        await db.SaveChangesAsync();
        db.AddRange(lines);
        e.BusinessDate = r.BusinessDate;
        e.PeriodId = period.Id;
        e.Description = r.Description.Trim();
        e.Version = Guid.NewGuid();
        db.Add(Audit(org, user, "JournalDraftEdited", id, e.Version.ToString()));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return e;
    }
    public async Task DeleteDraft(Guid org, Guid id, ClaimsPrincipal user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var e = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "凭证不存在");
        Need(e.Status == "Draft", 409, "已过账凭证不可删除");
        db.Add(Audit(org, user, "JournalDraftDeleted", id, System.Text.Json.JsonSerializer.Serialize(e)));
        db.RemoveRange(await db.Set<JournalLine>().Where(x => x.OrganizationId == org && x.EntryId == id).ToListAsync());
        await db.SaveChangesAsync();
        db.Remove(e);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    async Task PostCore(Guid org, JournalEntry e)
    {
        if (e.Status == "Posted")
            return;
        if (e.SourceReceivableId is Guid ar)
            Need(await db.Set<Receivable>().AnyAsync(x => x.OrganizationId == org && x.Id == ar && !x.Cancelled), 409, "来源应收已取消");
        if (e.SourcePayableId is Guid ap)
            Need(await db.Set<Payable>().AnyAsync(x => x.OrganizationId == org && x.Id == ap && !x.Cancelled), 409, "来源应付已取消");
        var period = await Period(org, e.BusinessDate);
        Need(period.Id == e.PeriodId, 409, "凭证期间不符");
        var lines = await db.Set<JournalLine>().Where(x => x.OrganizationId == org && x.EntryId == e.Id).OrderBy(x => x.Position).ToListAsync();
        await ValidateLines(org, e.Id, e.Currency, lines.Select(x => new JournalLineInput(x.LedgerAccountId, Money(x.Debit), Money(x.Credit))).ToArray());
        e.DebitTotal = lines.Sum(x => x.Debit);
        e.CreditTotal = lines.Sum(x => x.Credit);
        period.NextNumber = checked(period.NextNumber + 1);
        e.Number = $"JE-{period.Code}-{period.NextNumber:D8}";
        e.PostedAt = DateTimeOffset.UtcNow;
        e.Status = "Posted";
        await db.SaveChangesAsync();
    }
    public async Task<JournalEntry> Post(Guid org, Guid id, JournalPostInput r, ClaimsPrincipal user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var e = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "凭证不存在");
        Need(e.Version == r.Version, 409, "草稿版本已变化");
        var posted = e.Status == "Posted";
        await PostCore(org, e);
        if (!posted)
        {
            db.Add(Audit(org, user, "JournalPosted", id, e.Number!));
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return e;
    }
    async Task<Guid> Map(Guid org, string kind, Guid resource, string currency) => await db.Set<AccountingMapping>().Where(x => x.OrganizationId == org && x.Kind == kind && x.ResourceId == resource && x.Currency == currency).Select(x => (Guid?)x.LedgerAccountId).SingleOrDefaultAsync() ?? throw new AccountingFault(409, $"缺少{kind}科目映射：{resource} / {currency}");
    public async Task<JournalEntry> Import(Guid org, JournalImportInput r, ClaimsPrincipal user)
    {
        Need(r.RequestId != Guid.Empty && r.SourceKind is "Transaction" or "Receivable" or "Payable", 400, "来源类型或请求编号无效");
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var existing = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && (r.SourceKind == "Transaction" ? x.SourceTransactionId == r.SourceId : r.SourceKind == "Receivable" ? x.SourceReceivableId == r.SourceId : x.SourcePayableId == r.SourceId));
        if (existing != null)
        {
            var used = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.RequestId == r.RequestId);
            Need(used == null || used.Id == existing.Id, 409, "请求编号已用于其他凭证");
            return existing;
        }
        Guid? cash = null, ar = null, ap = null, reversal = null;
        DateOnly date;
        string currency, description;
        var lines = new List<JournalLineInput>();
        void Pair(Guid debit, Guid credit, decimal amount)
        {
            lines.Add(new(debit, Money(amount), "0"));
            lines.Add(new(credit, "0", Money(amount)));
        }
        if (r.SourceKind == "Transaction")
        {
            var t = await db.Set<FinancialTransaction>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == r.SourceId) ?? throw new AccountingFault(404, "资金流水不存在");
            cash = t.Id;
            date = t.BusinessDate;
            currency = await db.Set<FinancialAccount>().Where(x => x.OrganizationId == org && x.Id == t.AccountId).Select(x => x.Currency).SingleAsync();
            description = "资金流水 " + t.Id + " " + t.Description;
            if (t.ReversalOfId is Guid original)
            {
                var prior = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.SourceTransactionId == original && x.Status == "Posted") ?? throw new AccountingFault(409, "请先过账原资金流水凭证");
                Need(!await db.Set<JournalEntry>().AnyAsync(x => x.OrganizationId == org && x.ReversalOfId == prior.Id), 409, "原凭证已有冲销草稿或冲销凭证");
                reversal = prior.Id;
                lines = await db.Set<JournalLine>().Where(x => x.OrganizationId == org && x.EntryId == prior.Id).OrderBy(x => x.Position).Select(x => new JournalLineInput(x.LedgerAccountId, Money(x.Credit), Money(x.Debit))).ToListAsync();
            }
            else
            {
                var bank = await Map(org, "FinancialAccount", t.AccountId, currency);
                if (t.Kind == "Transfer")
                    Pair(await Map(org, "FinancialAccount", t.DestinationAccountId!.Value, currency), bank, t.Amount);
                else if (t.Kind == "Opening")
                    Pair(bank, await Map(org, "Opening", Guid.Empty, currency), t.Amount);
                else if (t.ReceivableId == null && t.PayableId == null && await db.Set<TransactionAllocation>().AnyAsync(x => x.OrganizationId == org && x.TransactionId == t.Id))
                {
                    foreach (var part in await db.Set<TransactionAllocation>().Where(x => x.OrganizationId == org && x.TransactionId == t.Id).OrderBy(x => x.CategoryId).ToListAsync())
                    {
                        var mapped = await Map(org, "Category", part.CategoryId, currency);
                        if (t.Kind == "Income")
                            Pair(bank, mapped, part.Amount);
                        else
                            Pair(mapped, bank, part.Amount);
                    }
                }
                else if (t.Kind == "Income")
                    Pair(bank, await Map(org, t.ReceivableId != null ? "Receivable" : "Category", t.ReceivableId != null ? Guid.Empty : t.CategoryId!.Value, currency), t.Amount);
                else
                    Pair(await Map(org, t.PayableId != null ? "Payable" : "Category", t.PayableId != null ? Guid.Empty : t.CategoryId!.Value, currency), bank, t.Amount);
            }
        }
        else
        {
            var incoming = r.SourceKind == "Receivable";
            TradeDocument d = incoming ? await db.Set<Receivable>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == r.SourceId) ?? throw new AccountingFault(404, "应收单不存在") : await db.Set<Payable>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == r.SourceId) ?? throw new AccountingFault(404, "应付单不存在");
            Need(!d.Cancelled, 409, "已取消单据不可确认");
            date = d.IssueDate;
            currency = d.Currency;
            description = "单据确认 " + d.Number;
            if (incoming)
            {
                ar = d.Id;
                Pair(await Map(org, "Receivable", Guid.Empty, currency), await Map(org, "Category", d.CategoryId, currency), d.Amount);
            }
            else
            {
                ap = d.Id;
                Pair(await Map(org, "Category", d.CategoryId, currency), await Map(org, "Payable", Guid.Empty, currency), d.Amount);
            }
        }
        if (description.Length > 500)
            description = description[..500];
        var entry = await DraftCore(org, new(r.RequestId, date, currency, description, lines.ToArray()), user, cash, ar, ap, reversal);
        await tx.CommitAsync();
        return entry;
    }
    public async Task<JournalEntry> Reverse(Guid org, Guid id, JournalReverseInput r, ClaimsPrincipal user)
    {
        Need(r.RequestId != Guid.Empty && !string.IsNullOrWhiteSpace(r.Reason) && r.Reason.Length <= 400, 400, "冲销请求编号或原因无效");
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, org);
        var original = await db.Set<JournalEntry>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "凭证不存在");
        Need(original.Status == "Posted" && original.ReversalOfId == null, 409, "仅可冲销原始已过账凭证");
        Need(original.SourceTransactionId == null, 409, "资金来源凭证须先撤销资金流水，再导入并过账反向流水，避免资金账与总账脱节");
        if (original.SourceReceivableId != null || original.SourcePayableId != null)
            Need(await new SettlementService(db).Settled(org, original.SourceReceivableId, original.SourcePayableId) == 0, 409, "请先撤销单据全部资金结算再冲销确认凭证");
        Need(r.BusinessDate >= original.BusinessDate, 400, "冲销日期不得早于原凭证");
        var hash = Hash(new
        {
            id,
            input = r
        });
        var old = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.ReversalOfId == id);
        if (old != null)
        {
            Need(old.RequestId == r.RequestId && old.RequestHash == hash, 409, "原凭证已有其他冲销请求");
            return old;
        }
        var lines = await db.Set<JournalLine>().Where(x => x.OrganizationId == org && x.EntryId == id).OrderBy(x => x.Position).ToListAsync();
        var entry = await DraftCore(org, new(r.RequestId, r.BusinessDate, original.Currency, "冲销：" + r.Reason, lines.Select(x => new JournalLineInput(x.LedgerAccountId, Money(x.Credit), Money(x.Debit))).ToArray()), user, reversal: id, requestHash: hash);
        await PostCore(org, entry);
        db.Add(Audit(org, user, "JournalReversed", entry.Id, id + ":" + r.Reason));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entry;
    }
}
