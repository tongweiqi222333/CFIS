# 前置工具安装记录（2026-10-30）

## 重启后复验

WSL 3.0.3.0 版本检查通过。Docker Engine 29.9.2 正常响应。项目 compose.yaml 已启动 postgres:18，容器健康检查通过。数据库随机密码保存在被 .gitignore 排除的 .env 中，不提交仓库。使用 `docker compose up -d --wait postgres` 启动基础服务。

默认执行沙箱无法访问 Docker 命名管道；沙箱外执行成功，这是工具权限限制，不是 Docker 安装失败。

- .NET SDK：已安装，`C:\Program Files\dotnet\dotnet.exe --list-sdks` 返回 10.0.403。
- Node.js：已安装，`C:\Program Files\nodejs\node.exe --version` 返回 v26.13.0。
- npm：已安装，`C:\Program Files\nodejs\npm.cmd --version` 返回 11.20.0。
- Docker Desktop：已安装。Docker CLI 29.9.2、Compose v5.6.1 版本检查通过；引擎连接失败，尚未可运行容器。
- Git：2.53.0.windows.3 可用。
- EF Core CLI：已安装到项目 .tools，版本 10.0.0。
- WSL：普通安装因要求管理员权限失败；后台提权未出现用户可见弹窗，且未生成管理员日志，需要用户从管理员 PowerShell 执行项目安装脚本。

新安装的软件 PATH 可能需要重新打开终端后生效。尚未验证 Docker Engine、Compose 或 PostgreSQL 容器。

管理员安装脚本：install-prerequisites-admin.ps1。启用 VirtualMachinePlatform，不自动重启，并执行 wsl --install --no-distribution --web-download；日志写入 docs/windows-prerequisites-admin.log。如系统要求重启，应保存工作后手动重启。
