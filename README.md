<div align="center">
  <img src="Assets/startIcon.png" width="112" height="112" alt="Centre 图标">

  # Centre

  一个简洁、可自定义的 Windows 全屏应用启动器。
</div>

Centre 使用 WPF 和 .NET 10 构建，提供类似 Launchpad 的应用启动体验。应用列表完全由用户维护，不会自动扫描或导入开始菜单中的软件。

## 功能特性

- 默认使用空白应用列表，仅支持手动添加。
- 支持添加 `.exe`、`.lnk` 和 `.appref-ms`。
- 支持通过文件选择器批量添加，也可以直接拖放文件。
- 自动读取 Windows Shell 图标，读取失败时显示字母占位图标。
- 支持搜索、分页、鼠标滚轮和方向键翻页。
- 支持拖拽排序以及通过页码圆点跨页移动。
- 右键菜单支持重命名、更换图标、恢复默认图标和删除。
- 支持全屏和固定尺寸窗口两种显示模式。
- 提供可选的中央悬浮搜索模式，采用超大圆角和一体化结果面板。
- 悬浮搜索模式自动跟随 Windows 明暗主题，并支持键盘选择与启动。
- 可调整窗口宽高、网格行列数和应用图标大小。
- 使用 `Shift + Tab` 全局显示或隐藏，使用 `Esc` 返回桌面。
- 应用列表与设置自动保存在当前 Windows 用户目录中。

## 界面说明

| 控件或操作 | 功能 |
| --- | --- |
| 右上角 `＋` | 选择并添加一个或多个应用 |
| 右上角设置按钮 | 打开外观与窗口设置 |
| 搜索框 | 按应用名称实时筛选 |
| 拖放文件 | 添加受支持的应用或快捷方式 |
| 拖动应用图标 | 调整应用顺序 |
| 右键应用图标 | 重命名、更换图标、恢复图标或删除 |
| 鼠标滚轮 / `←` `→` | 切换页面 |
| `Shift + Tab` | 全局显示或隐藏 Centre |
| `Esc` | 隐藏 Centre；设置打开时先关闭设置 |

在“中央悬浮搜索”模式中，输入内容后面板会纵向展开搜索结果：

- `↑` / `↓`：切换选中的搜索结果。
- `Enter`：启动当前结果。
- `Esc`：先清空搜索内容，再次按下时隐藏 Centre。

> 删除操作只会移除 Centre 中的启动条目，不会删除原始程序或快捷方式。

## 运行环境

- Windows 10 或 Windows 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

如需从源码构建，还需要安装 .NET 10 SDK。

## 快速开始

### 直接运行

构建完成后运行：

```text
bin/Release/net10.0-windows/Centre.exe
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

默认配置为全屏显示、`1200 × 760` 窗口尺寸、`8 × 5` 网格和 76 像素图标。

中央悬浮搜索模式开启后，Centre 会使用全屏背景承载居中的浮动搜索面板；关闭后恢复原有的网格启动器。原有窗口和网格设置会被保留。

## 数据存储

用户数据保存在：

```text
%AppData%\Centre
├── items.json       # 应用条目与排序
├── settings.json    # 窗口与外观设置
└── Icons\           # 用户自定义图标的本地副本
```

- 数据仅保存在本机当前 Windows 用户目录中。
- Centre 不会上传应用列表、设置或图标。
- JSON 文件无法解析时，原文件会被重命名为带时间戳的 `.corrupt` 备份，然后恢复默认数据。

## 项目结构

```text
centre_app/
├── Assets/               # 程序图标与品牌资源
├── AppDataStore.cs       # JSON 持久化及自定义图标管理
├── LauncherData.cs       # 应用条目与设置模型
├── MainWindow.xaml       # 主界面和设置抽屉
├── MainWindow.xaml.cs    # 启动器交互、排序、搜索与窗口逻辑
├── RenameDialog.cs       # 应用重命名对话框
└── centre_app.csproj     # WPF 项目配置
```

## 当前限制

- 仅支持 Windows。
- 仅支持 `.exe`、`.lnk` 和 `.appref-ms`，不支持普通文档、文件夹或网址。
- 全局快捷键固定为 `Shift + Tab`，暂不支持自定义。
- 暂不包含开机启动、主题切换和自动更新功能。
- 多显示器环境下当前以主显示器作为全屏显示区域。

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
```

## 许可证

本项目采用 [MIT License](LICENSE)。
