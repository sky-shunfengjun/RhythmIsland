# 律动岛 · RhythmIsland

<img src="icon.png" alt="律动岛图标" width="128" height="128" />

律动岛是面向 ClassIsland 2.x 的实时音频频谱组件插件。

> 当前版本：`1.0.0.0 公开测试版`

插件只分析默认扬声器正在播放的系统声音，不是录音软件。插件默认关闭，必须由用户主动打开声音捕获。

## 安装和开始使用

1. 从 [GitHub Releases](https://github.com/sky-shunfengjun/RhythmIsland/releases) 下载 `RhythmIsland.cipx` 后本地安装。
2. 重启 ClassIsland。
3. 打开“设置 → 插件 → 律动岛”，打开声音捕获总开关。
4. 进入 ClassIsland 主界面编辑模式，添加“律动岛频谱”组件。
5. 播放声音，观察组件、设备名称、最近频谱帧时间和峰值是否持续更新。

如果没有频谱，请先确认主开关已打开、系统存在默认扬声器，并且确实有声音输出。

## 组件功能

每个“律动岛频谱”组件都有独立设置，不会改变其他组件。

- **外观**：柱状频谱、平滑线条、曲线填充；底部向上或上下镜像；颜色与渐变；激光发光；长度和透明度。
- **频谱**：细节数量、中心镜像、频率均衡补偿、幅度和刷新率。
- **功能**：无声自动收起。

中心镜像默认开启，会把低频放在中间并向两侧展开。频率均衡补偿提供“原始”“均衡”和“突出高频”三档，默认使用“均衡”。

颜色来源包括主题色、音乐封面（SMTC）和自选颜色。音乐封面可以使用“鲜艳”“柔和”或“偏色”模式；没有可用封面时会自动回退到主题色。渐变提供关闭、静态和动态三种方式；动态渐变提供慢速、适中和快速三档。发光默认关闭。

## 配套背景主题

背景主题是单独的可选安装包，不包含在插件包中。安装插件后，可以从 [GitHub Releases](https://github.com/sky-shunfengjun/RhythmIsland/releases) 下载 `RhythmIsland.Theme.zip`，并将它放在 Fluent 或 Classic 之后加载。

背景主题的开关和设置位于律动岛插件设置页。它只控制背景频谱层，不会关闭普通频谱组件；声音捕获总开关仍然由插件统一控制。

## 常见情况

### 主开关已打开但状态为停止

可以在插件设置页点击“重启捕获”。如果系统没有默认输出设备，状态会显示“未连接”；设备恢复后插件会自动尝试重新连接。

### 音乐封面取色不可用

部分播放器或较早 Windows 10 版本可能无法提供 SMTC 封面。此时频谱仍然正常工作，只是颜色回退到主题色。

### 更新插件后没有变化

插件更新、安装或卸载后通常需要重启 ClassIsland 才会加载新的程序集。

## 兼容范围和状态

- Windows 10/11 x64
- ClassIsland 2.1.0.1
- `1.0.0.0 公开测试版`

Fluent 和 Classic 是主要兼容目标。第三方主题、不同显示器切换、长期运行和较早 Windows 10 的 SMTC 降级路径仍处于公开测试范围。

## 隐私说明

音频和媒体封面只在内存中处理，不写入文件、不联网、不上传，也不保存播放历史。

## 第三方依赖与说明

- [ClassIsland.PluginSdk 2.1.0.1](https://www.nuget.org/packages/ClassIsland.PluginSdk/2.1.0.1)：提供 ClassIsland 插件和组件接口。
- [NAudio 2.3.0](https://www.nuget.org/packages/NAudio/2.3.0)：负责默认扬声器的 WASAPI 回环捕获，源码见 [NAudio/NAudio](https://github.com/naudio/NAudio)。
- 界面使用 ClassIsland 提供的 [Avalonia](https://github.com/AvaloniaUI/Avalonia) 运行环境，封面读取使用 Windows [SMTC](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager) 系统接口。

仓库首页展示图中的歌曲信息和封面来自 [MediaIsland](https://github.com/bywhite0/MediaIsland)，歌词来自 [ExtraIsland](https://github.com/LiPolymer/ExtraIsland)。这两个插件不是律动岛的运行依赖。

本项目在需求、代码、测试和文档编写过程中使用了 AI 工具辅助；最终内容由项目作者审阅、修改和验证。

作者：`sky-shunfengjun`
许可证：`GPL-3.0-or-later`
