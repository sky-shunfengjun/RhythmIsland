# 开发指南

本文描述 v0.1 工程的开发与验证方式。插件、测试和独立主题源码已经分层；当前发布物只包含插件及其频谱组件。

## 1. 开发基线

| 项目 | 固定选择 |
|---|---|
| 操作系统 | Windows 10/11 x64 |
| 目标框架 | `net8.0-windows` |
| ClassIsland | 2.1.0.1 |
| ClassIsland Plugin SDK | 2.1.0.1 |
| NAudio | 2.3.0 稳定版 |
| 测试框架 | xUnit |
| 许可证 | `GPL-3.0-or-later` |

不使用 NAudio 3 预览版。它面向 .NET 9，且在 v0.1 所需功能上没有足以抵消升级风险的收益。

本机相邻的 `D:\My-code\ClassIsland` 仅作为只读接口参考。律动岛不得通过项目引用依赖这份源码，也不得修改它。

## 2. 当前与后续目录

```text
RhythmIsland/
├─ AGENTS.md
├─ RhythmIsland.sln
├─ README.md
├─ LICENSE
├─ src/
│  ├─ RhythmIsland.Plugin/
│  │  ├─ RhythmIsland.csproj
│  │  ├─ Plugin.cs
│  │  ├─ Abstractions/ Models/ Services/
│  │  ├─ Controls/Components/
│  │  └─ Views/SettingsPages/
│  └─ RhythmIsland.Theme/
│     ├─ manifest.yml
│     └─ Styles.axaml
├─ tests/RhythmIsland.Tests/
├─ cipx/RhythmIsland.cipx
└─ docs/
```

架构边界必须保持：声音捕获、分析、帧交换、共享刷新、组件绘制和设置存储不得混成一个类。主题不得编入插件。

## 3. 项目与清单

主项目使用 ClassIsland 插件 SDK 的动态加载和 cipx 打包能力：

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <EnableDynamicLoading>true</EnableDynamicLoading>
  <CreateCipx>true</CreateCipx>
  <Version>0.1.0</Version>
</PropertyGroup>
```

依赖版本必须显式固定，禁止在 v0.1 使用浮动版本或预览包：

```xml
<PackageReference Include="ClassIsland.PluginSdk" Version="2.1.0.1">
  <ExcludeAssets>runtime; native</ExcludeAssets>
</PackageReference>
<PackageReference Include="NAudio" Version="2.3.0" />
```

清单固定使用以下身份信息：

```yaml
id: RhythmIsland
name: 律动岛 RhythmIsland
description: 在 ClassIsland 主界面显示默认扬声器实时频谱的组件插件。
url: https://github.com/sky-shunfengjun/RhythmIsland
author: sky-shunfengjun
entranceAssembly: RhythmIsland.dll
version: 0.1.0.0
apiVersion: 2.1.0.1
icon: icon.png
readme: README.md
repoOwner: sky-shunfengjun
repoName: RhythmIsland
assetsRoot: main
artifactName: RhythmIsland.cipx
supportedOSPlatforms:
  - Windows
```

主题 ID 固定为 `rhythmisland.spectrum-background`，但只存在于独立主题目录。插件 ID、程序集、组件 GUID 和主题 ID 发布后不得随意更名。

## 4. 实现顺序

每个阶段都应保持项目可构建，避免同时引入音频、UI 和主题后才第一次验证。

1. 创建解决方案、主项目、测试项目、清单和最小插件入口。
2. 实现设置模型、范围校验和原子设置存储。
3. 先用生成的正弦波完成频谱算法和单元测试。
4. 接入 NAudio 默认设备回环捕获及设备切换。
5. 实现只依赖模拟频谱帧的绘制控件和布局测试。
6. 注册 ClassIsland 频谱组件、实例设置页和共享刷新时钟。
7. 完成插件设置页、真实设备测试、打包和稳定性验证。
8. 在独立开发周期中实现并发布背景主题。

截至当前版本，步骤 1–6 已实施并有自动测试；真实设备和完整人工体验仍需验证。独立主题只有合法空骨架，不参与构建。

## 5. 构建与测试

在仓库根目录运行：

```powershell
dotnet restore RhythmIsland.sln
dotnet build RhythmIsland.sln -c Debug
dotnet test tests/RhythmIsland.Tests/RhythmIsland.Tests.csproj -c Debug
```

涉及打包或清单资源时再运行：

```powershell
dotnet build src/RhythmIsland.Plugin/RhythmIsland.csproj -c Release
```

ClassIsland Plugin SDK 会在构建后创建：

```text
cipx/RhythmIsland.cipx
```

若包没有生成，先检查 `CreateCipx`、`manifest.yml` 是否复制到输出目录，以及 PowerShell 是否可用。不要手工压缩一个不完整的输出目录来绕过打包错误。

## 6. 自动化测试范围

### 单元测试

- IEEE Float 32 位和 PCM 16/24/32 位转换为正确的单声道样本。
- 静音输入只产生零值，不出现 `NaN` 或无穷值。
- 生成的单频正弦波落入预期的对数频段，允许相邻一柱的窗函数泄漏。
- 灵敏度提高时幅度不下降；攻击速度快于回落速度。
- 停止输入后频谱平滑归零，不永久停留在旧帧。
- 24/32/48/64/96 柱均可生成有限值。
- 插件设置与组件实例设置越界、非法颜色和损坏 JSON 均回退到安全值。
- 保存设置使用临时文件替换，不留下半写入的正式文件。
- 重复开启、连续启停和停止操作不会产生重叠捕获实例。
- 两种组件布局、零尺寸、极窄宽度和全部柱数不产生越界或非法坐标。
- 静音阈值以下不绘制底部退化横线；“底部向上”的绘制坐标位于圆角安全边距内，“上下镜像”保持原有边距。
- 组件幅度在 `0.25×–3.00×` 内缩放并限制到有限的 `[0, 1]` 绘制值。
- 自动收起等待时间、编辑模式保护、声音恢复和关闭自动收起均通过状态测试覆盖。
- 组件刷新重复挂载只订阅一次，移除后正确解绑。
- 停止时清除最新帧，插件源码不注册主题，独立主题使用 `<Styles>` 根对象。

### 集成场景

- 插件启用、停用和 ClassIsland 退出时只存在一个捕获实例。
- 默认设备切换后旧设备被释放，新设备自动开始捕获。
- 没有输出设备、设备被移除、捕获意外停止时能够显示状态并重试。
- 损坏配置恢复后设置页仍可打开并重新保存。

主题安装和主题重载不属于当前插件验收范围。

涉及 Avalonia 控件的自动测试优先使用 Avalonia Headless；无法可靠自动化的宿主视觉树行为保留为人工验收，不编写只验证实现细节的脆弱测试。

## 7. 本地安装与人工验收

### 当前组件版本验收

构建 Release 包后，在用户授权的 `D:\My-code\ClassIsland` Debug 宿主中通过外部插件路径或安装包加载插件，不修改 ClassIsland 源码。随后：

1. 打开“律动岛”设置页，确认初始状态为未启用。
2. 打开主开关，确认状态变为“运行中”，默认扬声器名称正确。
3. 在编辑模式添加“律动岛频谱”，确认插件不自动启用主题。
4. 播放音乐，确认帧时间和峰值更新，组件连续绘制。
5. 在各自的组件设置中用滑块分别设置 120px、300px、幅度和透明度，并用选择框切换不同柱数，确认没有越过主界面圆角或消失；幅度和透明度每次变化 `0.05`，提示保留两位小数。
6. 设置无声自动收起等待时间，暂停音乐确认组件在等待时间后收起；播放音乐确认组件恢复。
7. 切换方向与自选颜色；添加第二个实例，确认两者设置互不影响且共享捕获。
8. 关闭自动收起后暂停音乐，确认组件仍保留；关闭主开关确认组件清空。
9. 关闭宿主并检查日志中没有主题类型转换、插件加载或未处理异常。

默认性能验收目标：ClassIsland 不崩溃、内存不持续增长、主界面没有可见卡顿；在同一台验收机上相对关闭插件时的进程 CPU 增量平均不超过 5 个百分点。结果必须记录机器配置、采样时长和测量方式。

## 8. 日志与验证边界

日志应包含插件版本、当前默认设备显示名、捕获状态、重试原因和异常堆栈。禁止记录原始音频字节、浮点样本或完整频谱历史。

交付说明必须区分：

- `dotnet build`：仅证明编译和资源打包阶段通过。
- `dotnet test`：仅证明已覆盖的纯逻辑和可自动化场景通过。
- 生成 `.cipx`：不代表安装成功。
- 在 ClassIsland 中安装并启动：不代表设备切换、主题兼容和长期性能已经验证。
- 只有执行对应人工步骤后，才能声称该行为已在真实环境中通过。
