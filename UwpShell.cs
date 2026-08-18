using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace TouchKeyboardAudioUwp
{
    static class UwpShell
    {
        const string Accent = "#FF0078D7";

        public static void Apply(TouchKeyboardAudioFloat.MainWindow window)
        {
            bool dark = IsDarkMode();

            window.Width = 700;
            window.Height = 510;
            window.MinWidth = 700;
            window.MinHeight = 510;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowStyle = WindowStyle.None;
            window.FontFamily = new FontFamily("Segoe UI");
            window.Background = Brush(dark ? "#CC202020" : "#D9F7F7F7");
            window.Foreground = Brush(dark ? "#FFF2F2F2" : "#FF1A1A1A");

            Grid content = window.Content as Grid;
            if (content == null)
                return;

            window.Content = null;

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = Brush(dark ? "#334A4A4A" : "#22000000"),
                Background = Brushes.Transparent
            };

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            frame.Child = outer;

            Grid titleBar = MakeTitleBar(window, dark);
            outer.Children.Add(titleBar);

            content.Margin = new Thickness(40, 25, 40, 30);
            Grid.SetRow(content, 1);
            outer.Children.Add(content);

            RestyleContent(content, dark);
            window.Content = frame;
        }

        static Grid MakeTitleBar(Window window, bool dark)
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

            var title = new TextBlock
            {
                Text = "Touch Keyboard Audio",
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                Foreground = Brush(dark ? "#FFE6E6E6" : "#FF333333")
            };
            bar.Children.Add(title);

            var captions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(captions, 1);
            bar.Children.Add(captions);

            Button minimize = CaptionButton("\uE921", false, dark);
            minimize.ToolTip = "最小化";
            minimize.Click += delegate { window.WindowState = WindowState.Minimized; };
            captions.Children.Add(minimize);

            Button close = CaptionButton("\uE8BB", true, dark);
            close.ToolTip = "关闭";
            close.Click += delegate { window.Close(); };
            captions.Children.Add(close);

            return bar;
        }

        static Button CaptionButton(string glyph, bool close, bool dark)
        {
            var button = new Button
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
                Foreground = Brush(dark ? "#FFF0F0F0" : "#FF202020"),
                Background = Brushes.Transparent
            };

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>" +
                "<Border Background='{TemplateBinding Background}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "</ControlTemplate>")));

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty,
                Brush(close ? "#FFE81123" : (dark ? "#30FFFFFF" : "#14000000"))));
            if (close)
                hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty,
                Brush(close ? "#FFC50F1F" : (dark ? "#4AFFFFFF" : "#24000000"))));
            if (close)
                pressed.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(pressed);

            button.Style = style;
            return button;
        }

        static void RestyleContent(Grid root, bool dark)
        {
            if (root.Children.Count < 3)
                return;

            StackPanel head = root.Children[0] as StackPanel;
            Border card = root.Children[1] as Border;
            Grid bottom = root.Children[2] as Grid;

            if (head != null && head.Children.Count >= 2)
            {
                TextBlock title = head.Children[0] as TextBlock;
                TextBlock subtitle = head.Children[1] as TextBlock;

                if (title != null)
                {
                    title.FontSize = 32;
                    title.FontWeight = FontWeights.Light;
                    title.Foreground = Brush(dark ? "#FFF6F6F6" : "#FF171717");
                }

                if (subtitle != null)
                {
                    subtitle.Text = "调整触摸键盘按键反馈音";
                    subtitle.FontSize = 12;
                    subtitle.Margin = new Thickness(1, 5, 0, 0);
                    subtitle.Foreground = Brush(dark ? "#FFB9B9B9" : "#FF666666");
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

                    Slider slider = panel.Children.OfType<Slider>().FirstOrDefault();
                    if (slider != null)
                    {
                        slider.Height = 36;
                        slider.Style = (Style)XamlReader.Parse(SliderStyle(dark));
                    }

                    Grid values = panel.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetRow(g) == 0);
                    if (values != null)
                    {
                        TextBlock[] texts = values.Children.OfType<TextBlock>().ToArray();
                        if (texts.Length > 0)
                        {
                            texts[0].FontSize = 44;
                            texts[0].FontWeight = FontWeights.Light;
                            texts[0].Foreground = Brush(Accent);
                        }
                        if (texts.Length > 1)
                        {
                            texts[1].FontSize = 14;
                            texts[1].Foreground = Brush(dark ? "#FFB7B7B7" : "#FF606060");
                        }
                    }

                    Grid info = panel.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetRow(g) == 6);
                    if (info != null)
                    {
                        panel.Children.Remove(info);
                        var separator = new Border
                        {
                            BorderThickness = new Thickness(0, 1, 0, 0),
                            BorderBrush = Brush(dark ? "#35FFFFFF" : "#1E000000"),
                            Padding = new Thickness(0, 14, 0, 0),
                            Child = info
                        };
                        Grid.SetRow(separator, 6);
                        panel.Children.Add(separator);

                        foreach (TextBlock text in Descendants<TextBlock>(info))
                            if (text.FontSize >= 13) text.FontSize = 12;
                    }
                }
            }

            if (bottom != null)
            {
                TextBlock note = bottom.Children.OfType<TextBlock>().FirstOrDefault();
                if (note != null)
                {
                    note.Text = "Windows 10 · TextInputHost · IEEE Float32";
                    note.FontSize = 11;
                    note.Foreground = Brush(dark ? "#FF9D9D9D" : "#FF777777");
                }

                Button restore = bottom.Children.OfType<Button>().FirstOrDefault(b => Grid.GetColumn(b) == 1);
                Button apply = bottom.Children.OfType<Button>().FirstOrDefault(b => Grid.GetColumn(b) == 3);
                if (restore != null) StyleActionButton(restore, false, dark);
                if (apply != null) StyleActionButton(apply, true, dark);
            }
        }

        static void StyleActionButton(Button button, bool primary, bool dark)
        {
            button.Height = 32;
            button.MinWidth = primary ? 88 : 102;
            button.Padding = new Thickness(16, 0, 16, 0);
            button.FontSize = 13;
            button.FontWeight = FontWeights.Normal;
            button.BorderThickness = primary ? new Thickness(0) : new Thickness(1);
            button.BorderBrush = Brush(dark ? "#55FFFFFF" : "#55000000");
            button.Background = Brush(primary ? Accent : (dark ? "#18FFFFFF" : "#0A000000"));
            button.Foreground = Brush(primary ? "#FFFFFFFF" : (dark ? "#FFF2F2F2" : "#FF1A1A1A"));
            button.Style = (Style)XamlReader.Parse(ButtonStyle(primary, dark));
        }

        static string SliderStyle(bool dark)
        {
            string rail = dark ? "#FF6B6B6B" : "#FF8A8A8A";
            string thumb = dark ? "#FFFFFFFF" : "#FF171717";

            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                     xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                     TargetType='{x:Type Slider}'>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
<Grid Height='36'>
  <Track x:Name='PART_Track' VerticalAlignment='Center'>
    <Track.DecreaseRepeatButton>
      <RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>
        <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'>
          <Border Height='2' Background='" + Accent + @"'/>
        </ControlTemplate></RepeatButton.Template>
      </RepeatButton>
    </Track.DecreaseRepeatButton>
    <Track.Thumb>
      <Thumb Width='10' Height='24'>
        <Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
          <Border Width='10' Height='24' CornerRadius='5' Background='" + thumb + @"' BorderBrush='" + Accent + @"' BorderThickness='2'/>
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

        static string ButtonStyle(bool primary, bool dark)
        {
            string bg = primary ? Accent : (dark ? "#18FFFFFF" : "#0A000000");
            string fg = primary ? "#FFFFFFFF" : (dark ? "#FFF2F2F2" : "#FF1A1A1A");
            string border = dark ? "#55FFFFFF" : "#55000000";
            string hover = primary ? "#FF1084DE" : (dark ? "#2AFFFFFF" : "#14000000");
            string pressed = primary ? "#FF006CBE" : (dark ? "#3AFFFFFF" : "#22000000");

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

        static void Walk<T>(DependencyObject root, System.Collections.Generic.List<T> list) where T : DependencyObject
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

        static bool IsDarkMode()
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
            catch { return false; }
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
