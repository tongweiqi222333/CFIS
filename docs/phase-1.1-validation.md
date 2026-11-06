# Phase 1.1 加固记录

日期：2026-10-30（Asia/Shanghai）。

## 完成内容

- Identity security stamp 在每个已认证请求校验。POST /api/v1/session/logout 撤销当前用户的全部会话，已签发访问令牌立即失效，刷新令牌也受 Identity stamp 校验约束。前端自动刷新过期令牌，刷新失败回到登录；令牌仅存内存。
- 企业成员管理界面、企业切换、当前角色展示，非法角色/邮箱/分页验证。重复成员返回409，数据库唯一约束冲突也映射409，防止并发重复写入。
- ProblemDetails 增加稳定 HTTP 状态错误码 code 与 traceId，前端显示错误标题与追踪号。
- Docker 密钥卷持久化，目录权限700、应用非root用户拥有。生产要求配置持久化路径与PFX证书，以证书加密密钥；未配置时拒绝启动。开发容器磁盘密钥未加密，不应当直接用于生产。
- PostgreSQL集成测试自动创建随机临时数据库，迁移后执行、结束删除；CFIS_TEST_ADMIN 必须为本地测试服务器的 CREATEDB 账号。
- Playwright真实Chromium测试：注册、登录、创建两个企业、切换企业、添加成员、错误追踪提示、退出。scripts/test-browser.ps1 使用独立临时数据库及5081 API，结束清理。
- GitHub Actions 已配置构建、PostgreSQL集成测试、前端构建、Chromium测试；尚未推送或远程运行。
- 按需引入UI组件，JS从约1092KB降到457.81KB（gzip163.48KB），无500KB告警。
- 数据库和认证密钥备份脚本，独立数据库恢复演练脚本；备份目录已排除Git。

## 实际验证

- 后端5项测试通过：角色矩阵、隔离、非法角色和分页、重复及并发添加成员、退出后旧令牌拒绝；过期401、刷新200、失败锁定、限流429。
- 独立空数据库迁移、真实浏览器流程通过：1 passed。结束后临时数据库删除。
- 前端类型检查和生产构建通过。
- Docker镜像构建通过；容器重建后旧令牌访问 /me 返回200，证明密钥持久化生效。
- pg_dump备份、pg_restore恢复到随机独立库、查询迁移历史成功，恢复演练库已清理。数据库备份与密钥归档已导出至被忽略的backups目录。
- 已修复浏览器测试发现的密钥卷写权限问题。

## 复现命令（项目根目录PowerShell）

    docker compose up -d --wait postgres
    $env:CFIS_TEST_ADMIN='Host=localhost;Database=postgres;Username=cfis;Password='+((Get-Content .env | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' }).Substring(18))
    dotnet test
    npm ci --prefix frontend
    cd frontend
    npx playwright install chromium
    cd ..
    ./scripts/test-browser.ps1
    ./scripts/backup.ps1
    ./scripts/verify-restore.ps1 -Backup ./backups/实际文件名.dump

浏览器测试会临时使用5081和5174，需保证端口可用。脚本仅启动后台进程，不弹出交互窗口。独立测试失败也会尝试清理数据库。

## 生产配置与恢复

开发使用compose的Development设置。生产需设置 ASPNETCORE_ENVIRONMENT=Production、DataProtection__Path、DataProtection__CertificatePath、DataProtection__CertificatePassword，证书及密码通过外部秘密配置挂载；不要提交证书、密码、密钥或备份。生产证书供应与部署不属于本地验收，尚未实际部署验证。

密钥卷的访问仅限应用用户与主机管理员。备份含敏感认证数据，需另行加密离线保管，并与数据库备份一起保存PFX和恢复说明。恢复时先停止API，恢复数据库，再把匹配的密钥归档还原到密钥目录，恢复应用用户所有权和700权限，重新启动后验证登录及会话。不应在生产主库直接演练，使用verify-restore脚本的随机数据库。

## 范围与限制

退出会撤销该用户全部会话，并非仅单设备。每个已认证请求查询用户stamp，当前规模可接受，后续再根据测量优化。分布式限流和反向代理来源地址配置未引入。登录、创建企业和成员添加已提供加载状态，注册操作在处理中禁用。恢复验证目前包含schema/迁移历史，尚无财务数据业务对账。

未进入Phase 2。


最终复验：5项后端测试通过；扩展浏览器测试1项通过；前后端构建通过；API镜像更新成功；/health/ready返回200；临时测试数据库清理后查询0条。
