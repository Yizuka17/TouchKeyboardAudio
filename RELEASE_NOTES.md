# TouchKeyboardAudio v1.0.0

首个正式版本。

## 亮点

- 真实追踪并使用 Windows 10 `TextInputHost` 的实际键盘音效路径：`MicrosoftWindows.Client.CBS_*\InputApp\Assets\Kbd*.wav`
- 使用 **Float32 Direct Gain**：把微软原版 PCM16 WAV 转为 IEEE Float32 后直接线性增益，不使用 limiter、soft clip、hard clip 或 dither
- 实机确认 TextInputHost 可以播放绝对值大于 `1.0` 的 Float32 WAV 样本，因此 `+20 dB` 可在文件生成阶段保持线性波形，不受 PCM16 满刻度限制
- `-20 dB` ～ `+30 dB`，0.5 dB 步进；首次打开默认 `+20 dB`
- 一键恢复微软原版 PCM16 WAV
- 先生成全部 5 个目标文件再替换；失败时尝试整体回滚
- 自动停止/重启 `TabTip` 与 `TextInputHost`
- 自动定位 `MicrosoftWindows.Client.CBS_*` SystemApps 包
- Windows 10 UWP / Fluent 风格界面与 Acrylic 背景
- 不修改 `TextInput.dll`，不修改 `tabskb.dll`

## 修改的音效

- `KbdKeyTap.wav`
- `KbdSpacebar.wav`
- `KbdFunction.wav`
- `KbdAccentPicker.wav`
- `KbdSwipeGesture.wav`

## 适用范围

当前版本面向 Windows 10 上使用 `MicrosoftWindows.Client.CBS_*` / `TextInputHost` 触摸键盘音效路径的系统环境。程序需要管理员权限，因为会替换 `SystemApps` 下的键盘 WAV 资源。

建议首次使用先保持默认 `+20 dB`。如果需要撤销，点击“恢复原版”即可将 5 个音效恢复为微软原始 PCM16 文件。
