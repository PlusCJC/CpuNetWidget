# CPU 网速悬浮窗

使用 C#、WPF 和 .NET 8 编写的 Windows 桌面监控小组件。程序提供完整监控面板、迷你监控条、实时网速曲线、系统托盘和贴边收起功能，默认以普通用户权限运行。

## 功能

- 实时显示 CPU 使用率；
- 读取 CPU 温度，优先使用硬件传感器，无数据时回退到 Windows ACPI 温区；
- 显示当前下载和上传速率；
- 显示最近 1、5 或 10 分钟的上传、下载曲线；
- CPU、温度、下载、上传和曲线均可独立开关；
- 支持自动选择或手动指定温度传感器；
- 支持完整面板和迷你监控条两种模式；
- 迷你监控条可拖动到任意屏幕的四条边并自动收起；
- 支持置顶、开机启动、最小化到托盘和可选管理员权限；
- 发生读取错误时保留其他监控功能，并记录有限大小的本地诊断日志。

## 运行

系统要求：

- Windows 10 或 Windows 11；
- x64 处理器；
- 自包含版本不要求预装 .NET；
- 框架依赖版本要求安装 .NET 8 Desktop Runtime x64。

直接运行：

```text
publish\CpuNetWidget.exe
```

Windows 可能对未进行商业代码签名的本地 EXE 显示 SmartScreen 提示。请只运行自己编译或从可信来源获得的文件。

## 窗口与操作

完整面板默认尺寸为 `390 × 330 DIP`，最小尺寸为 `350 × 300 DIP`。在完整面板中：

- 拖动非按钮区域可移动窗口；
- 点击 `◉` 切换到迷你监控条；
- 点击最小化按钮隐藏到系统托盘；
- 点击红色 `×` 完全退出；
- 点击齿轮打开设置。

迷你监控条尺寸为 `50 × 228 DIP`，从上到下显示 CPU、温度、下载和上传。其操作为：

- 拖动监控条可移动；
- 双击恢复完整面板；
- 右键打开托盘菜单；
- 拖到当前显示器边缘 24 DIP 范围内会自动吸附；
- 左右边缘收起为 `7 × 50 DIP` 灰条，上下边缘收起为 `50 × 7 DIP` 灰条；
- 点击灰条后，监控条会在距离边缘 32 DIP 的位置恢复。

DIP 是 Windows 与缩放比例无关的界面单位。例如在 125% 缩放下，迷你监控条约为 `63 × 285 px`，灰条约为 `9 × 63 px`。

## 设置

设置界面包含：

- CPU 使用率、CPU 温度、下载和上传监控开关，首次运行默认全部开启；
- 实时网速曲线开关；
- 1、5、10 分钟曲线范围；
- 自动或手动温度传感器；
- 迷你监控条模式；
- 靠近屏幕边缘时自动收起；
- 窗口始终置顶；
- 登录 Windows 后自动启动；
- 启动时请求管理员权限，默认关闭。

普通设置保存在：

```text
HKEY_CURRENT_USER\Software\CpuNetWidget
```

开机启动项保存在：

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
```

所有注册表项都位于当前用户范围，不写入系统级注册表。

## 数据来源与准确性

### CPU 使用率

CPU 使用率通过 Windows `GetSystemTimes` 的空闲、内核和用户计数器差值计算。程序使用相邻有效样本，不依赖性能计数器名称或系统语言。首次采样、系统计数器回退或无效样本显示 `--%`，下一次有效采样会自动恢复。

### 网络速率

程序读取处于启用状态的非回环、非隧道网络适配器字节计数，并优先使用包含默认网关的适配器。时间间隔由单调计时器计算，因此修改系统时间不会造成速率跳变。

- 第一次读取仅建立基线；
- 适配器断开、计数器重置或读取失败时不会产生负速率；
- 失败的适配器重新出现后会先重新建立基线，避免异常峰值；
- 显示单位按 1024 进位，但沿用常见的 `KB/s`、`MB/s` 标记；
- 速率包含该适配器上的所有进程流量，不是单个应用流量；
- VPN、虚拟交换机、流量复制软件可能导致系统适配器计数与任务管理器的口径不同。

曲线使用单调时间戳定位样本，而不是简单假设每次刷新严格间隔一秒，因此 1、5、10 分钟范围代表实际经过时间。

### CPU 温度

温度读取顺序：

1. `LibreHardwareMonitorLib` 暴露的 CPU 温度传感器；
2. Windows `Thermal Zone Information` ACPI 温区。

自动模式优先选择 CPU Package、Tdie、Tctl、Core Average 等传感器，只接受 `-20°C` 到 `150°C` 的合理值。不同主板、固件和处理器暴露的传感器不同，因此温度可能与 BIOS 或厂商软件存在少量差异。

少数电脑只有在管理员权限下才能读取 CPU Package。启用“启动时请求管理员权限”后，下次启动会显示标准 Windows UAC 确认；关闭该选项后不会请求管理员权限。CPU 使用率和网络速率不需要管理员权限。

LibreHardwareMonitor 可能调用底层硬件访问组件。Windows Defender 或“易受攻击的驱动程序阻止列表”可能阻止这类访问；不要为了显示温度而关闭 Windows 安全保护。程序不会修改系统阻止列表，也不会自动安装 PawnIO。访问被阻止时会安全降级到其他传感器或显示 `--°C`。

## 托盘与退出

关闭完整面板的系统关闭动作会隐藏到托盘；红色 `×` 和托盘菜单中的“退出”会释放温度监控器、性能计数器、托盘图标和菜单资源并终止程序。双击托盘图标可以恢复窗口。

## 诊断日志与隐私

程序不上传监控数据、不发送遥测，也不主动连接远程服务器。诊断日志只在读取或程序异常时写入当前电脑：

```text
%LOCALAPPDATA%\CpuNetWidget\Logs\app.log
```

日志最大约 1 MiB，超过后轮换为 `app.previous.log`，只保留当前和上一份日志。日志可能包含 Windows 返回的异常消息和本地文件路径；提交日志前请自行检查其中内容。

## 编译

要求安装 .NET 8 SDK x64。在 PowerShell 中执行：

```powershell
.\Build.ps1
```

默认生成包含 .NET 运行时的自包含单文件：

```text
publish\CpuNetWidget.exe
```

生成依赖本机 .NET 8 Desktop Runtime、体积更小的版本：

```powershell
.\Build.ps1 -FrameworkDependent
```

NuGet 官方源暂时不可访问、且已经单独完成漏洞检查时，可以跳过本次构建的联网审计：

```powershell
.\Build.ps1 -SkipPackageAudit
```

`-SkipPackageAudit` 只跳过 NuGet 漏洞数据库查询，不会跳过编译，也不会改变依赖版本。正式发布前仍应在可联网环境执行漏洞检查。

| 发布方式 | 是否携带 .NET | 目标电脑要求 | 特点 |
|---|---|---|---|
| 默认自包含 | 是 | Windows 10/11 x64 | 文件较大，可直接运行 |
| `-FrameworkDependent` | 否 | 已安装 .NET 8 Desktop Runtime x64 | 文件较小 |

构建脚本优先使用仓库中的 `.dotnet\dotnet.exe`，否则使用系统 `dotnet`。脚本会检查 .NET 8 SDK、生成应用图标、发布 EXE，并复制第三方许可证说明。

## 安装与卸载

仓库提供标准 Windows 安装器源码：

```text
Installer\CpuNetWidget.iss
```

安装 [Inno Setup 6](https://jrsoftware.org/isdl.php) 后，在 PowerShell 中执行：

```powershell
.\BuildSetup.ps1
```

该命令会先生成默认的自包含应用，再生成：

```text
setup\CpuNetWidget-Setup.exe
```

已有最新的 `publish` 文件时，可以只编译安装器：

```powershell
.\BuildSetup.ps1 -SkipAppBuild
```

如果 `ISCC.exe` 不在默认目录，可显式指定：

```powershell
.\BuildSetup.ps1 -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

安装器默认建议安装到当前用户的 `%LOCALAPPDATA%\Programs\CpuNetWidget`，并且始终显示安装目录页面；用户可以直接输入或通过“浏览”按钮选择其他目录，升级时会自动带入上次使用的路径。安装到当前用户可写目录时不要求管理员权限。安装过程中还可以选择桌面快捷方式和开机启动。

可从 Windows“设置 > 应用 > 已安装的应用”或开始菜单中的“卸载 CPU 网速悬浮窗”执行卸载。卸载程序会先结束正在运行的悬浮窗，然后删除：

- 安装目录、开始菜单和桌面快捷方式；
- `HKEY_CURRENT_USER\Software\CpuNetWidget` 下的全部应用设置；
- 当前用户的 `CpuNetWidget` 开机启动项；
- `%LOCALAPPDATA%\CpuNetWidget` 下的全部运行数据，包括 `Logs\app.log` 和轮换日志。

卸载是不可恢复的；如需保留诊断日志，请在卸载前自行复制。直接运行 `publish\CpuNetWidget.exe` 属于便携使用，不会自动注册卸载入口。

## 温度诊断工具

如果温度显示异常，可以运行源码中的诊断工具查看系统实际暴露的传感器：

```powershell
dotnet run --project .\Diagnostics\TemperatureProbe\TemperatureProbe.csproj -c Release
```

建议分别以普通权限和管理员权限运行并比较结果。该工具只读取传感器，不修改硬件配置。

## 常见问题

### 温度一直显示 `--°C`

1. 打开设置，保持传感器为“自动选择”；
2. 尝试启用管理员权限并重启；
3. 使用温度诊断工具检查是否存在 CPU Package/Tdie/Tctl；
4. 检查诊断日志；
5. 如果硬件和 ACPI 都没有温度数据，则该电脑可能没有向 Windows 暴露可用传感器。

### 网速显示与任务管理器不同

确认是否正在使用 VPN、虚拟机、Hyper-V、WSL 或厂商网络加速软件。不同工具可能选择不同适配器或使用不同单位口径。

### 窗口找不到

检查系统托盘并双击应用图标。窗口位置会限制在当前显示器工作区内；贴边灰条需要单击后恢复。

### 设置无法保存

程序仍会在本次运行中使用新设置。检查当前用户是否有权写入 `HKEY_CURRENT_USER\Software\CpuNetWidget`，并查看诊断日志。

### 开机启动失效

移动 EXE 后，重新打开设置并关闭、再开启“登录 Windows 后自动启动”，让程序写入新的绝对路径。

## 源码结构

```text
CpuNetWidget/
  App.xaml.cs                         启动与全局异常处理
  AppDiagnostics.cs                  有限大小的本地诊断日志
  AppSettings.cs                     当前用户设置持久化
  MainWindow.xaml(.cs)               主界面、曲线、托盘与贴边逻辑
  SettingsWindow.xaml(.cs)           设置界面
  PrivilegeHelper.cs                 管理员权限检测与 UAC 重启
  Monitoring/CpuUsageReader.cs       CPU 使用率
  Monitoring/NetworkSpeedReader.cs   上传/下载速率
  Monitoring/CpuTemperatureReader.cs 温度与 ACPI 回退
Diagnostics/TemperatureProbe/        温度传感器诊断工具
Build.ps1                             发布脚本
BuildSetup.ps1                        发布应用并生成 Setup EXE
Installer/CpuNetWidget.iss            安装、快捷方式与卸载清理规则
THIRD-PARTY-NOTICES.txt               第三方组件说明
SECURITY.md                           安全与隐私说明
AUDIT.md                              最近一次代码审计记录
LICENSE                               项目 MIT 许可证
```

## 已知限制

- 硬件温度的可用性和准确性取决于主板、固件、驱动及传感器权限；
- 本项目没有内核驱动，不会绕过 Windows 或硬件访问限制；
- 网速是适配器计数差值，不能区分每个进程；
- 程序发布文件当前未进行商业代码签名；
- 当前发布目标固定为 Windows x64。

## 第三方组件

项目使用 `LibreHardwareMonitorLib 0.9.6` 读取硬件传感器，许可证为 Mozilla Public License 2.0。详细信息见 `THIRD-PARTY-NOTICES.txt`。

本次审计使用 NuGet 官方漏洞源检查了顶级包及全部传递依赖，未报告已知漏洞。漏洞数据库会持续变化，后续发布前仍应重新执行：

```powershell
dotnet list .\CpuNetWidget\CpuNetWidget.csproj package --vulnerable --include-transitive
```

## 许可证

本项目自身代码采用 [MIT License](LICENSE)。你可以使用、复制、修改、发布和分发代码，但必须在副本或主要代码中保留原始版权及许可声明。

项目引用的第三方组件继续适用各自的许可证；`LibreHardwareMonitorLib` 的 MPL-2.0 声明和来源信息见 `THIRD-PARTY-NOTICES.txt`。

## Git 历史

查看修改记录：

```powershell
git log --oneline
```

每项完整功能或审计修复应单独提交，便于审查和回退。
