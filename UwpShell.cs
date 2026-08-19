using System;
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
            ThemeSnapshot snapshot;
            if (TryReadUiSettings(out snapshot))
                return snapshot;

            Color accent = ReadDwmAccent();
            bool dark = ReadRegistryDarkFallback();

            return new ThemeSnapshot
            {
                Dark = dark,
                AdvancedEffectsEnabled = !SystemParameters.HighContrast,
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
                    AdvancedEffectsEnabled = advancedEffects && !SystemParameters.HighContrast,
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

    sealed class ShellElements
    {
        public Slider Slider;
        public TextBlock DbText;
        public TextBlock GainText;
        public TextBlock PeakText;
        public TextBlock MixText;
        public TextBlock StatusText;
        public Button ApplyButton;
        public Button RestoreButton;

        public TextBlock CaptionTitle;
        public Button MinimizeButton;
        public Button CloseButton;
        public TextBlock PageTitle;
        public TextBlock Subtitle;
        public Border InfoSeparator;
        public TextBlock FormatLabel;
        public TextBlock FooterNote;

        public static ShellElements Capture(TouchKeyboardAudioFloat.MainWindow window)
        {
            var elements = new ShellElements
            {
                Slider = ReadField<Slider>(window, "slider"),
                DbText = ReadField<TextBlock>(window, "dbText"),
                GainText = ReadField<TextBlock>(window, "gainText"),
                PeakText = ReadField<TextBlock>(window, "peakText"),
                MixText = ReadField<TextBlock>(window, "mixText"),
                StatusText = ReadField<TextBlock>(window, "statusText"),
                ApplyButton = ReadField<Button>(window, "applyButton"),
                RestoreButton = ReadField<Button>(window, "restoreButton")
            };

            Detach(elements.Slider);
            Detach(elements.DbText);
            Detach(elements.GainText);
            Detach(elements.PeakText);
            Detach(elements.MixText);
            Detach(elements.StatusText);
            Detach(elements.ApplyButton);
            Detach(elements.RestoreButton);

            return elements;
        }

        static T ReadField<T>(object owner, string name) where T : class
        {
            FieldInfo field = owner.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
                throw new MissingFieldException(owner.GetType().FullName, name);

            T value = field.GetValue(owner) as T;
            if (value == null)
                throw new InvalidOperationException("Frontend field is unavailable: " + name);

            return value;
        }

        static void Detach(FrameworkElement element)
        {
            if (element == null)
                return;

            DependencyObject parent = element.Parent;
            Panel panel = parent as Panel;
            if (panel != null)
            {
                panel.Children.Remove(element);
                return;
            }

            Decorator decorator = parent as Decorator;
            if (decorator != null && decorator.Child == element)
            {
                decorator.Child = null;
                return;
            }

            ContentControl content = parent as ContentControl;
            if (content != null && content.Content == element)
                content.Content = null;
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
        readonly ShellElements elements;

        HwndSource source;
        bool refreshQueued;
        ThemeSnapshot theme;

        public ShellController(
            TouchKeyboardAudioFloat.MainWindow window,
            Border frame,
            ShellElements elements)
        {
            this.window = window;
            this.frame = frame;
            this.elements = elements;
        }

        public void Initialize()
        {
            elements.Slider.ValueChanged += delegate { ReapplyDynamicAccent(); };
            elements.ApplyButton.Click += delegate { ReapplyDynamicAccent(); };
            elements.RestoreButton.Click += delegate { ReapplyDynamicAccent(); };

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

        IntPtr WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
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

            window.Background = Brush(
                dark
                    ? (theme.AdvancedEffectsEnabled ? "#CC202020" : "#FF202020")
                    : (theme.AdvancedEffectsEnabled ? "#D9F7F7F7" : "#FFF7F7F7"));
            window.Foreground = Brush(dark ? "#FFF2F2F2" : "#FF1A1A1A");

            frame.BorderBrush = Brush(dark ? "#334A4A4A" : "#22000000");

            elements.CaptionTitle.Foreground = Brush(dark ? "#FFE6E6E6" : "#FF333333");
            elements.MinimizeButton.Style = UwpShell.CaptionButtonStyle(false, dark);
            elements.CloseButton.Style = UwpShell.CaptionButtonStyle(true, dark);

            elements.PageTitle.Foreground = Brush(dark ? "#FFF6F6F6" : "#FF171717");
            elements.Subtitle.Foreground = Brush(dark ? "#FFB9B9B9" : "#FF666666");
            elements.DbText.Foreground = new SolidColorBrush(accent);
            elements.GainText.Foreground = Brush(dark ? "#FFB7B7B7" : "#FF606060");

            elements.Slider.Style = (Style)XamlReader.Parse(
                UwpShell.SliderStyle(dark, accent));

            elements.InfoSeparator.BorderBrush =
                Brush(dark ? "#35FFFFFF" : "#1E000000");

            elements.PeakText.Foreground = new SolidColorBrush(accent);
            elements.MixText.Foreground = Brush(dark ? "#FFB7B7B7" : "#FF666666");
            elements.StatusText.Foreground = Brush(dark ? "#FFB7B7B7" : "#FF666666");
            elements.FormatLabel.Foreground = Brush(dark ? "#FF9D9D9D" : "#FF777777");

            elements.FooterNote.Text =
                "Windows 10 · System theme · " +
                (theme.FromUiSettings ? "UISettings" : "DWM fallback") +
                " · IEEE Float32";
            elements.FooterNote.Foreground =
                Brush(dark ? "#FF9D9D9D" : "#FF777777");

            UwpShell.StyleActionButton(
                elements.RestoreButton,
                false,
                dark,
                theme.Accent,
                theme.AccentDark1,
                theme.AccentLight1);
            UwpShell.StyleActionButton(
                elements.ApplyButton,
                true,
                dark,
                theme.Accent,
                theme.AccentDark1,
                theme.AccentLight1);

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

        void ReapplyDynamicAccent()
        {
            if (theme == null)
                return;

            elements.DbText.Foreground = new SolidColorBrush(theme.Accent);
            elements.PeakText.Foreground = new SolidColorBrush(theme.Accent);
        }

        static Brush Brush(string value)
        {
            return (Brush)new BrushConverter().ConvertFromString(value);
        }
    }

    static class UwpShell
    {
        public static void Apply(TouchKeyboardAudioFloat.MainWindow window)
        {
            ShellElements elements = ShellElements.Capture(window);

            window.Content = null;
            window.Width = 700;
            window.Height = 510;
            window.MinWidth = 700;
            window.MinHeight = 510;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowStyle = WindowStyle.None;
            window.FontFamily = new FontFamily("Segoe UI");

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent
            };

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            outer.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            frame.Child = outer;

            Grid titleBar = MakeTitleBar(window, elements);
            outer.Children.Add(titleBar);

            Grid page = MakePage(elements);
            Grid.SetRow(page, 1);
            outer.Children.Add(page);

            window.Content = frame;

            var controller = new ShellController(window, frame, elements);
            controller.Initialize();
        }

        static Grid MakeTitleBar(
            Window window,
            ShellElements elements)
        {
            var bar = new Grid
            {
                Height = 32,
                Background = Brushes.Transparent
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            var dragRegion = new Border
            {
                Background = Brushes.Transparent
            };
            dragRegion.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { window.DragMove(); }
                    catch { }
                }
            };
            bar.Children.Add(dragRegion);

            elements.CaptionTitle = new TextBlock
            {
                Text = "Touch Keyboard Audio",
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                IsHitTestVisible = false
            };
            bar.Children.Add(elements.CaptionTitle);

            var captions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(captions, 1);
            bar.Children.Add(captions);

            elements.MinimizeButton = CaptionButton("\uE921");
            elements.MinimizeButton.ToolTip = "最小化";
            elements.MinimizeButton.Click += delegate
            {
                window.WindowState = WindowState.Minimized;
            };
            captions.Children.Add(elements.MinimizeButton);

            elements.CloseButton = CaptionButton("\uE8BB");
            elements.CloseButton.ToolTip = "关闭";
            elements.CloseButton.Click += delegate { window.Close(); };
            captions.Children.Add(elements.CloseButton);

            return bar;
        }

        static Grid MakePage(ShellElements elements)
        {
            var page = new Grid
            {
                Margin = new Thickness(40, 25, 40, 30)
            };

            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel();
            elements.PageTitle = new TextBlock
            {
                Text = "触摸键盘音量",
                FontSize = 32,
                FontWeight = FontWeights.Light
            };
            elements.Subtitle = new TextBlock
            {
                Text = "调整触摸键盘按键反馈音",
                FontSize = 12,
                Margin = new Thickness(1, 5, 0, 0)
            };
            header.Children.Add(elements.PageTitle);
            header.Children.Add(elements.Subtitle);
            page.Children.Add(header);

            var values = new Grid();
            Grid.SetRow(values, 2);
            page.Children.Add(values);

            elements.DbText.FontSize = 44;
            elements.DbText.FontWeight = FontWeights.Light;
            elements.DbText.Margin = new Thickness(0);
            elements.DbText.VerticalAlignment = VerticalAlignment.Center;
            values.Children.Add(elements.DbText);

            elements.GainText.FontSize = 14;
            elements.GainText.Margin = new Thickness(0);
            elements.GainText.HorizontalAlignment = HorizontalAlignment.Right;
            elements.GainText.VerticalAlignment = VerticalAlignment.Center;
            values.Children.Add(elements.GainText);

            elements.Slider.Height = 36;
            elements.Slider.Margin = new Thickness(0);
            Grid.SetRow(elements.Slider, 4);
            page.Children.Add(elements.Slider);

            var scale = new Grid();
            scale.Children.Add(
                ScaleLabel("-20 dB", HorizontalAlignment.Left));
            scale.Children.Add(
                ScaleLabel("0 dB", HorizontalAlignment.Center));
            scale.Children.Add(
                ScaleLabel("+30 dB", HorizontalAlignment.Right));
            Grid.SetRow(scale, 5);
            page.Children.Add(scale);

            var info = new Grid();
            info.ColumnDefinitions.Add(new ColumnDefinition());
            info.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            var infoLeft = new StackPanel();
            elements.PeakText.FontSize = 12;
            elements.PeakText.Margin = new Thickness(0);
            elements.MixText.FontSize = 12;
            elements.MixText.Margin = new Thickness(0, 5, 0, 0);
            elements.StatusText.FontSize = 12;
            elements.StatusText.Margin = new Thickness(0, 5, 0, 0);
            infoLeft.Children.Add(elements.PeakText);
            infoLeft.Children.Add(elements.MixText);
            infoLeft.Children.Add(elements.StatusText);
            info.Children.Add(infoLeft);

            elements.FormatLabel = new TextBlock
            {
                Text = "5 个 WAV · IEEE Float32",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(20, 0, 0, 0)
            };
            Grid.SetColumn(elements.FormatLabel, 1);
            info.Children.Add(elements.FormatLabel);

            elements.InfoSeparator = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 14, 0, 0),
                Child = info
            };
            Grid.SetRow(elements.InfoSeparator, 7);
            page.Children.Add(elements.InfoSeparator);

            var footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(12) });
            footer.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(footer, 9);
            page.Children.Add(footer);

            elements.FooterNote = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Children.Add(elements.FooterNote);

            elements.RestoreButton.Height = 32;
            elements.RestoreButton.MinWidth = 102;
            elements.RestoreButton.Padding = new Thickness(16, 0, 16, 0);
            elements.RestoreButton.FontSize = 13;
            elements.RestoreButton.FontWeight = FontWeights.Normal;
            Grid.SetColumn(elements.RestoreButton, 1);
            footer.Children.Add(elements.RestoreButton);

            elements.ApplyButton.Height = 32;
            elements.ApplyButton.MinWidth = 88;
            elements.ApplyButton.Padding = new Thickness(16, 0, 16, 0);
            elements.ApplyButton.FontSize = 13;
            elements.ApplyButton.FontWeight = FontWeights.Normal;
            Grid.SetColumn(elements.ApplyButton, 3);
            footer.Children.Add(elements.ApplyButton);

            return page;
        }

        static TextBlock ScaleLabel(
            string text,
            HorizontalAlignment alignment)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = alignment,
                Foreground = Brush("#FF777777")
            };
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
            style.Setters.Add(
                new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(
                new Setter(
                    Control.ForegroundProperty,
                    Brush(dark ? "#FFF0F0F0" : "#FF202020")));
            style.Setters.Add(
                new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(
                new Setter(
                    Control.TemplateProperty,
                    (ControlTemplate)XamlReader.Parse(
                        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>" +
                        "<Border x:Name='B' Background='{TemplateBinding Background}'>" +
                        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
                        "</Border></ControlTemplate>")));

            var hover = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            hover.Setters.Add(
                new Setter(
                    Control.BackgroundProperty,
                    Brush(
                        close
                            ? "#FFE81123"
                            : (dark ? "#30FFFFFF" : "#14000000"))));
            if (close)
                hover.Setters.Add(
                    new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hover);

            var pressed = new Trigger
            {
                Property = ButtonBase.IsPressedProperty,
                Value = true
            };
            pressed.Setters.Add(
                new Setter(
                    Control.BackgroundProperty,
                    Brush(
                        close
                            ? "#FFC50F1F"
                            : (dark ? "#4AFFFFFF" : "#24000000"))));
            if (close)
                pressed.Setters.Add(
                    new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(pressed);

            return style;
        }

        internal static void StyleActionButton(
            Button button,
            bool primary,
            bool dark,
            Color accent,
            Color accentDark,
            Color accentLight)
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            button.ClearValue(Control.BorderBrushProperty);
            button.ClearValue(Control.BorderThicknessProperty);
            button.Style = (Style)XamlReader.Parse(
                ButtonStyle(
                    primary,
                    dark,
                    accent,
                    accentDark,
                    accentLight));
        }

        internal static string SliderStyle(
            bool dark,
            Color accent)
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
            string background =
                primary ? Hex(accent) : (dark ? "#18FFFFFF" : "#0A000000");
            string foreground =
                primary
                    ? (SystemTheme.UseDarkText(accent) ? "#FF000000" : "#FFFFFFFF")
                    : (dark ? "#FFF2F2F2" : "#FF1A1A1A");
            string border = dark ? "#55FFFFFF" : "#55000000";
            string hover =
                primary
                    ? Hex(dark ? accentLight : accentDark)
                    : (dark ? "#2AFFFFFF" : "#14000000");
            string pressed =
                primary
                    ? Hex(accentDark)
                    : (dark ? "#3AFFFFFF" : "#22000000");

            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                     xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                     TargetType='{x:Type Button}'>
<Setter Property='Background' Value='" + background + @"'/>
<Setter Property='Foreground' Value='" + foreground + @"'/>
<Setter Property='BorderBrush' Value='" + border + @"'/>
<Setter Property='BorderThickness' Value='" + (primary ? "0" : "1") + @"'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
<Border x:Name='B'
        Background='{TemplateBinding Background}'
        BorderBrush='{TemplateBinding BorderBrush}'
        BorderThickness='{TemplateBinding BorderThickness}'
        CornerRadius='2'>
  <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
</Border>
<ControlTemplate.Triggers>
  <Trigger Property='IsMouseOver' Value='True'>
    <Setter TargetName='B' Property='Background' Value='" + hover + @"'/>
  </Trigger>
  <Trigger Property='IsPressed' Value='True'>
    <Setter TargetName='B' Property='Background' Value='" + pressed + @"'/>
  </Trigger>
  <Trigger Property='IsEnabled' Value='False'>
    <Setter TargetName='B' Property='Opacity' Value='.45'/>
  </Trigger>
</ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter>
</Style>";
        }

        static Brush Brush(string value)
        {
            return (Brush)new BrushConverter().ConvertFromString(value);
        }

        static string Hex(Color value)
        {
            return string.Format(
                "#{0:X2}{1:X2}{2:X2}{3:X2}",
                value.A,
                value.R,
                value.G,
                value.B);
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
