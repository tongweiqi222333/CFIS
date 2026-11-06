# Phase 3 业务扩展

## 已实现

客户/供应商/Both往来方创建、搜索分页、改名、联系方式编辑及停用归档；应收应付创建、部分与全额结算、状态/到期展示、结算历史及未结单据取消；费用预算多分类制定、执行分析与超支预警；Excel交易导出。

新增counterparties、receivables、payables、budgets、budget_items；transactions新增ReceivableId/PayableId。应收应付共用业务逻辑但使用TPC映射到独立数据表。金额均NUMERIC(19,4)，API金额为字符串，企业关联均使用复合外键。迁移BusinessExpansion、BusinessConstraints增加正金额、日期和单据互斥约束。

## 一致性

结算先锁单据，再锁账户及分类，校验剩余应收应付、币种、日期和分类，直接复用财务入账事务。重复请求不会重复付款。余额、结算流水和审计原子提交；撤销结算保留单据关联，通过反向流水恢复未结金额。单据只能在净已结金额为零时取消；取消后禁止新结算。

已结金额从不可改流水计算，不单独维护易失配的计数。预算只统计费用分类、指定币种及业务日期区间内的净支出；反向流水抵减费用。超支为预警，不阻止付款。

运行角色不能删除新业务表；应收应付仅允许更新Cancelled列，账户币种及分类类型也禁止数据库运行账号修改。迁移后必须重新执行权限配置，scripts/migrate.ps1已包含。

## API

统一前缀 /api/v1/organizations/{org}：

|路径|方法|
|---|---|
|/counterparties|GET、POST|
|/counterparties/{id}|PATCH|
|/receivables、/payables|GET、POST|
|/receivables/{id}、/payables/{id}|GET|
|/receivables/{id}/settlements、/payables/{id}/settlements|POST|
|/receivables/{id}/cancel、/payables/{id}/cancel|POST|
|/budgets|GET、POST|
|/budgets/{id}、/budgets/{id}/execution|GET|
|/exports/transactions.xlsx|GET|

列表支持page/pageSize（最大100）；往来方支持search；单据支持counterpartyId；详情结算历史分页。

往来方：name、kind（Customer/Supplier/Both）、contact。修改使用name/status/contact。
单据：number、counterpartyId、categoryId、currency、amount字符串、issueDate、dueDate、description。单号在企业内按应收/应付分别唯一，类型须与往来方/分类匹配。
结算：requestId UUID、accountId、amount字符串、businessDate、description。必须同币种，日期不得早于单据。
预算：name、currency、startDate、endDate、items数组（categoryId、amount字符串），1–100项且分类不重复。

单据状态：Cancelled、Paid、Overdue、PartiallyPaid、Open；逾期优先于部分结清展示，同时保留已结与未结金额。

Owner/Accountant可写，所有合法企业成员可读和导出；跨企业404，越权403。

## Excel与界面

选择企业后点击“打开业务扩展”。按需加载业务面板；结算后财务余额面板刷新。预算界面显示分类金额和超支预警。

导出支持dateFrom/dateTo/accountId，最多10000条，超限要求缩小范围。金额保存为精确文本，避免Excel数字精度损失；用户字符串不作为公式执行。导出写入审计。组件ClosedXML 0.105.1，MIT许可证，来源：https://www.nuget.org/packages/ClosedXML/0.105.1 。

## 验证与复现

    ./scripts/init-env.ps1
    docker compose up -d --wait postgres
    ./scripts/migrate.ps1
    docker compose up -d --build api
    npm ci --prefix frontend
    npm run dev --prefix frontend

测试沿用README的CFIS_TEST_ADMIN连接，再运行dotnet test和scripts/test-browser.ps1。scripts/smoke-business.ps1使用实际Docker API和受限账号完成创建、收付款、预算、Excel与撤销核对；会保留明确标识的部署验收数据，结束时冲回全部资金。

## 限制

当前预算为费用预算，无预算审批、调整版本或硬性拦截；应收应付金额及单据要素创建后固定，错误单据可在结算净额归零后取消重建；无税额、发票、折扣、坏账、跨币种结算或自动任务提醒。逾期实时计算，不发通知。Excel仅交易流水，尚未导出预算或单据。选取往来方使用当前分页，账户/分类选项通过分页加载；大规模远程搜索留待性能阶段。未进入Phase 4。

## 最终验收（2026-10-31，Asia/Shanghai）

- 中断恢复后全量后端24项通过，0失败。
- Chromium两条完整流程再次通过：核心财务回归、Phase 3业务流程。
- 前后端构建成功，后端0警告0错误；主JS478.36KB，业务面板按需加载14.94KB。
- 数据库迁移执行成功，重复执行无变更；模型与迁移一致。
- 实际Docker Linux及cfis_app受限账号冒烟通过：创建往来方/单据、收付款、预算超支预警、结算撤销和取消、核对最终余额0.0000。
- Linux Excel导出HTTP200，7050字节；精确文本金额及公式安全已由集成测试重新读取工作簿验证。
- 数据库与密钥备份导出成功，独立恢复成功，迁移历史5条。
- Docker镜像使用锁文件还原；此前依赖漏洞扫描未报告已知漏洞。
- 新增详情及受限列权限后，针对BusinessTests和RuntimePermissionsTests的5项回归已通过；恢复后全量再次通过。
- 测试临时数据库清理完成；部署冒烟记录明确标识为非业务数据并已冲回资金。
- 远程GitHub CI和生产证书部署尚未实测。

已完成Phase 3，等待确认再进入Phase 4（Dashboard、趋势、现金流、预算可视化与PDF）。
