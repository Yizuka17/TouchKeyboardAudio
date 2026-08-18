# TouchKeyboardAudio

一个用于 Windows 10 旧版触摸键盘（TabTip）的按键音增益调节工具。

它针对 `C:\Program Files\Common Files\Microsoft Shared\ink\tabskb.dll` 中内嵌的 PCM16 WAVE 音效工作：首次运行保存原版备份，之后每次都从备份重新生成目标增益，因此不会叠加增益。

## 功能

- Fluent 风格 WPF 界面与 Windows 10 Acrylic 背景
- `-20 dB` ～ `+30 dB`，0.5 dB 步进
- 实时显示振幅倍率与预计削顶率
- 一键应用增益 / 恢复原版
- 自动请求管理员权限
- 针对当前确认的 14 个 PCM16 内嵌键盘音效做结构校验

## 构建

仓库自带 GitHub Actions。向 `main` push 后会在 `windows-latest` 上使用 MSBuild 编译 `.NET Framework 4.8` WPF EXE，并上传 `TouchKeyboardAudio-Windows` artifact。

本地也可以使用 Visual Studio / MSBuild 构建 `TouchKeyboardAudio.csproj`。

## 注意

应用非零增益会修改系统 `tabskb.dll` 的内容，因此该 DLL 原有的微软数字签名会失效；Windows Update、SFC 或 DISM 也可能恢复系统原版。程序会把首次运行时的 `tabskb.dll` 备份到：

`C:\ProgramData\TouchKeyboardAudio\tabskb.original.dll`
