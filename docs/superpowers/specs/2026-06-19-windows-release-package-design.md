# WowVmMonitor Windows 发布包设计

## 目标

为 WowVmMonitor 生成仅面向本机 Windows 10/11 x64 的正式发布产物：

- 一个自包含的应用单文件 `WowVmMonitor.exe`。
- 一个安装程序 `WowVmMonitor-Setup-1.0.0.exe`。
- 控制面板和开始菜单可用的卸载程序。
- 使用新版安装程序进行原位覆盖升级。

## 发布架构

桌面项目以 `win-x64`、self-contained、single-file 模式发布。目标电脑无需另行安装 .NET 8。WPF 所需原生库允许在运行时自解压，发布目录最终只保留应用 EXE 及必要的发布说明，不向用户暴露一组松散 DLL。

发布版本从 `1.0.0` 开始，并统一设置 `Version`、`AssemblyVersion`、`FileVersion` 和安装程序版本。发布脚本接收版本参数，拒绝不符合三段式语义版本的值。

## 签名

本发布包仅在当前电脑使用，复用当前用户证书存储中的 `CN=WowVmMonitor Local Development` 代码签名证书。

发布顺序为：

1. 编译单文件应用。
2. 对应用 EXE 执行 SHA-256 Authenticode 签名并验证状态为 `Valid`。
3. 使用 Inno Setup 编译安装程序。
4. 对安装程序 EXE 签名并验证状态为 `Valid`。

不修改或关闭 Windows Code Integrity、SmartScreen 或其他安全策略。该本地证书不作为面向其他电脑分发的可信签名方案。

## 安装程序

采用 Inno Setup，固定 AppId，使用当前用户安装模式：

- 安装目录：`%LocalAppData%\Programs\WowVmMonitor`。
- 不申请管理员权限。
- 创建开始菜单快捷方式。
- 默认创建桌面快捷方式，用户可在安装时取消。
- 安装结束可选择立即启动程序。
- 安装新版本时检测并关闭正在运行的 WowVmMonitor，再替换应用文件。

安装包内只包含已签名的单文件应用及必要安装元数据。

## 卸载

Inno Setup 注册标准卸载项，并生成卸载程序。卸载执行以下行为：

- 关闭正在运行的 WowVmMonitor。
- 删除安装目录、快捷方式和卸载注册信息。
- 默认保留 `%LocalAppData%\WowVmMonitor\config.json`、备份、损坏配置样本和 Windows Credential Manager 凭据，避免误删监控配置。
- 提供明确的发布说明，说明彻底清理数据需由用户单独执行。

## 版本升级

- 所有安装包使用同一个固定 AppId。
- 新版版本号必须高于已安装版本。
- 新版安装程序在同一目录覆盖旧应用并更新卸载信息。
- 配置目录和 Credential Manager 目标不变，因此升级后继续使用原机器列表和共享凭据。
- 配置架构升级继续由现有 `ConfigurationMigrator` 负责，安装程序不解析 JSON。
- 不提供程序内联网检查更新，用户手动运行新版安装程序升级。

## 自动化脚本

新增一个 PowerShell 发布入口，负责：

- 检查 .NET SDK、Inno Setup 编译器和本地签名证书。
- 清理并创建独立的 `artifacts/release/<version>` 输出目录。
- 执行 Release 测试。
- 发布、签名和验证单文件应用。
- 调用 Inno Setup 生成、签名并验证安装程序。
- 输出最终文件路径、版本、SHA-256 和签名状态。

脚本遇到测试失败、缺少证书、签名无效或安装器编译失败时立即停止，不留下被误认为成功的发布包。

## 验证

- Release 构建 0 警告、0 错误。
- 自动化测试全部通过。
- 应用发布目录只包含单个可执行应用文件。
- 应用 EXE 和安装程序签名状态均为 `Valid`。
- 单文件应用可在当前 Windows Code Integrity 策略下启动。
- 安装程序可完成当前用户安装并启动应用。
- 控制面板卸载项存在，卸载后程序文件和快捷方式消失。
- 从 `1.0.0` 测试包升级到更高测试版本时配置和凭据保持可用。

## 非目标

- 不支持 x86、ARM64 或 Windows 7。
- 不提供在线自动更新服务。
- 不面向其他电脑提供公开可信签名。
- 不在卸载时自动删除用户配置和凭据。
