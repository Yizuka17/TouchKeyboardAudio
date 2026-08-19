# System theme and accent integration

TouchKeyboardAudio's Fluent shell prefers Windows-provided theme state instead of hard-coded colors.

- `Windows.UI.ViewManagement.UISettings.GetColorValue` is queried for `Background`, `Foreground`, `Accent`, `AccentDark1`, and `AccentLight1`.
- `UISettings.AdvancedEffectsEnabled` controls whether the Acrylic compatibility layer is enabled.
- The desktop window listens for `WM_DWMCOLORIZATIONCOLORCHANGED`, `WM_SETTINGCHANGE`, `WM_THEMECHANGED`, `WM_SYSCOLORCHANGE`, and `WM_DWMCOMPOSITIONCHANGED`, then re-queries the system values on the WPF dispatcher.
- If WinRT `UISettings` activation is unavailable, accent falls back to the documented Win32 `DwmGetColorizationColor` API. The existing `AppsUseLightTheme` registry value is used only as the final dark/light fallback.
- Windows 10 classic WPF has no public Acrylic API; `SetWindowCompositionAttribute` remains isolated to the background Acrylic compatibility layer only.

The Float32 keyboard-audio processing path is unchanged by this feature.
