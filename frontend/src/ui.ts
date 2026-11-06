import { t } from "./i18n";
export const labels: Record<string, string> = {
  Owner: "企业所有者",
  Admin: "管理员",
  Accountant: "会计",
  Viewer: "只读成员",
  Active: "已启用",
  Inactive: "已停用",
  Archived: "已归档",
  Draft: "草稿",
  Posted: "已过账",
  Open: "进行中",
  Closed: "已关闭",
  Cancelled: "已取消",
  Paid: "已结清",
  PartiallyPaid: "部分结算",
  Overdue: "已逾期",
  Income: "收入",
  Expense: "支出",
  Transfer: "转账",
  Opening: "期初",
  Bank: "银行",
  Cash: "现金",
  Customer: "客户",
  Supplier: "供应商",
  Both: "客户及供应商",
  Asset: "资产",
  Liability: "负债",
  Equity: "权益",
  Revenue: "收入",
  Receivable: "应收款",
  Payable: "应付款",
  Transaction: "资金流水",
  Journal: "会计凭证",
  NotDue: "未到期",
};
export const labelOf = (value: string | undefined | null) =>
  value ? t(labels[value] ?? value) : "—";
export function errorText(error: any): string {
  const body = error?.response?.data;
  const validation = body?.errors
    ? Object.values(body.errors).flat().join("；")
    : "";
  const message =
    validation || body?.title || error?.message || "操作未完成，请稍后重试";
  return body?.traceId ? `${message}（追踪号：${body.traceId}）` : message;
}
export function formatAmount(value: string | undefined | null): string {
  if (value == null) return "—";
  const [integer, fraction] = String(value).split(".");
  return (
    integer!.replace(/\B(?=(\d{3})+(?!\d))/g, ",") +
    (fraction == null ? "" : "." + fraction)
  );
}
export function units(value: string): bigint | null {
  if (!/^\d{1,15}(\.\d{1,4})?$/.test(value)) return null;
  const [integer, fraction = ""] = value.split(".");
  return BigInt(integer!) * 10000n + BigInt(fraction.padEnd(4, "0"));
}
export function unitText(value: bigint): string {
  return `${value / 10000n}.${(value % 10000n).toString().padStart(4, "0")}`;
}

export const auditActions: Record<string, string> = {
  AccountCreated: "创建资金账户",
  AccountChanged: "调整资金账户",
  CategoryCreated: "创建收支分类",
  CategoryChanged: "调整收支分类",
  TransactionPosted: "登记资金流水",
  TransactionReversed: "撤销资金流水",
  CounterpartyCreated: "创建往来方",
  CounterpartyChanged: "调整往来方",
  ReceivableCreated: "创建应收单",
  PayableCreated: "创建应付单",
  DocumentCancelled: "取消单据",
  BudgetCreated: "创建预算",
  BudgetSuperseded: "替换预算版本",
  BudgetStatusChanged: "调整预算状态",
  TransactionsExported: "导出交易流水",
  FinancialReportExported: "导出财务报表",
  LedgerAccountCreated: "创建会计科目",
  LedgerAccountStatus: "调整科目状态",
  FiscalPeriodCreated: "建立会计期间",
  FiscalPeriodClosed: "关闭会计期间",
  AccountingMappingChanged: "调整科目映射",
  JournalDraftCreated: "创建凭证草稿",
  JournalDraftEdited: "编辑凭证草稿",
  JournalDraftDeleted: "删除凭证草稿",
  JournalPosted: "凭证过账",
  JournalReversed: "凭证冲销",
  AttachmentAdded: "上传业务附件",
  MemberAdded: "添加企业成员",
  MemberInvited: "邀请企业成员",
  MemberRoleChanged: "调整成员角色",
  MemberRemoved: "移除企业成员",
  InvitationAccepted: "接受企业邀请",
  DemoCreated: "创建演示账套",
  DemoCompleted: "初始化演示账套",
};
