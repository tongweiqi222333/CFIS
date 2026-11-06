# Phase 2 核心财务设计与使用

## 范围

账户创建与查询（银行/现金）、收入支出分类、收支登记、同币种账户转账、账户余额、交易流水分页筛选排序及财务操作审计。财务流水不提供直接修改和删除接口。当前没有期初余额字段，账户余额从零开始，通过收入流水形成资金。

所有金额HTTP输入输出均为十进制字符串；后端decimal、数据库NUMERIC(19,4)。超过4位小数直接拒绝，不隐式四舍五入。金额必须为正，上限999999999999999.9999；任何操作不得令余额负数或超上限。币种接受三位大写代码，预留多币种，但不提供汇率计算或跨币种转账。

## API

前缀 /api/v1/organizations/{organizationId}，使用登录Bearer令牌。

|资源|方法|内容|
|---|---|---|
|/accounts|GET、POST|列出账户或创建账户|
|/categories|GET、POST|列出分类或创建分类|
|/transactions|GET、POST|查询流水或登记收入支出|
|/transfers|POST|转账|
|/audit-logs|GET|查询财务审计|

账户POST：{"name":"基本户","currency":"CNY","kind":"Bank"}，kind可为Bank/Cash。创建返回余额字符串0.0000。
分类POST：{"name":"销售收入","kind":"Income"}，kind为Income/Expense，同企业同类型名称唯一。

交易POST示例：

    {
      "requestId": "调用方生成的非空UUID",
      "accountId": "本企业账户UUID",
      "destinationAccountId": null,
      "categoryId": "本企业且类型匹配的分类UUID",
      "kind": "Income",
      "amount": "100.0001",
      "description": "销售收款"
    }

转账使用同一结构，但kind=Transfer，destinationAccountId为不同的同币种账户，categoryId=null。requestId在企业内唯一，相同数据重复提交返回原交易；相同requestId但数据不同返回409。并发且锁定不同账户的冲突请求可能返回409，调用方应查询确认。前端失败重试保留相同请求ID和数据。

流水GET支持page、pageSize（1–100）、kind、accountId（转出或转入均匹配）、from、to（ISO时间）及sort=asc/desc（按创建时间和ID稳定排序）。返回total和items。审计支持分页，仅Owner/Admin可见；Viewer可读账户/分类/流水但不能写入。

## 一致性和隔离

- 企业权限统一由Finance.Access检查成员关系，非成员404、Viewer写入403。
- 账户、分类、流水、审计均有organization_id。流水使用企业ID与资源ID的复合外键，数据库拒绝跨企业引用。
- 资金操作开启数据库事务，按稳定UUID顺序逐个SELECT FOR UPDATE锁账户，串行化同账户余额变更并避免反向转账死锁。
- 余额更新、不可直接修改的交易流水、审计事件同事务提交。提交前任何验证失败或数据库写入异常均回滚。
- 同一请求ID有企业范围唯一索引，防止重复入账。分类唯一索引防止并发重名。
- 余额可以由收入减支出、转出加转入的流水重新核对；测试已逐账户核对。
- 账户创建、分类创建与审计同一SaveChanges事务提交。

当前审计为应用级追加写入；数据库管理员仍可修改数据，未引入防篡改外部存储。历史更正/冲销在后续会计阶段设计，当前不提供修改余额接口。

## 数据模型

新增financial_accounts、categories、transactions、audit_logs。Migration为CoreFinance，包含NUMERIC精度、余额非负/交易金额为正约束、企业复合外键、幂等和查询索引。

transaction_allocations暂未引入：Phase 2每笔收支只有一个分类，拆分分摊在需要时单独设计，避免存在无实际使用的表。

## 启动与测试

沿用README环境配置；升级数据库执行：

    dotnet ef database update --project backend/CFIS.Api
    docker compose up -d --build api

前端在企业列表选择企业后展示核心财务。先创建账户和收入/支出分类，再登记收入，之后可登记支出和转账。资金账户和转入账户从本企业列表选取。

    $env:CFIS_TEST_ADMIN='Host=localhost;Database=postgres;Username=cfis;Password='+((Get-Content .env -Raw).Trim().Split('=',2)[1])
    dotnet test
    npm run build --prefix frontend
    ./scripts/test-browser.ps1

测试自动创建迁移独立数据库并清理，不使用模拟接口。财务测试包含金额精度/非法输入、同币种转账、幂等重试、并发防透支、跨企业账户拒绝、Viewer权限、余额流水核对、审计数量，以及模拟审计插入失败时双边资金与流水完整回滚。

## 当前限制

账户与分类当前提供创建/查询，不提供删除、归档、改名；金额修正和交易撤销待后续明确冲销规则。无期初余额导入、手续费、汇兑、附件或分摊。账户/分类列表尚未分页，流水及审计已分页。审计界面展示最近20条，API可翻页。该阶段不含应收应付、预算、财务报表或复式会计引擎。

## Phase 2 实际验证（2026-10-30）

- dotnet build：0警告、0错误。
- dotnet test：6通过、0失败，包含Phase 1.1回归及真实PostgreSQL财务集成测试。
- 新增CoreFinance迁移已应用；has-pending-model-changes无变化。
- 财务故障注入：审计插入抛错时接口500，转出/转入余额与流水数量保持不变。
- 并发幂等转账：一条201、一条200，只增加一条流水与一条审计。
- 并发支出：一条201、一条409，余额不透支。
- 四位小数、超精度及负金额、分类类型、跨企业账户、跨币种、自转账、Viewer写入和审计权限测试均通过。
- Playwright真实Chromium：1项完整流程通过，包含注册、登录、企业切换、成员添加、账户分类创建、收入及转账后双账户余额验证、错误提示、退出。
- 浏览器选择控件等待问题已修复，财务表单使用原生select，支持键盘与标准可访问角色。
- 前端生产构建通过，JS约465.68KB，无500KB告警。
- API Docker镜像构建成功。
- 所有随机测试数据库已清理（查询0条）。

下一阶段计划：在确认后进入Phase 3，扩展往来方、应收应付、预算与Excel导出。当前不自动进入Phase 3。

容器发布复验：迁移重复执行成功且无变更，/health/ready返回200；OpenAPI包含accounts、categories、transactions、transfers、audit-logs；API运行，PostgreSQL健康。
