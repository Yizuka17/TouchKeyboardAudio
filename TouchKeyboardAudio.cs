using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace TouchKeyboardAudio
{
    sealed class WaveRegion { public int Start; public int Size; }
    sealed class ClipStats { public double Gain; public int Clipped; public int Total; public double Percent; }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags, Color, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct WcaData { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WcaData data);

        public static void Acrylic(IntPtr hwnd, bool dark)
        {
            var p = new AccentPolicy { State = 4, Flags = 2, Color = dark ? unchecked((int)0xCC202020) : unchecked((int)0xD9F7F7F7) };
            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr mem = Marshal.AllocHGlobal(size);
            try {
                Marshal.StructureToPtr(p, mem, false);
                var d = new WcaData { Attribute = 19, Data = mem, Size = size };
                SetWindowCompositionAttribute(hwnd, ref d);
            } finally { Marshal.FreeHGlobal(mem); }
        }
    }

    public sealed class MainWindow : Window
    {
        const string DllPath = @"C:\Program Files\Common Files\Microsoft Shared\ink\tabskb.dll";
        static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TouchKeyboardAudio");
        static readonly string BackupPath = Path.Combine(DataDir, "tabskb.original.dll");
        static readonly string StatePath = Path.Combine(DataDir, "state.txt");
        static readonly string TempPath = Path.Combine(Path.GetTempPath(), "tabskb.tka.tmp.dll");

        byte[] original;
        List<WaveRegion> regions;
        List<short> samples;
        Slider slider;
        TextBlock dbText, gainText, clipText, statusText;
        Button applyButton, restoreButton;
        bool dark;

        public MainWindow()
        {
            Directory.CreateDirectory(DataDir);
            dark = IsDarkMode();
            EnsureBackup();
            original = File.ReadAllBytes(BackupPath);
            regions = FindWaves(original);
            if (regions.Count != 14) throw new InvalidOperationException("预期找到 14 个触摸键盘音效，实际为 " + regions.Count + " 个。为安全起见已停止。");
            samples = ExtractSamples(original, regions);
            BuildUi();
            SourceInitialized += delegate { try { Native.Acrylic(new WindowInteropHelper(this).Handle, dark); } catch { } };
            double saved = LoadState();
            slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, saved));
            UpdatePreview();
        }

        void BuildUi()
        {
            Title = "Touch Keyboard Audio";
            Width = 590; Height = 430; MinWidth = 590; MinHeight = 430;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");
            Background = Brush(dark ? "#CC202020" : "#D9F7F7F7");
            Foreground = Brush(dark ? "#FFF5F5F5" : "#FF202020");

            var root = new Grid { Margin = new Thickness(34) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = "触摸键盘音量", FontSize = 25, FontWeight = FontWeights.SemiBold });
            head.Children.Add(new TextBlock { Text = "调整 Windows 10 触摸键盘的按键反馈音", FontSize = 13, Margin = new Thickness(0, 6, 0, 0), Foreground = SubBrush() });
            root.Children.Add(head);

            var card = new Border { Background = Brush(dark ? "#AA2C2C2C" : "#B8FFFFFF"), BorderBrush = Brush(dark ? "#35FFFFFF" : "#22000000"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(24) };
            Grid.SetRow(card, 2); root.Children.Add(card);
            var panel = new Grid(); card.Child = panel;
            for (int i = 0; i < 7; i++) panel.RowDefinitions.Add(new RowDefinition { Height = (i == 1 ? new GridLength(20) : i == 3 ? new GridLength(4) : i == 5 ? new GridLength(22) : GridLength.Auto) });

            var values = new Grid(); Grid.SetRow(values, 0); panel.Children.Add(values);
            dbText = new TextBlock { FontSize = 37, FontWeight = FontWeights.SemiBold, Foreground = Brush("#FF0078D4") };
            gainText = new TextBlock { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = SubBrush() };
            values.Children.Add(dbText); values.Children.Add(gainText);

            slider = new Slider { Minimum = -20, Maximum = 30, Value = 20, TickFrequency = .5, IsSnapToTickEnabled = true, SmallChange = .5, LargeChange = 2, Height = 34 };
            slider.Style = (Style)XamlReader.Parse(SliderStyle());
            slider.ValueChanged += delegate { UpdatePreview(); };
            Grid.SetRow(slider, 2); panel.Children.Add(slider);

            var scale = new Grid(); Grid.SetRow(scale, 4); panel.Children.Add(scale);
            scale.Children.Add(Label("-20 dB", HorizontalAlignment.Left));
            scale.Children.Add(Label("0 dB", HorizontalAlignment.Center));
            scale.Children.Add(Label("+30 dB", HorizontalAlignment.Right));

            var info = new Grid(); info.ColumnDefinitions.Add(new ColumnDefinition()); info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetRow(info, 6); panel.Children.Add(info);
            var infoLeft = new StackPanel(); info.Children.Add(infoLeft);
            clipText = new TextBlock { FontSize = 13 }; infoLeft.Children.Add(clipText);
            statusText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 5, 0, 0), Foreground = SubBrush() }; infoLeft.Children.Add(statusText);
            var wave = new TextBlock { Text = "14 个 PCM16 音效", FontSize = 11, Foreground = SubBrush(), VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetColumn(wave, 1); info.Children.Add(wave);

            var bottom = new Grid(); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetRow(bottom, 4); root.Children.Add(bottom);
            bottom.Children.Add(new TextBlock { Text = "每次都从微软原版重新生成，不会叠加增益", FontSize = 11, Foreground = SubBrush(), VerticalAlignment = VerticalAlignment.Center });
            restoreButton = MakeButton("恢复原版", false); Grid.SetColumn(restoreButton, 1); bottom.Children.Add(restoreButton);
            applyButton = MakeButton("应用", true); Grid.SetColumn(applyButton, 3); bottom.Children.Add(applyButton);
            restoreButton.Click += delegate { RestoreClicked(); };
            applyButton.Click += delegate { ApplyClicked(); };
        }

        Button MakeButton(string text, bool primary)
        {
            var b = new Button { Content = text, Height = 38, MinWidth = 92, Padding = new Thickness(20, 0, 20, 0), BorderThickness = new Thickness(0), FontSize = 14, Foreground = primary ? Brushes.White : Foreground, Background = primary ? Brush("#FF0078D4") : Brush(dark ? "#FF3A3A3A" : "#FFE9E9E9") };
            b.Style = (Style)XamlReader.Parse(ButtonStyle(primary ? "#FF0078D4" : (dark ? "#FF3A3A3A" : "#FFE9E9E9"), primary ? "White" : (dark ? "#FFF5F5F5" : "#FF202020")));
            return b;
        }

        TextBlock Label(string text, HorizontalAlignment align) { return new TextBlock { Text = text, FontSize = 12, Foreground = SubBrush(), HorizontalAlignment = align }; }
        Brush SubBrush() { return Brush(dark ? "#FFBDBDBD" : "#FF666666"); }
        static Brush Brush(string s) { return (Brush)new BrushConverter().ConvertFromString(s); }

        string SliderStyle()
        {
            string track = dark ? "#FF5B5B5B" : "#FFD0D0D0";
            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'><Grid Height='34'><Track x:Name='PART_Track' VerticalAlignment='Center'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='#FF0078D4'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='20' Height='20'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Grid><Ellipse Width='20' Height='20' Fill='#FF0078D4'/><Ellipse Width='8' Height='8' Fill='White'/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='" + track + @"'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
        }

        static string ButtonStyle(string bg, string fg)
        {
            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'><Setter Property='Background' Value='" + bg + @"'/><Setter Property='Foreground' Value='" + fg + @"'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'><Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='5'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='.88'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Opacity' Value='.72'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
        }

        void UpdatePreview()
        {
            if (slider == null) return;
            double db = Math.Round(slider.Value, 1); var s = Stats(db);
            dbText.Text = FormatDb(db); gainText.Text = s.Gain.ToString("N2") + "×";
            clipText.Text = string.Format("预计削顶：{0:N2}%  ({1:N0} / {2:N0} samples)", s.Percent, s.Clipped, s.Total);
            clipText.Foreground = s.Percent >= 10 ? Brushes.IndianRed : s.Percent >= 1 ? Brushes.DarkOrange : Foreground;
            statusText.Text = Math.Abs(LoadState() - db) < .05 ? "当前正在使用这个增益" : "尚未应用";
        }

        void ApplyClicked()
        {
            try { SetButtons(false); double db = Math.Round(slider.Value, 1); statusText.Text = "正在写入触摸键盘音效……"; Dispatcher.Invoke(delegate { }, System.Windows.Threading.DispatcherPriority.Background); var r = ApplyGain(db); statusText.Text = "已应用 " + FormatDb(db) + "，实际削顶 " + r.Percent.ToString("N2") + "%"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); statusText.Text = "应用失败"; }
            finally { SetButtons(true); UpdatePreview(); }
        }

        void RestoreClicked()
        {
            try { SetButtons(false); statusText.Text = "正在恢复微软原版……"; Dispatcher.Invoke(delegate { }, System.Windows.Threading.DispatcherPriority.Background); ReplaceDll(BackupPath); SaveState(0); StartTabTip(); slider.Value = 0; statusText.Text = "已恢复微软原版"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); statusText.Text = "恢复失败"; }
            finally { SetButtons(true); UpdatePreview(); }
        }

        void SetButtons(bool v) { applyButton.IsEnabled = v; restoreButton.IsEnabled = v; }
        static string FormatDb(double d) { return Math.Abs(d) < .05 ? "0.0 dB" : (d > 0 ? "+" : "") + d.ToString("N1") + " dB"; }

        ClipStats Stats(double db)
        {
            double g = Math.Pow(10, db / 20.0); int c = 0;
            foreach (short x in samples) { double v = x * g; if (v > 32767 || v < -32768) c++; }
            return new ClipStats { Gain = g, Clipped = c, Total = samples.Count, Percent = samples.Count == 0 ? 0 : 100.0 * c / samples.Count };
        }

        ClipStats ApplyGain(double db)
        {
            double g = Math.Pow(10, db / 20.0); byte[] b = (byte[])original.Clone(); int c = 0, total = 0;
            foreach (var r in regions) for (int p = r.Start; p + 1 < r.Start + r.Size; p += 2) { int v = (int)Math.Round(BitConverter.ToInt16(b, p) * g); total++; if (v > 32767) { v = 32767; c++; } else if (v < -32768) { v = -32768; c++; } short n = (short)v; b[p] = (byte)(n & 255); b[p + 1] = (byte)((n >> 8) & 255); }
            File.WriteAllBytes(TempPath, b); if (new FileInfo(TempPath).Length != original.Length) throw new InvalidOperationException("修改后的 DLL 长度发生变化，已停止。");
            ReplaceDll(TempPath); try { File.Delete(TempPath); } catch { } SaveState(db); StartTabTip();
            return new ClipStats { Gain = g, Clipped = c, Total = total, Percent = total == 0 ? 0 : 100.0 * c / total };
        }

        static void ReplaceDll(string source)
        {
            StopTabTip(); Run("takeown.exe", "/F \"" + DllPath + "\" /A"); Run("icacls.exe", "\"" + DllPath + "\" /grant *S-1-5-32-544:F /C");
            try { File.Copy(source, DllPath, true); }
            finally { try { Run("icacls.exe", "\"" + DllPath + "\" /reset /C"); } catch { } try { Run("icacls.exe", "\"" + DllPath + "\" /setowner \"NT SERVICE\\TrustedInstaller\" /C"); } catch { } }
        }

        static void Run(string file, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) { p.WaitForExit(); if (p.ExitCode != 0) throw new InvalidOperationException(file + " 执行失败，退出代码 " + p.ExitCode); }
        }
        static void StopTabTip() { foreach (var p in Process.GetProcessesByName("TabTip")) try { p.Kill(); p.WaitForExit(1000); } catch { } System.Threading.Thread.Sleep(250); }
        static void StartTabTip() { Process.Start(new ProcessStartInfo { FileName = @"C:\Program Files\Common Files\Microsoft Shared\ink\TabTip.exe", UseShellExecute = true }); }

        static void EnsureBackup()
        {
            if (!File.Exists(DllPath)) throw new FileNotFoundException("找不到系统 tabskb.dll。", DllPath);
            if (!File.Exists(BackupPath)) { File.Copy(DllPath, BackupPath, true); SaveState(0); }
        }

        static List<WaveRegion> FindWaves(byte[] b)
        {
            var list = new List<WaveRegion>();
            for (int i = 0; i <= b.Length - 12; i++) if (b[i] == 0x52 && b[i+1] == 0x49 && b[i+2] == 0x46 && b[i+3] == 0x46 && b[i+8] == 0x57 && b[i+9] == 0x41 && b[i+10] == 0x56 && b[i+11] == 0x45) { uint rs = BitConverter.ToUInt32(b, i+4); long end = (long)i + 8 + rs; if (end > b.Length) continue; int p = i + 12; ushort fmt = 0, bits = 0; while ((long)p + 8 <= end) { string id = Encoding.ASCII.GetString(b, p, 4); uint size = BitConverter.ToUInt32(b, p+4); int data = p + 8; if ((long)data + size > end) break; if (id == "fmt " && size >= 16) { fmt = BitConverter.ToUInt16(b, data); bits = BitConverter.ToUInt16(b, data+14); } else if (id == "data") { if (fmt != 1 || bits != 16) throw new InvalidOperationException("发现非 PCM16 音效，已停止。"); list.Add(new WaveRegion { Start = data, Size = checked((int)size) }); } p = checked((int)(p + 8L + size + (size % 2))); } i = checked((int)end - 1); }
            return list;
        }
        static List<short> ExtractSamples(byte[] b, List<WaveRegion> rr) { var l = new List<short>(); foreach (var r in rr) for (int p = r.Start; p + 1 < r.Start + r.Size; p += 2) l.Add(BitConverter.ToInt16(b, p)); return l; }
        static void SaveState(double d) { File.WriteAllText(StatePath, d.ToString("R", CultureInfo.InvariantCulture)); }
        static double LoadState() { double d; return File.Exists(StatePath) && double.TryParse(File.ReadAllText(StatePath), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0; }
        static bool IsDarkMode() { try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) { object v = k == null ? null : k.GetValue("AppsUseLightTheme"); return v != null && Convert.ToInt32(v) == 0; } } catch { return false; } }
    }

    public static class Program
    {
        [STAThread] public static void Main() { try { var app = new Application(); app.ShutdownMode = ShutdownMode.OnMainWindowClose; app.Run(new MainWindow()); } catch (Exception ex) { MessageBox.Show(ex.Message, "Touch Keyboard Audio", MessageBoxButton.OK, MessageBoxImage.Error); } }
    }
}
