<div align="center">
  <img src="Assets/startIcon.png" width="112" height="112" alt="Centre 图标">

  # Centre

  一个简洁、可自定义的 Windows 全屏应用启动器。
</div>

Centre 使用 WPF 和 .NET 10 构建，提供类似 Launchpad 的应用启动体验。应用列表完全由用户维护，不会自动扫描或导入开始菜单中的软件。

## 功能特性

- 默认使用空白应用列表，仅支持手动添加。
- 支持添加 `.exe`、`.lnk`、`.appref-ms` 和 Microsoft Store/UWP 应用。
- 支持通过文件选择器批量添加，也可以直接拖放文件。
- 解析快捷方式目标和指定图标，并将图标持久化缓存到本地。
- 支持名称、拼音、首字母、路径搜索，以及最近使用排序。
- 支持分页、鼠标滚轮、方向键和带插入提示的拖拽排序。
- 右键菜单支持重命名、更换图标、恢复默认图标和删除。
- 支持全屏和固定尺寸窗口两种显示模式。
- 提供可选的中央悬浮搜索模式，采用超大圆角和一体化结果面板。
- 悬浮搜索模式自动跟随 Windows 明暗主题，并支持键盘选择与启动。
- 可调整窗口宽高、网格行列数和应用图标大小。
- 背景模糊强度可在 `0–40px` 间实时调节，设为 `0` 可关闭额外模糊。
- 全局唤出快捷键可自定义，默认使用 `Shift + Tab`；`Esc` 返回桌面。
- 支持多显示器和 Per-Monitor V2 DPI，每次在鼠标所在屏幕显示。
- 采用单实例运行；再次启动 Centre 会唤醒已有窗口。
- 每天最多检查一次 GitHub Releases，新版本只提示、不自动下载或执行。
- 应用列表与设置采用原子写入和备份恢复。

## 界面说明

| 控件或操作 | 功能 |
| --- | --- |
| 右上角 `＋` | 选择并添加一个或多个应用 |
| 右上角设置按钮 | 打开外观与窗口设置 |
| 搜索框 | 按名称、拼音、首字母和路径实时筛选 |
| 拖放文件 | 添加受支持的应用或快捷方式 |
| 拖动应用图标 | 调整应用顺序 |
| 右键应用图标 | 重命名、更换图标、恢复图标或删除 |
| 鼠标滚轮 / `←` `→` | 切换页面 |
| 点击图标之外的空白区域 | 隐藏 Centre 并返回桌面 |
| `Shift + Tab` | 默认全局显示或隐藏快捷键，可在设置中修改 |
| `Esc` | 隐藏 Centre；设置打开时先关闭设置 |

在“中央悬浮搜索”模式中，输入内容后面板会纵向展开搜索结果：

- `↑` / `↓`：切换选中的搜索结果。
- `Enter`：启动当前结果。
- `Esc`：先清空搜索内容，再次按下时隐藏 Centre。

> 删除操作只会移除 Centre 中的启动条目，不会删除原始程序或快捷方式。

## 运行环境

- Windows 10 2004（19041）或更高版本
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

如需从源码构建，还需要安装 .NET 10 SDK。

## 快速开始

### 直接运行

构建完成后运行：

```text
bin/Release/net10.0-windows10.0.19041.0/Centre.exe
```

首次启动时应用列表为空。点击右上角 `＋`，或将受支持的应用文件拖入窗口即可开始使用。

### 从源码构建

在项目目录打开 PowerShell：

```powershell
dotnet restore
dotnet build centre_app.csproj -c Release
```

运行 Release 版本：

```powershell
dotnet run --project centre_app.csproj -c Release
```

## 设置范围

Centre 当前支持以下外观设置：

- 中央悬浮搜索模式开关。
- 全屏显示开关。
- 非全屏窗口宽度：最小 800 像素，最大为当前工作区宽度。
- 非全屏窗口高度：最小 600 像素，最大为当前工作区高度。
- 每页列数：4–12 列。
- 每页行数：3–8 行。
- 图标大小：48–128 像素。
- 背景模糊强度：0–40 像素，默认 18 像素。
- 全局唤出快捷键、预设组合及恢复默认。
- 自动检查更新开关和手动检查按钮。

默认配置为全屏显示、`1200 × 760` 窗口尺寸、`8 × 5` 网格和 76 像素图标。

中央悬浮搜索模式开启后，Centre 会使用全屏背景承载居中的浮动搜索面板；关闭后恢复原有的网格启动器。原有窗口和网格设置会被保留。

## 数据存储

用户数据保存在：

```text
%AppData%\Centre
├── items.json       # 应用条目与排序
├── items.json.bak   # 最近一次有效备份
├── settings.json    # 窗口与外观设置
├── settings.json.bak
├── Icons\           # 用户自定义图标的本地副本
└── IconCache\       # 自动提取的图标缓存
```

- 数据仅保存在本机当前 Windows 用户目录中。
- Centre 不会上传应用列表、设置或图标。
- JSON 文件无法解析时优先恢复 `.bak`；损坏文件会保留为带时间戳的 `.corrupt`。

## 项目结构

```text
centre_app/
├── Assets/               # 程序图标与品牌资源
├── AppDataStore.cs       # 原子 JSON 持久化及备份恢复
├── IconCacheService.cs   # 图标提取、指纹与磁盘缓存
├── PackagedAppService.cs # UWP 枚举、图标与启动
├── LauncherData.cs       # 应用条目与设置模型
├── MainWindow.xaml       # 主界面和设置抽屉
├── MainWindow.xaml.cs    # 启动器交互、排序、搜索与窗口逻辑
├── RenameDialog.cs       # 应用重命名对话框
└── centre_app.csproj     # WPF 项目配置
```

## 当前限制

- 仅支持 Windows。
- 不支持普通文档、文件夹或网址。
- 暂不包含开机启动和手动主题切换。
- 当前 GitHub Release 安装包未使用受信任代码签名证书，Windows 可能显示 SmartScreen 提示。

## 开发说明

项目使用以下主要技术：

- C# 14
- .NET 10
- WPF
- Windows Shell API
- Desktop Window Manager API

提交代码前建议同时验证 Debug 和 Release 构建：

```powershell
dotnet build centre_app.csproj -c Debug
dotnet build centre_app.csproj -c Release
dotnet test tests/Centre.Tests/Centre.Tests.csproj -c Release
```

## 发布

推送 `vX.Y.Z` 标签会触发 GitHub Actions，生成 x64 自包含便携 ZIP、当前用户 Inno Setup 安装包和 `SHA256SUMS.txt`。标签版本必须与项目中的 `Version` 一致。

安装包默认安装到 `%LocalAppData%\Programs\Centre`，创建开始菜单快捷方式，桌面快捷方式可选。卸载时默认保留用户数据，并询问是否一并清除。

如需正式签名，在 GitHub 仓库 Secrets 中配置：

- `WINDOWS_CERTIFICATE_BASE64`
- `WINDOWS_CERTIFICATE_PASSWORD`

## 许可证

本项目采用 [MIT License](LICENSE)。
