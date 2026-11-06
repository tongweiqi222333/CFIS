# CFIS v1.0 企业财务信息管理系统

可本地运行的企业财务管理初版，包含资金、往来、预算、报表、会计、附件、分类分摊、企业权限和审计。采用Vue 3、TypeScript、ASP.NET Core 10、Identity、EF Core与PostgreSQL的模块化单体架构。

## 快速开始（Windows PowerShell）

安装.NET 10 SDK、Node.js/npm、Git及Docker Desktop，打开Docker Linux引擎，在源代码根目录执行：

```powershell
./scripts/start.ps1
```

访问 http://127.0.0.1:5173 ，注册登录后选择“生成或打开演示账套”。首次运行需要联网恢复依赖和构建镜像。演示数据与已有企业隔离，可重复打开，不重置你的操作。

```powershell
./scripts/status.ps1
./scripts/stop.ps1
```

数据库、身份密钥和配置不会因停止服务删除。Compose固定使用cfis项目名，本机同一时刻使用一套实例。已有PostgreSQL卷不能通过改.env自动换密码；不要删除数据卷更新配置。后端5080、前端5173、数据库5432仅绑定本机。

## 当前文档

- [排版复审与双语验收](docs/i18n-layout-acceptance.md)：控件对齐、语言持久化与中英文验证。
- [前端视觉与交互优化验收](docs/ui-acceptance.md)：页面范围、交互调整及验证记录。

- [中文用户手册](docs/user-guide.md)：从企业创建到结账、附件、报表、备份的完整步骤。
- [功能与验收矩阵](docs/v1-acceptance-matrix.md)：逐项交付范围。
- [v1.0验收记录](docs/v1-acceptance.md)：实际构建、测试、浏览器、恢复、升级及性能结果。
- [项目仓库与源代码下载](https://github.com/tongweiqi222333/CFIS)。
- [发布说明](docs/release-v1.md)：下载文件、运行条件和升级方式。
- [运维与开发说明](docs/operations-phase-6.md)：配置、身份、数据库权限及恢复。
- [更新记录](CHANGELOG.md)。

## 业务规则

金额使用decimal / NUMERIC(19,4)，API使用精确十进制字符串，不允许当前资金余额透支。同币种转账原子完成。普通收支可按多个分类分摊，合计必须等于资金总额；往来结算、期初和转账不分摊。

交易不可改删，更正采用撤销及重新登记。已过账凭证和分录不可改删，只能追加冲销。附件只追加，支持单个5MB以内PDF、PNG、JPEG；凭证附件要求已过账。附件字节存于PostgreSQL，纳入同一份备份和SHA256恢复校验。

企业权限每次请求验证；Owner、Accountant可写财务，Owner、Admin管理成员和审计，Viewer只读。登录凭据仅保存在内存，刷新后重新登录并恢复有权访问的企业地址。开发允许注册；Production默认关闭，使用邀请。

## 测试和维护

```powershell
./scripts/validate.ps1
./scripts/backup.ps1
./scripts/verify-restore.ps1 -Backup backups/实际文件.dump
./scripts/test-upgrade.ps1 -Baseline 20261101040000_AcceptanceGuards -Result docs/upgrade-v1-results.json
./scripts/test-disaster-recovery.ps1
./scripts/test-performance.ps1 -Mixed -Duration 300s -ReportPrefix v1
```

使用独立本地临时库，测试账号需要CREATEDB/CREATEROLE。不要对生产库执行测试。涉及Compose切换、停止或重启的演练必须与数据库测试及压力测试串行。失败初始化残留的无连接随机测试库可使用clean-test-databases.ps1清理，主库不在允许范围内。

备份包含密码、数据库、附件和密钥，必须加密保管，不得上传GitHub。生产模式及SMTP仅提供可选配置，本版交付目标为初版成品，不包含公网运营验收。

## 源代码结构

backend/CFIS.Api：模块化后端及迁移；frontend：Vue界面与浏览器测试；tests：xUnit真实数据库测试；scripts：启动、验证、备份、升级及打包；docs：中文文档。

Phase 1–6文档是历史记录，当前功能以本手册、v1功能矩阵和验收报告为准。完整阶段技术说明仍保留在docs中。

## 首版范围

不包含跨币种汇兑、税务、自动年结、折旧、坏账自动处理、多级审批、预约自动付款或附件删除。Redis、消息队列、微服务和后台调度未引入。
