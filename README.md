# MYKEY

Windows 本地 API 密钥与账号管理工具，使用 WPF 原生界面。

## 安装与更新

从 [GitHub Releases](https://github.com/Mulic-ca/mykey-desktop/releases/latest) 下载 `MYKEY-Setup-1.1.0-x64.exe`。

已安装 1.0.0 的用户，直接运行新版安装包覆盖安装，无需卸载。默认安装到当前用户的 `%LOCALAPPDATA%\Programs\MYKEY`。数据库和设置位于安装目录的 `data` 文件夹，安装包不包含个人数据，卸载保留此目录。

从 1.1.0 起，在「设置 → 常规 → 检查更新」查看正式发布版本和更新说明，确认后下载、校验、备份数据库并重启安装。可关闭启动时检查更新。更新通过 GitHub Releases 获取，不需要登录 GitHub。网络错误、下载中断或校验失败均不会替换当前程序；安装失败时更新辅助程序尝试恢复之前的可执行文件。

## 功能

- API 卡片：多个 Base URL、兼容类型、自动检测与手动模型、默认模型、复制和官网跳转。
- 账号卡片：同站点多个账号、邮箱与手机列表、可选择复制的特殊备注。
- 中文、拼音和首字母搜索，账号搜索只保留匹配的账号。
- 两个板块均支持置顶、多标签、标签筛选及拖动排序。拖动卡片右上角的六点手柄调整顺序；置顶区与普通区分别排序。拖动前清除搜索及标签筛选，选择「自定义排序」。也可选择最新添加或名称排序。
- 回收站支持整卡恢复；编辑账号组时移除的单个账号同样可以恢复。每次删除保留 30 天，到期在软件运行时自动清理，退出期间不会有后台清理任务。
- 经典、深色、青竹、晴蓝、玫瑰五套配色，保留相同布局和控件风格。
- 单实例、启动到托盘、关闭到托盘、数据库导入导出。

## 本地开发

需要 Windows x64 和 .NET 8 SDK。发布脚本优先使用 `.dotnet\dotnet.exe`；该 SDK 目录不上传到仓库。

```powershell
dotnet build MyKey.Desktop.csproj -c Release
dotnet run --project tests/MyKey.Tests.csproj -c Release -- --test-mode --data-dir C:\tmp\mykey-qa-new
```

测试目录应为新的空目录。测试使用合成数据，包含数据库迁移、回收站边界、标签、排序、更新校验和真实 WPF 窗口检查；截图留在测试目录。`--test-mode --data-dir` 仅用于隔离测试，普通启动仍使用安装目录的数据库。

`publish-desktop.bat` 生成独立运行的 x64 程序。安装 Inno Setup 6 后，运行 `build-installer.bat` 生成安装包。`tools/smoke-release.ps1 -FixtureDirectory <上述测试目录>` 执行隔离安装、单实例、托盘及升级测试。

## 发布约定

修改和本地构建不会自动上传。每次发布前完成测试和人工界面检查，再提交源码并创建正式 Release。发布版本号同步修改项目文件、安装脚本和更新说明；Release 标签使用 `v主版本.次版本.修订版本`，安装包使用 `MYKEY-Setup-版本号-x64.exe`，必须具有 GitHub 提供的 SHA-256 digest。

不上传 `.dotnet`、`bin`、`obj`、测试截图、数据库、备份、个人设置或访问令牌。MiSans 字体及其授权文件随程序保留。
