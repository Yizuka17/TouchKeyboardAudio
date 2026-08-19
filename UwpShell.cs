using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace TouchKeyboardAudioUwp
{
    sealed class ThemeSnapshot
    {
        public bool Dark;
        public bool AdvancedEffectsEnabled;
        public Color Accent;
        public Color AccentDark1;
        public Color AccentLight1;
        public bool FromUiSettings;
    }

    static class SystemTheme
    {
        const int UIColorBackground = 0;
        const int UIColorForeground = 1;
        const int UIColorAccentDark1 = 4;
        const int UIColorAccent = 5;
        const int UIColorAccentLight1 = 6;

        [DllImport("dwmapi.dll")]
        static extern int DwmGetColorizationColor(
            out uint colorization,
            [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

        public static ThemeSnapshot Read()
        {
            ThemeSnapshot value;
            if (TryReadUiSettings(out value))
                return value;

            Color accent = ReadDwmAccent();
            bool dark = ReadRegistryDarkFallback();

            return new ThemeSnapshot
            {
                Dark = dark,
                AdvancedEffectsEnabled = true,
                Accent = accent,
                AccentDark1 = Blend(accent, Colors.Black, 0.16),
                AccentLight1 = Blend(accent, Colors.White, 0.16),
                FromUiSettings = false
            };
        }

        static bool TryReadUiSettings(out ThemeSnapshot snapshot)
        {
            snapshot = null;

            try
            {
                Type settingsType = Type.GetType(
                    "Windows.UI.ViewManagement.UISettings, Windows, ContentType=WindowsRuntime",
                    false);
                Type colorType = Type.GetType(
                    "Windows.UI.ViewManagement.UIColorType, Windows, ContentType=WindowsRuntime",
                    false);

                if (settingsType == null || colorType == null)
                    return false;

                object settings = Activator.CreateInstance(settingsType);
                MethodInfo getColor = settingsType.GetMethod("GetColorValue", new[] { colorType });
                if (settings == null || getColor == null)
                    return false;

                Color background = ReadProjectedColor(
                    getColor.Invoke(settings, new[] { Enum.ToObject(colorType, UIColorBackground) }));
                Color foreground = ReadProjectedColor(
                    getColor.Invoke(settings, new[] { Enum.ToObject(colorType, UIColorForeground) }));
                Color accent = ReadProjectedColor(
                    getColor.Invoke(settings, new[] { Enum.ToObject(colorType, UIColorAccent) }));
                Color accentDark1 = ReadProjectedColor(
                    getColor.Invoke(settings, new[] { Enum.ToObject(colorType, UIColorAccentDark1) }));
                Color accentLight1 = ReadProjectedColor(
                    getColor.Invoke(settings, new[] { Enum.ToObject(colorType, UIColorAccentLight1) }));

                bool advancedEffects = true;
                PropertyInfo advanced = settingsType.GetProperty("AdvancedEffectsEnabled");
                if (advanced != null)
                {
                    object raw = advanced.GetValue(settings, null);
                    if (raw != null)
                        advancedEffects = Convert.ToBoolean(raw);
                }

                snapshot = new ThemeSnapshot
                {
                    Dark = Luma(background) < Luma(foreground),
                    AdvancedEffectsEnabled = advancedEffects,
                    Accent = ForceOpaque(accent),
                    AccentDark1 = ForceOpaque(accentDark1),
                    AccentLight1 = ForceOpaque(accentLight1),
                    FromUiSettings = true
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        static Color ReadProjectedColor(object value)
        {
            if (value == null)
                throw new InvalidOperationException("UISettings returned no color.");

            Type type = value.GetType();
            return Color.FromArgb(
                ReadByte(type, value, "A"),
                ReadByte(type, value, "R"),
                ReadByte(type, value, "G"),
                ReadByte(type, value, "B"));
        }

        static byte ReadByte(Type type, object value, string property)
        {
            PropertyInfo info = type.GetProperty(property);
            if (info == null)
                throw new MissingMemberException(type.FullName, property);
            return Convert.ToByte(info.GetValue(value, null));
        }

        static Color ReadDwmAccent()
        {
            try
            {
                uint argb;
                bool opaque;
                if (DwmGetColorizationColor(out argb, out opaque) >= 0)
                {
                    return Color.FromRgb(
                        (byte)((argb >> 16) & 0xFF),
                        (byte)((argb >> 8) & 0xFF),
                        (byte)(argb & 0xFF));
                }
            }
            catch { }

            return Color.FromRgb(0x00, 0x78, 0xD7);
        }

        static bool ReadRegistryDarkFallback()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value != null && Convert.ToInt32(value) == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        static Color ForceOpaque(Color value)
        {
            return Color.FromRgb(value.R, value.G, value.B);
        }

        static double Luma(Color value)
        {
            return (0.2126 * value.R + 0.7152 * value.G + 0.0722 * value.B) / 255.0;
        }

        public static bool UseDarkText(Color background)
        {
            return Luma(background) > 0.60;
        }

        static Color Blend(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * amount),
                (byte)Math.Round(a.G + (b.G - a.G) * amount),
                (byte)Math.Round(a.B + (b.B - a.B) * amount));
        }
    }

    static class AcrylicCompat
    {
        const int WcaAccentPolicy = 19;
        const int AccentDisabled = 0;
        const int AccentEnableAcrylicBlurBehind = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy
        {
            public int State;
            public int Flags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WcaData
        {
            public int Attribute;
            public IntPtr Data;
            public int Size;
        }

        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WcaData data);

        public static void Apply(IntPtr hwnd, bool dark, bool enabled)
        {
            if (hwnd == IntPtr.Zero)
                return;

            var policy = new AccentPolicy
            {
                State = enabled ? AccentEnableAcrylicBlurBehind : AccentDisabled,
                Flags = enabled ? 2 : 0,
                GradientColor = enabled
                    ? (dark ? unchecked((int)0xCC202020) : unchecked((int)0xD9F7F7F7))
                    : 0,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, mem, false);
                var data = new WcaData
                {
                    Attribute = WcaAccentPolicy,
                    Data = mem,
                    Size = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }
    }

    sealed class ShellController
    {
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_SYSCOLORCHANGE = 0x0015;
        const int WM_THEMECHANGED = 0x031A;
        const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

        readonly TouchKeyboardAudioFloat.MainWindow window;
        readonly Border frame;
        readonly TextBlock captionTitle;
        readonly Button minimizeButton;
        readonly Button closeButton;
        readonly TextBlock pageTitle;
        readonly TextBlock subtitle;
        readonly Slider slider;
        readonly TextBlock dbText;
        readonly TextBlock gainText;
        readonly Border infoSeparator;
        readonly TextBlock[] infoTexts;
        readonly TextBlock footerNote;
        readonly Button restoreButton;
        readonly Button applyButton;

        HwndSource source;
        bool refreshQueued;
        ThemeSnapshot theme;

        public ShellController(
            TouchKeyboardAudioFloat.MainWindow window,
            Border frame,
            TextBlock captionTitle,
            Button minimizeButton,
            Button closeButton,
            TextBlock pageTitle,
            TextBlock subtitle,
            Slider slider,
            TextBlock dbText,
            TextBlock gainText,
            Border infoSeparator,
            TextBlock[] infoTexts,
            TextBlock footerNote,
            Button restoreButton,
            Button applyButton)
        {
            this.window = window;
            this.frame = frame;
            this.captionTitle = captionTitle;
            this.minimizeButton = minimizeButton;
            this.closeButton = closeButton;
            this.pageTitle = pageTitle;
            this.subtitle = subtitle;
            this.slider = slider;
            this.dbText = dbText;
            this.gainText = gainText;
            this.infoSeparator = infoSeparator;
            this.infoTexts = infoTexts ?? new TextBlock[0];
            this.footerNote = footerNote;
            this.restoreButton = restoreButton;
            this.applyButton = applyButton;
        }

        public void Initialize()
        {
            RefreshTheme(false);

            window.SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                source = HwndSource.FromHwnd(hwnd);
                if (source != null)
                    source.AddHook(WndProc);

                RefreshTheme(true);
            };

            window.Closed += delegate
            {
                if (source != null)
                {
                    try { source.RemoveHook(WndProc); }
                    catch { }
                    source = null;
                }
            };
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETTINGCHANGE ||
                msg == WM_SYSCOLORCHANGE ||
                msg == WM_THEMECHANGED ||
                msg == WM_DWMCOLORIZATIONCOLORCHANGED ||
                msg == WM_DWMCOMPOSITIONCHANGED)
            {
                QueueRefresh();
            }

            return IntPtr.Zero;
        }

        void QueueRefresh()
        {
            if (refreshQueued)
                return;

            refreshQueued = true;
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(delegate
                {
                    refreshQueued = false;
                    RefreshTheme(true);
                }));
        }

        void RefreshTheme(bool updateComposition)
        {
            theme = SystemTheme.Read();
            bool dark = theme.Dark;

            Color accent = theme.Accent;
            Color accentDark = theme.AccentDark1;
            Color accentLight = theme.AccentLight1;
            string accentHex = Hex(accent);

            window.Background = Brush(
                dark
                    ? (theme.AdvancedEffectsEnabled ? "#CC202020" : "#FF202020")
                    : (theme.AdvancedEffectsEnabled ? "#D9F7F7F7" : "#FFF7F7F7"));
            window.Foreground = Brush(dark ? "#FFF2F2F2" : "#FF1A1A1A");

            frame.BorderBrush = Brush(dark ? "#334A4A4A" : "#22000000");
            captionTitle.Foreground = Brush(dark ? "#FFE6E6E6" : "#FF333333");
            minimizeButton.Style = UwpShell.CaptionButtonStyle(false, dark);
            closeButton.Style = UwpShell.CaptionButtonStyle(true, dark);

            pageTitle.Foreground = Brush(dark ? "#FFF6F6F6" : "#FF171717");
            subtitle.Foreground = Brush(dark ? "#FFB9B9B9" : "#FF666666");

            dbText.Foreground = new SolidColorBrush(accent);
            gainText.Foreground = Brush(dark ? "#FFB7B7B7" : "#FF606060");

            slider.Style = (Style)XamlReader.Parse(
                UwpShell.SliderStyle(dark, accent, accentDark, accentLight));

            if (infoSeparator != null)
                infoSeparator.BorderBrush = Brush(dark ? "#35FFFFFF" : "#1E000000");

            for (int i = 0; i < infoTexts.Length; i++)
            {
                TextBlock text = infoTexts[i];
                if (i == 0)
                    text.Foreground = new SolidColorBrush(accent);
                else
                    text.Foreground = Brush(dark ? "#FFB7B7B7" : "#FF666666");
            }

            if (footerNote != null)
            {
                footerNote.Text =
                    "Windows 10 · System theme · " +
                    (theme.FromUiSettings ? "UISettings" : "DWM fallback") +
                    " · IEEE Float32";
                footerNote.Foreground = Brush(dark ? "#FF9D9D9D" : "#FF777777");
            }

            if (restoreButton != null)
                UwpShell.StyleActionButton(
                    restoreButton, false, dark, accent, accentDark, accentLight);
            if (applyButton != null)
                UwpShell.StyleActionButton(
                    applyButton, true, dark, accent, accentDark, accentLight);

            if (updateComposition)
            {
                try
                {
                    AcrylicCompat.Apply(
                        new WindowInteropHelper(window).Handle,
                        dark,
                        theme.AdvancedEffectsEnabled);
                }
                catch { }
            }
        }

        static Brush Brush(string value)
        {
            return (Brush)new BrushConverter().ConvertFromString(value);
        }

        static string Hex(Color value)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", value.A, value.R, value.G, value.B);
        }
    }

    static class UwpShell
    {
        public static void Apply(TouchKeyboardAudioFloat.MainWindow window)
        {
            window.Width = 700;
            window.Height = 510;
            window.MinWidth = 700;
            window.MinHeight = 510;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowStyle = WindowStyle.None;
            window.FontFamily = new FontFamily("Segoe UI");

            Grid content = window.Content as Grid;
            if (content == null)
                return;

            window.Content = null;

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent
            };

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            frame.Child = outer;

            TextBlock captionTitle;
            Button minimizeButton;
            Button closeButton;
            Grid titleBar = MakeTitleBar(
                window,
                out captionTitle,
                out minimizeButton,
                out closeButton);
            outer.Children.Add(titleBar);

            content.Margin = new Thickness(40, 25, 40, 30);
            Grid.SetRow(content, 1);
            outer.Children.Add(content);

            TextBlock pageTitle;
            TextBlock subtitle;
            Slider slider;
            TextBlock dbText;
            TextBlock gainText;
            Border infoSeparator;
            TextBlock[] infoTexts;
            TextBlock footerNote;
            Button restoreButton;
            Button applyButton;

            RestyleContent(
                content,
                out pageTitle,
                out subtitle,
                out slider,
                out dbText,
                out gainText,
                out infoSeparator,
                out infoTexts,
                out footerNote,
                out restoreButton,
                out applyButton);

            window.Content = frame;

            var controller = new ShellController(
                window,
                frame,
                captionTitle,
                minimizeButton,
                closeButton,
                pageTitle,
                subtitle,
                slider,
                dbText,
                gainText,
                infoSeparator,
                infoTexts,
                footerNote,
                restoreButton,
                applyButton);
            controller.Initialize();
        }

        static Grid MakeTitleBar(
            Window window,
            out TextBlock title,
            out Button minimize,
            out Button close)
        {
            var bar = new Grid
            {
                Height = 32,
                Background = Brushes.Transparent
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { window.DragMove(); }
                    catch { }
                }
            };

            title = new TextBlock
            {
                Text = "Touch Keyboard Audio",
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            bar.Children.Add(title);

            var captions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(captions, 1);
            bar.Children.Add(captions);

            minimize = CaptionButton("\uE921");
            minimize.ToolTip = "最小化";
            minimize.Click += delegate { window.WindowState = WindowState.Minimized; };
            captions.Children.Add(minimize);

            close = CaptionButton("\uE8BB");
            close.ToolTip = "关闭";
            close.Click += delegate { window.Close(); };
            captions.Children.Add(close);

            return bar;
        }

        static Button CaptionButton(string glyph)
        {
            return new Button
            {
                Width = 46,
                Height = 32,
                Content = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                BorderThickness = new Thickness(0),
                Focusable = false,
                Background = Brushes.Transparent
            };
        }

        internal static Style CaptionButtonStyle(bool close, bool dark)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(
                Control.ForegroundProperty,
                Brush(dark ? "#FFF0F0F0" : "#FF202020")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>" +
                "<Border x:Name='B' Background='{TemplateBinding Background}'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
                "</Border></ControlTemplate>")));

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(
                Control.BackgroundProperty,
                Brush(close ? "#FFE81123" : (dark ? "#30FFFFFF" : "#14000000"))));
            if (close)
                hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(
                Control.BackgroundProperty,
                Brush(close ? "#FFC50F1F" : (dark ? "#4AFFFFFF" : "#24000000"))));
            if (close)
                pressed.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(pressed);

            return style;
        }

        static void RestyleContent(
            Grid root,
            out TextBlock pageTitle,
            out TextBlock subtitle,
            out Slider slider,
            out TextBlock dbText,
            out TextBlock gainText,
            out Border infoSeparator,
            out TextBlock[] infoTexts,
            out TextBlock footerNote,
            out Button restoreButton,
            out Button applyButton)
        {
            pageTitle = null;
            subtitle = null;
            slider = null;
            dbText = null;
            gainText = null;
            infoSeparator = null;
            infoTexts = new TextBlock[0];
            footerNote = null;
            restoreButton = null;
            applyButton = null;

            if (root.Children.Count < 3)
                return;

            StackPanel head = root.Children[0] as StackPanel;
            Border card = root.Children[1] as Border;
            Grid bottom = root.Children[2] as Grid;

            if (head != null && head.Children.Count >= 2)
            {
                pageTitle = head.Children[0] as TextBlock;
                subtitle = head.Children[1] as TextBlock;

                if (pageTitle != null)
                {
                    pageTitle.FontSize = 32;
                    pageTitle.FontWeight = FontWeights.Light;
                }

                if (subtitle != null)
                {
                    subtitle.Text = "调整触摸键盘按键反馈音";
                    subtitle.FontSize = 12;
                    subtitle.Margin = new Thickness(1, 5, 0, 0);
                }
            }

            if (card != null)
            {
                card.Background = Brushes.Transparent;
                card.BorderThickness = new Thickness(0);
                card.CornerRadius = new CornerRadius(0);
                card.Padding = new Thickness(0);

                Grid panel = card.Child as Grid;
                if (panel != null)
                {
                    if (panel.RowDefinitions.Count > 1) panel.RowDefinitions[1].Height = new GridLength(18);
                    if (panel.RowDefinitions.Count > 3) panel.RowDefinitions[3].Height = new GridLength(7);
                    if (panel.RowDefinitions.Count > 5) panel.RowDefinitions[5].Height = new GridLength(22);

                    slider = panel.Children.OfType<Slider>().FirstOrDefault();
                    if (slider != null)
                        slider.Height = 36;

                    Grid values = panel.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetRow(g) == 0);
                    if (values != null)
                    {
                        TextBlock[] texts = values.Children.OfType<TextBlock>().ToArray();
                        if (texts.Length > 0)
                        {
                            dbText = texts[0];
                            dbText.FontSize = 44;
                            dbText.FontWeight = FontWeights.Light;
                        }
                        if (texts.Length > 1)
                        {
                            gainText = texts[1];
                            gainText.FontSize = 14;
                        }
                    }

                    Grid info = panel.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetRow(g) == 6);
                    if (info != null)
                    {
                        panel.Children.Remove(info);
                        infoSeparator = new Border
                        {
                            BorderThickness = new Thickness(0, 1, 0, 0),
                            Padding = new Thickness(0, 14, 0, 0),
                            Child = info
                        };
                        Grid.SetRow(infoSeparator, 6);
                        panel.Children.Add(infoSeparator);

                        infoTexts = Descendants<TextBlock>(info);
                        foreach (TextBlock text in infoTexts)
                            if (text.FontSize >= 13) text.FontSize = 12;
                    }
                }
            }

            if (bottom != null)
            {
                footerNote = bottom.Children.OfType<TextBlock>().FirstOrDefault();
                if (footerNote != null)
                    footerNote.FontSize = 11;

                restoreButton = bottom.Children.OfType<Button>().FirstOrDefault(
                    b => Grid.GetColumn(b) == 1);
                applyButton = bottom.Children.OfType<Button>().FirstOrDefault(
                    b => Grid.GetColumn(b) == 3);
            }
        }

        internal static void StyleActionButton(
            Button button,
            bool primary,
            bool dark,
            Color accent,
            Color accentDark,
            Color accentLight)
        {
            button.Height = 32;
            button.MinWidth = primary ? 88 : 102;
            button.Padding = new Thickness(16, 0, 16, 0);
            button.FontSize = 13;
            button.FontWeight = FontWeights.Normal;
            button.BorderThickness = primary ? new Thickness(0) : new Thickness(1);
            button.Style = (Style)XamlReader.Parse(
                ButtonStyle(primary, dark, accent, accentDark, accentLight));
        }

        internal static string SliderStyle(
            bool dark,
            Color accent,
            Color accentDark,
            Color accentLight)
        {
            string rail = dark ? "#FF6B6B6B" : "#FF8A8A8A";
            string thumb = dark ? "#FFFFFFFF" : "#FF171717";
            string accentHex = Hex(accent);

            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                     xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                     TargetType='{x:Type Slider}'>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
<Grid Height='36'>
  <Track x:Name='PART_Track' VerticalAlignment='Center'>
    <Track.DecreaseRepeatButton>
      <RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>
        <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'>
          <Border Height='2' Background='" + accentHex + @"'/>
        </ControlTemplate></RepeatButton.Template>
      </RepeatButton>
    </Track.DecreaseRepeatButton>
    <Track.Thumb>
      <Thumb Width='10' Height='24'>
        <Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
          <Border Width='10' Height='24' CornerRadius='5' Background='" + thumb + @"' BorderBrush='" + accentHex + @"' BorderThickness='2'/>
        </ControlTemplate></Thumb.Template>
      </Thumb>
    </Track.Thumb>
    <Track.IncreaseRepeatButton>
      <RepeatButton Command='Slider.IncreaseLarge' Focusable='False'>
        <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'>
          <Border Height='2' Background='" + rail + @"'/>
        </ControlTemplate></RepeatButton.Template>
      </RepeatButton>
    </Track.IncreaseRepeatButton>
  </Track>
</Grid>
</ControlTemplate></Setter.Value></Setter>
</Style>";
        }

        static string ButtonStyle(
            bool primary,
            bool dark,
            Color accent,
            Color accentDark,
            Color accentLight)
        {
            string bg = primary ? Hex(accent) : (dark ? "#18FFFFFF" : "#0A000000");
            string fg = primary
                ? (SystemTheme.UseDarkText(accent) ? "#FF000000" : "#FFFFFFFF")
                : (dark ? "#FFF2F2F2" : "#FF1A1A1A");
            string border = dark ? "#55FFFFFF" : "#55000000";
            string hover = primary
                ? Hex(dark ? accentLight : accentDark)
                : (dark ? "#2AFFFFFF" : "#14000000");
            string pressed = primary
                ? Hex(accentDark)
                : (dark ? "#3AFFFFFF" : "#22000000");

            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                     xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                     TargetType='{x:Type Button}'>
<Setter Property='Background' Value='" + bg + @"'/>
<Setter Property='Foreground' Value='" + fg + @"'/>
<Setter Property='BorderBrush' Value='" + border + @"'/>
<Setter Property='BorderThickness' Value='" + (primary ? "0" : "1") + @"'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
<Border x:Name='B' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='2'>
  <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
</Border>
<ControlTemplate.Triggers>
  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='" + hover + @"'/></Trigger>
  <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='" + pressed + @"'/></Trigger>
  <Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='.45'/></Trigger>
</ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter>
</Style>";
        }

        static T[] Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            var list = new System.Collections.Generic.List<T>();
            Walk(root, list);
            return list.ToArray();
        }

        static void Walk<T>(DependencyObject root, System.Collections.Generic.List<T> list)
            where T : DependencyObject
        {
            if (root == null) return;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T typed = child as T;
                if (typed != null) list.Add(typed);
                Walk(child, list);
            }
        }

        static Brush Brush(string value)
        {
            return (Brush)new BrushConverter().ConvertFromString(value);
        }

        static string Hex(Color value)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", value.A, value.R, value.G, value.B);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var window = new TouchKeyboardAudioFloat.MainWindow();
                UwpShell.Apply(window);
                app.Run(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Touch Keyboard Audio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
