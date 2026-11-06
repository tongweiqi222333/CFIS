# Phase 6 测试、恢复与部署验收

日期：2026-11-01，Asia/Shanghai。Phase 6 已全部完成并实际验收。完整压力脚本成功退出，最终代码远程CI通过。

## Phase 5 前置复核

进入本阶段前实际重做后端全量43项测试（原42项加状态参数回归）、前后端构建、3条浏览器流程、10条迁移备份恢复、恢复库登录及跨企业隔离，均通过。核查已提交的10万交易读取压测和EXPLAIN证据；本阶段不把此前读取结果当作写入测试。

## 完成内容与验证

|范围|完成内容|验证依据|
|---|---|---|
|财务数据库约束|新增来源金额、币种、日期保护；结账检查历史未过账来源、已冲销确认的有效单据、未映射账户及资金总账差异|直接SQL反向测试、原有不可变及借贷测试|
|历史接入|完整历史接入策略；最早业务日、期间未覆盖流水、映射及待确认检查；来源唯一与幂等防止重复期初|setup-check、历史来源与批量重试测试|
|业务验收|应收和应付分次结算、撤回、再次结算、超额拒绝、全部撤销、确认冲销、取消、对账和结账|真实PostgreSQL贯穿流程测试|
|财务操作|结账预检查共用规则、分页阻塞明细、逐项批量结果、安全重试、来源到凭证和科目双向追溯|会计集成测试及浏览器流程|
|身份安全|48小时单次邀请、新用户设置密码、已有账户绑定校验、角色调整、成员移除、注册策略、SMTP找回、会话失效和响应头|安全生命周期、真实本地SMTP协议、登录限流及下载跨企业测试|
|恢复|数据库、保护密钥、配置和证书备份；新容器/新库/新卷恢复原用户及访问/刷新凭据，验证恢复后收入|recovery-phase-6-results.json，实际恢复6.09秒|
|性能|10万目标和10万背景交易；同账户写入/重试、结算/重试、过账/重试、报表/Excel混合负载；资源持续采样|performance-phase-6.md、脱敏JSON|
|部署升级|容器前端、HTTPS代理、Production注册限制、PFX加密保护密钥；Phase5到Phase6升级、数据摘要不变、回退说明|release-phase-6-results.json、upgrade-phase-6-results.json|

本阶段新增 MemberInvitations 和 AcceptanceGuards，主库共12条迁移。核心财务逻辑自主实现；未增加微服务、消息队列或Redis。

## 实际测试结果

- 解决方案构建成功，0警告、0错误。
- 全量后端50项通过；追加来源日期反向断言后定向测试1项通过。
- 真实浏览器4条流程：工作空间、业务报表、会计、邀请。最终本地4条全部通过（21.6秒）。
- NuGet后端及传递依赖扫描、npm audit均未报告已知漏洞。
- Phase5第10条迁移升级至当前第12条通过；原有账户数据摘要不变，无待迁移模型变更。
- 实际Production HTTPS前端、生产API、已有用户登录、自主注册403、保护密钥加密检查通过，随后恢复开发服务；耗时29.34秒，含本地容器准备，不代表生产SLA。
- 原有用户、访问令牌、刷新令牌恢复及业务续办通过。完整快照恢复不包括快照后写入，没有配置PITR。

- 最终完整300秒混合负载：8VU、56,126次HTTP请求、26,157次迭代，HTTP错误0，检查全部通过；两项测试后SQL一致性校验通过。见 performance-phase-6.md。
- 最终备份 cfis-20261101-040304.dump：12条迁移、v5身份与邀请摘要、财务一致性及恢复登录隔离通过。

## 新API

会计前缀 `/api/v1/organizations/{org}/accounting`：

- GET `/setup-check`：完整历史接入与待办检查。
- GET `/periods/{id}/precheck`：当前是否可结账及各阻塞数量。
- GET `/periods/{id}/blockers?kind=Transaction|Receivable|Payable|Draft|Unmapped&page=1&pageSize=20`：具体阻塞编号，最大100项。
- POST `/batch-import`：数组，每项 requestId、sourceKind、sourceId，最多50项。
- POST `/batch-post`：数组，每项 id、version，最多50项。
- GET `/source-journal?kind=Transaction|Receivable|Payable&sourceId=UUID`：业务到凭证追溯。

身份与成员：

- GET `/api/v1/auth/options`：注册与邮件找回能力。
- POST `/api/v1/organizations/{org}/invitations`：email、role，返回一次展示的邀请代码，数据库仅保存摘要。
- POST `/api/v1/invitations/accept`：token、可选password；已有账户必须登录本人，新账户以邀请邮箱创建。
- PATCH `/api/v1/organizations/{org}/members/{userId}/role`：role。
- DELETE `/api/v1/organizations/{org}/members/{userId}`：移除本企业访问，保留审计。
- Identity标准 `/auth/forgotPassword`、`/auth/resetPassword`、`/auth/manage/info`：邮件重置和本人修改密码。

所有新增业务读写保持企业隔离和角色权限；Owner不可通过成员调整接口移除或降权。批量各项独立提交，结果明确区分成功失败；失败重试不重复入账。

## 复现

```powershell
./scripts/validate.ps1
./scripts/test-disaster-recovery.ps1
./scripts/test-release.ps1
./scripts/test-performance.ps1 -Mixed -Duration 300s
./scripts/backup.ps1
```

涉及容器切换的发布演练与数据库测试、压测必须串行执行，避免主动重启数据库干扰测试。常规检查不替代发布、恢复和压力演练。完整操作说明见 operations-phase-6.md。

## 已发现并处理的问题

- 接入真实邮件发送器后，未配置SMTP使可选注册确认失败：允许未配置邮件的本地注册；密码找回统一503并在界面标记不可用。
- 密码重置后的旧Bearer阻止重新登录：公共凭据入口跳过旧会话检查，保护接口仍检查安全戳，刷新由Identity验证。
- 阻塞查询的构造记录排序无法翻译为SQL：在数据库实体投影前排序，定向查询验证通过。
- HTTPS健康代理默认Host不在生产白名单：转发原Host并复测通过。
- 一次5分钟压测与部署切换重叠，因数据库重启出现错误：记录失败，不降低阈值；最终串行独立重跑。
- 大数据来源恢复校验的UNION+CASE连接执行过慢：改为三种来源分别按企业及来源编号连接，实测同一压测数据集校验通过，并完整复跑工具。
- 浏览器和恢复API占用编译目录：运行前复制独立目录，避免构建与测试互相锁文件；邀请测试等待退出完成后再操作。

## 交付边界

公网域名、可信证书和真实SMTP账户由部署环境提供；本阶段验证本地Production及隔离SMTP，不冒称公网发布或外部邮件投递。附件上传未在本版本实现，因此恢复中没有附件文件；相关说明保留到运维手册。会计完整历史接入不会自动产生业务期初或自动结转。五分钟本地负载不能证明数日无泄漏或构成容量SLA。

该阶段最终远程验证中，构建、50项后端测试、迁移、前端构建、依赖审计和4条浏览器全部通过。此项为阶段历史验收记录。

最终收尾：备份v5摘要再次恢复比对通过；清理38个失败初始化遗留的无活动连接随机测试库，复核测试/恢复/压力临时库为0，无临时压力或恢复容器；主库健康ready，前端5173返回200。本地清理工具实际执行通过，不强制断开活动连接。最终归档包含该本地工具和脱敏报告，业务代码以4638fe2的远程CI结果为准。
