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

程序把同一个 SystemApps 包根目录下 `Assets\Kbd*.wav` 当作微软原版基线，因此每次应用都会从原版重新生成目标增益，不会叠加；“恢复原版”也会直接从这套基线恢复。

## 功能

- Fluent 风格 WPF 界面与 Windows 10 Acrylic 背景
- `-20 dB` ～ `+30 dB`，0.5 dB 步进
- 首次打开默认停在 `+20 dB`，只有点击“应用”才会写入
- 实时显示振幅倍率与预计 PCM 削顶率
- 一键应用增益 / 恢复微软原版 WAV
- 自动停止并重新拉起 `TabTip` / `TextInputHost`，处理资源占用
- 自动请求管理员权限
- 自动定位 `MicrosoftWindows.Client.CBS_*` SystemApps 包
- 不修改 `TextInput.dll`，不修改 `tabskb.dll`

## 构建

仓库自带 GitHub Actions。push 或 pull request 后会在 `windows-latest` 上使用 MSBuild 编译 `.NET Framework 4.8` WPF EXE，并上传 `TouchKeyboardAudio-Windows` artifact。

本地也可以使用 Visual Studio / MSBuild 构建 `TouchKeyboardAudio.csproj`。

## 注意

正增益是直接修改 WAV PCM 振幅，因此较高增益会产生削顶。程序会在应用前显示预计削顶比例；`+20 dB` 相当于振幅约 `10×`。

程序会修改 `C:\Windows\SystemApps` 中的键盘 WAV 资源，所以需要管理员权限。Windows Update 可能恢复这些文件；点击“恢复原版”也可以随时把 `InputApp\Assets` 还原为包内根目录的微软原版资源。

程序还会在 `C:\ProgramData\TouchKeyboardAudio\TextInputAssets` 保存一份只读意义上的安全副本，但正常应用和恢复都优先使用当前 SystemApps 包里的微软原版基线。
