# TouchKeyboardAudio

一个用于 Windows 10 触摸键盘按键反馈音的增益调节工具。

经过实际追踪，当前 Windows 10 的触摸键盘声音由 `TextInputHost.exe` / `TextInput.dll` 通过 XAudio2 播放，实际使用的资源位于：

`C:\Windows\SystemApps\MicrosoftWindows.Client.CBS_*\InputApp\Assets\Kbd*.wav`

本工具只修改这 5 个实际播放的 PCM16 WAV：

- `KbdKeyTap.wav`
- `KbdSpacebar.wav`
- `KbdFunction.wav`
- `KbdAccentPicker.wav`
- `KbdSwipeGesture.wav`

首次运行会把同包根目录 `Assets\Kbd*.wav` 复制到 `C:\ProgramData\TouchKeyboardAudio\TextInputAssets\Baseline_*.wav` 作为只读处理基线。之后每次应用都从这套基线重新生成目标音效，不会叠加增益；“恢复原版”也直接从这套基线恢复。

## DSP 模式

### 智能限制器（推荐）

处理链：

`PCM16 -> float -> requested gain -> 1.5 ms look-ahead limiter -> -1.0 dBFS ceiling -> 10 ms release -> TPDF dither -> PCM16`

限制器在整个 WAV 已经拿到手的前提下提前观察后续峰值，因此可以在峰值真正到来之前降低增益，避免原来那种直接把超过 16-bit 上限的波形切平。多声道 WAV 使用 linked gain，避免破坏左右声道关系。

### 线性安全（无削顶）

计算 5 个原始音效中最高峰值所允许的全局线性增益。如果用户请求值超过安全余量，程序自动把实际增益限制到安全上限。这个模式不做动态压缩，也不产生硬削顶，并保持 5 个音效之间的相对响度。

### 硬削顶（A/B 对比）

保留传统直接截断方式，主要用于和智能限制器做听感对比。

## 功能

- Fluent 风格 WPF 界面与 Windows 10 Acrylic 背景
- `-20 dB` ～ `+30 dB`，0.5 dB 步进
- 默认 `+20 dB` + 智能限制器
- 实时显示预计受限峰值、线性安全上限和最大 gain reduction
- TPDF dither 后重新量化为 PCM16
- 一键应用增益 / 恢复微软原版 WAV
- 先生成全部 5 个临时 WAV，再开始替换；替换中任何一步失败都会尝试自动回滚全部目标文件
- 自动停止并重新拉起 `TabTip` / `TextInputHost`
- 自动请求管理员权限
- 自动定位 `MicrosoftWindows.Client.CBS_*` SystemApps 包
- 不修改 `TextInput.dll`，不修改 `tabskb.dll`

## 构建

仓库自带 GitHub Actions。push 或 pull request 后会在 `windows-latest` 上使用 MSBuild 编译 `.NET Framework 4.8` WPF EXE，并上传 `TouchKeyboardAudio-Windows` artifact。

本地也可以使用 Visual Studio / MSBuild 构建 `TouchKeyboardAudio.csproj`。

## 关于“无损 +20 dB”

16-bit PCM 有固定的数字满刻度。原始峰值如果在放大后超过 0 dBFS，就不可能在同一个 PCM16 容器里既保持原波形比例、又完整保留超过满刻度的幅度。因此工具提供两种尽量干净的方案：线性安全模式保证不削顶；智能限制器则允许更高的主观响度，但只压低真正会越界的峰值，避免 hard clipping。
