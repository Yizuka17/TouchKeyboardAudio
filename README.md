# TouchKeyboardAudio

一个用于 Windows 10 触摸键盘按键反馈音的增益调节工具。

经过实际追踪，Windows 10 触摸键盘声音由 `TextInputHost.exe` / `TextInput.dll` 通过 XAudio2 播放，实际使用的资源位于：

`C:\Windows\SystemApps\MicrosoftWindows.Client.CBS_*\InputApp\Assets\Kbd*.wav`

本工具只修改这 5 个实际播放的 WAV：

- `KbdKeyTap.wav`
- `KbdSpacebar.wav`
- `KbdFunction.wav`
- `KbdAccentPicker.wav`
- `KbdSwipeGesture.wav`

同包根目录 `Assets\Kbd*.wav` 始终作为微软原版 PCM16 基线；程序不会修改 `TextInput.dll` 或 `tabskb.dll`。

## Float32 Direct Gain

实机测试确认 TextInputHost 的音效加载器可以直接播放 `WAVE_FORMAT_IEEE_FLOAT` 32-bit float WAV，而且可以接受绝对值大于 `1.0` 的样本。

因此当前版本不再使用 limiter、soft clip、hard clip 或 dither。每次应用时，程序直接执行：

`微软原版 PCM16 -> 精确归一化 float -> 线性 gain -> IEEE Float32 WAV`

例如 `+20 dB`：

`PCM16 sample / 32768 -> float -> ×10`

float WAV 可以保存 `> 1.0` 的中间样本，因此不会因为 16-bit 的 `±32767` 上限而切平峰值。之后 TextInputHost 自己的内部音量增益再参与播放链。

## 功能

- Fluent 风格 WPF 界面与 Windows 10 Acrylic 背景
- `-20 dB` ～ `+30 dB`，0.5 dB 步进
- 首次打开默认停在 `+20 dB`，只有点击“应用”才写入
- 实时显示倍率与生成后的 Float32 峰值；`> 1.0` 在 float WAV 中是合法数据
- 根据当前已逆出的 TextInput 默认 2% 内部增益显示一个混音峰值估算
- 一键恢复微软原版 PCM16 WAV
- 先生成全部 5 个临时 WAV，再一次性替换；替换失败会尝试自动回滚 5 个原版文件
- 自动停止并重新拉起 `TabTip` / `TextInputHost`
- 自动请求管理员权限
- 自动定位 `MicrosoftWindows.Client.CBS_*` SystemApps 包
- 不修改 `TextInput.dll`
- 不修改 `tabskb.dll`

## 为什么 Float32 能解决 PCM16 的失真

PCM16 的数字满刻度固定在 `-32768..32767`。当原始波形乘以较大增益后超过这个范围，只能削顶或压缩，都会改变波形。

IEEE Float32 WAV 没有这个整数容器限制。对于这条已经验证可用的 TextInputHost/XAudio2 播放路径，可以把大于 `1.0` 的样本直接保存在 WAV 里，由后面的 float 音频链继续处理。因此在文件生成阶段可以保持完全线性的增益关系。

最终输出设备仍然存在满刻度限制，所以极端高增益仍可能在播放链后端削顶；当前 GUI 上限保持在 `+30 dB`。

## 恢复原版

“恢复原版”会把：

`MicrosoftWindows.Client.CBS_*\Assets\Kbd*.wav`

重新复制到：

`MicrosoftWindows.Client.CBS_*\InputApp\Assets\Kbd*.wav`

恢复微软原始 PCM16 文件。

## 构建

仓库自带 GitHub Actions。push 或 pull request 后会在 `windows-latest` 上使用 MSBuild 编译 `.NET Framework 4.8` WPF EXE，并上传 `TouchKeyboardAudio-Windows` artifact。

本地也可以使用 Visual Studio / MSBuild 构建 `TouchKeyboardAudio.csproj`。
