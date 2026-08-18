using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace TouchKeyboardAudioFloat
{
    sealed class WaveInfo
    {
        public ushort Channels;
        public uint SampleRate;
        public int DataStart;
        public int DataSize;
    }

    sealed class SoundAsset
    {
        public string Name;
        public string SourcePath;
        public string TargetPath;
    }

    sealed class GeneratedAsset
    {
        public SoundAsset Asset;
        public string TempPath;
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy
        {
            public int State;
            public int Flags;
            public int Color;
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

        public static void Acrylic(IntPtr hwnd, bool dark)
        {
            var policy = new AccentPolicy
            {
                State = 4,
                Flags = 2,
                Color = dark ? unchecked((int)0xCC202020) : unchecked((int)0xD9F7F7F7)
            };

            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, mem, false);
                var data = new WcaData { Attribute = 19, Data = mem, Size = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }
    }

    public sealed class MainWindow : Window
    {
        const double InternalGainForDisplay = 0.02;

        static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TouchKeyboardAudio");

        static readonly string StatePath = Path.Combine(DataDir, "float-gain-state.txt");
        static readonly string SafetyBackupDir = Path.Combine(DataDir, "TextInputAssets");

        static readonly string[] SoundNames =
        {
            "KbdAccentPicker.wav",
            "KbdFunction.wav",
            "KbdKeyTap.wav",
            "KbdSpacebar.wav",
            "KbdSwipeGesture.wav"
        };

        readonly string packageRoot;
        readonly List<SoundAsset> assets;
        readonly double baselinePeak;
        readonly bool hadExternalModification;

        Slider slider;
        TextBlock dbText;
        TextBlock gainText;
        TextBlock peakText;
        TextBlock mixText;
        TextBlock statusText;
        Button applyButton;
        Button restoreButton;
        bool dark;

        public MainWindow()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(SafetyBackupDir);

            dark = IsDarkMode();
            packageRoot = FindPackageRoot();
            assets = DiscoverAssets(packageRoot);
            EnsureSafetyBackups();
            baselinePeak = GetBaselinePeak();

            bool targetsOriginal = TargetsMatchBaseline();
            hadExternalModification = !targetsOriginal && !File.Exists(StatePath);

            if (targetsOriginal && File.Exists(StatePath))
                SaveState(0);

            BuildUi();

            SourceInitialized += delegate
            {
                try { Native.Acrylic(new WindowInteropHelper(this).Handle, dark); }
                catch { }
            };

            double initial = File.Exists(StatePath) ? LoadState() : 20.0;
            slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, initial));
            UpdatePreview();
        }

        void BuildUi()
        {
            Title = "Touch Keyboard Audio";
            Width = 640;
            Height = 470;
            MinWidth = 640;
            MinHeight = 470;
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
            head.Children.Add(new TextBlock
            {
                Text = "触摸键盘音量",
                FontSize = 25,
                FontWeight = FontWeights.SemiBold
            });
            head.Children.Add(new TextBlock
            {
                Text = "Float32 Direct Gain · 不压缩、不限制、不硬削顶",
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = SubBrush()
            });
            root.Children.Add(head);

            var card = new Border
            {
                Background = Brush(dark ? "#AA2C2C2C" : "#B8FFFFFF"),
                BorderBrush = Brush(dark ? "#35FFFFFF" : "#22000000"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(24)
            };
            Grid.SetRow(card, 2);
            root.Children.Add(card);

            var panel = new Grid();
            card.Child = panel;
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var values = new Grid();
            Grid.SetRow(values, 0);
            panel.Children.Add(values);

            dbText = new TextBlock
            {
                FontSize = 37,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#FF0078D4")
            };

            gainText = new TextBlock
            {
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SubBrush()
            };

            values.Children.Add(dbText);
            values.Children.Add(gainText);

            slider = new Slider
            {
                Minimum = -20,
                Maximum = 30,
                Value = 20,
                TickFrequency = .5,
                IsSnapToTickEnabled = true,
                SmallChange = .5,
                LargeChange = 2,
                Height = 34
            };
            slider.Style = (Style)XamlReader.Parse(SliderStyle());
            slider.ValueChanged += delegate { UpdatePreview(); };
            Grid.SetRow(slider, 2);
            panel.Children.Add(slider);

            var scale = new Grid();
            Grid.SetRow(scale, 4);
            panel.Children.Add(scale);
            scale.Children.Add(Label("-20 dB", HorizontalAlignment.Left));
            scale.Children.Add(Label("0 dB", HorizontalAlignment.Center));
            scale.Children.Add(Label("+30 dB", HorizontalAlignment.Right));

            var info = new Grid();
            info.ColumnDefinitions.Add(new ColumnDefinition());
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(info, 6);
            panel.Children.Add(info);

            var infoLeft = new StackPanel();
            info.Children.Add(infoLeft);

            peakText = new TextBlock { FontSize = 13 };
            mixText = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = SubBrush()
            };
            statusText = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = SubBrush()
            };

            infoLeft.Children.Add(peakText);
            infoLeft.Children.Add(mixText);
            infoLeft.Children.Add(statusText);

            var formatLabel = new TextBlock
            {
                Text = "5 个 WAV · IEEE Float32",
                FontSize = 11,
                Foreground = SubBrush(),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(formatLabel, 1);
            info.Children.Add(formatLabel);

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(bottom, 4);
            root.Children.Add(bottom);

            bottom.Children.Add(new TextBlock
            {
                Text = "只修改 InputApp\\Assets；微软 PCM16 原版始终保留",
                FontSize = 11,
                Foreground = SubBrush(),
                VerticalAlignment = VerticalAlignment.Center
            });

            restoreButton = MakeButton("恢复原版", false);
            Grid.SetColumn(restoreButton, 1);
            bottom.Children.Add(restoreButton);

            applyButton = MakeButton("应用", true);
            Grid.SetColumn(applyButton, 3);
            bottom.Children.Add(applyButton);

            restoreButton.Click += delegate { RestoreClicked(); };
            applyButton.Click += delegate { ApplyClicked(); };
        }

        Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 92,
                Padding = new Thickness(20, 0, 20, 0),
                BorderThickness = new Thickness(0),
                FontSize = 14,
                Foreground = primary ? Brushes.White : Foreground,
                Background = primary
                    ? Brush("#FF0078D4")
                    : Brush(dark ? "#FF3A3A3A" : "#FFE9E9E9")
            };

            button.Style = (Style)XamlReader.Parse(
                ButtonStyle(
                    primary ? "#FF0078D4" : (dark ? "#FF3A3A3A" : "#FFE9E9E9"),
                    primary ? "White" : (dark ? "#FFF5F5F5" : "#FF202020")));

            return button;
        }

        TextBlock Label(string text, HorizontalAlignment align)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = SubBrush(),
                HorizontalAlignment = align
            };
        }

        Brush SubBrush()
        {
            return Brush(dark ? "#FFBDBDBD" : "#FF666666");
        }

        static Brush Brush(string value)
        {
            return (Brush)new BrushConverter().ConvertFromString(value);
        }

        string SliderStyle()
        {
            string track = dark ? "#FF5B5B5B" : "#FFD0D0D0";
            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
<Grid Height='34'><Track x:Name='PART_Track' VerticalAlignment='Center'>
<Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='#FF0078D4'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
<Track.Thumb><Thumb Width='20' Height='20'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Grid><Ellipse Width='20' Height='20' Fill='#FF0078D4'/><Ellipse Width='8' Height='8' Fill='White'/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
<Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='" + track + @"'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
        }

        static string ButtonStyle(string bg, string fg)
        {
            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
<Setter Property='Background' Value='" + bg + @"'/><Setter Property='Foreground' Value='" + fg + @"'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
<Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='5'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='.88'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Opacity' Value='.72'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter></Style>";
        }

        void UpdatePreview()
        {
            if (slider == null)
                return;

            double db = Math.Round(slider.Value, 1);
            double gain = DbToGain(db);
            double floatPeak = baselinePeak * gain;
            double estimatedMixPeak = floatPeak * InternalGainForDisplay;

            dbText.Text = FormatDb(db);
            gainText.Text = gain.ToString("N2") + "×";

            peakText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Float32 峰值：{0:F3}  ·  文件内不会削顶",
                floatPeak);
            peakText.Foreground = Brush("#FF0078D4");

            mixText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "按当前 TextInput 2% 内部增益估算，混音峰值约 {0:F3}",
                estimatedMixPeak);

            if (hadExternalModification && !File.Exists(StatePath))
            {
                statusText.Text = "检测到外部/手工修改；点击“应用”会统一重写 5 个音效";
            }
            else
            {
                double saved = LoadState();
                statusText.Text = Math.Abs(saved - db) < .05 && !TargetsMatchBaseline()
                    ? "当前正在使用这个 Float32 增益"
                    : "尚未应用";
            }
        }

        void ApplyClicked()
        {
            try
            {
                SetButtons(false);
                double db = Math.Round(slider.Value, 1);
                double gain = DbToGain(db);

                statusText.Text = "正在生成 Float32 直通音效……";
                Dispatcher.Invoke(delegate { }, System.Windows.Threading.DispatcherPriority.Background);

                ApplyFloatGain(gain, db);
                statusText.Text = "已应用 " + FormatDb(db) + " · Float32 Direct Gain";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "应用失败；已尝试回滚原版";
            }
            finally
            {
                SetButtons(true);
                UpdatePreview();
            }
        }

        void RestoreClicked()
        {
            try
            {
                SetButtons(false);
                statusText.Text = "正在恢复微软原版 PCM16 WAV……";
                Dispatcher.Invoke(delegate { }, System.Windows.Threading.DispatcherPriority.Background);

                RestoreBaseline();
                SaveState(0);
                slider.Value = 0;
                statusText.Text = "已恢复微软原版 PCM16";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "恢复失败";
            }
            finally
            {
                SetButtons(true);
                UpdatePreview();
            }
        }

        void SetButtons(bool enabled)
        {
            applyButton.IsEnabled = enabled;
            restoreButton.IsEnabled = enabled;
        }

        void ApplyFloatGain(double gain, double db)
        {
            var generated = new List<GeneratedAsset>();

            try
            {
                foreach (SoundAsset asset in assets)
                {
                    byte[] source = File.ReadAllBytes(asset.SourcePath);
                    WaveInfo info = ParsePcm16Wave(source, asset.SourcePath);
                    byte[] output = BuildFloatWave(source, info, gain);

                    string temp = Path.Combine(
                        Path.GetTempPath(),
                        "TKA_float_" + asset.Name + "." + Guid.NewGuid().ToString("N") + ".wav");

                    File.WriteAllBytes(temp, output);
                    generated.Add(new GeneratedAsset { Asset = asset, TempPath = temp });
                }

                StopInputStack();

                try
                {
                    foreach (GeneratedAsset item in generated)
                        ReplaceAsset(item.TempPath, item.Asset.TargetPath);

                    SaveState(db);
                }
                catch
                {
                    foreach (SoundAsset asset in assets)
                    {
                        try { ReplaceAsset(asset.SourcePath, asset.TargetPath); }
                        catch { }
                    }

                    SaveState(0);
                    throw;
                }
                finally
                {
                    StartTabTip();
                }
            }
            finally
            {
                foreach (GeneratedAsset item in generated)
                {
                    try { if (File.Exists(item.TempPath)) File.Delete(item.TempPath); }
                    catch { }
                }
            }
        }

        static byte[] BuildFloatWave(byte[] source, WaveInfo info, double gain)
        {
            int sampleCount = info.DataSize / 2;
            int frames = sampleCount / Math.Max(1, info.Channels);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write((uint)0);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write((uint)16);
                bw.Write((ushort)3); // WAVE_FORMAT_IEEE_FLOAT
                bw.Write(info.Channels);
                bw.Write(info.SampleRate);

                ushort blockAlign = checked((ushort)(info.Channels * 4));
                uint byteRate = checked(info.SampleRate * blockAlign);

                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write((ushort)32);

                bw.Write(Encoding.ASCII.GetBytes("fact"));
                bw.Write((uint)4);
                bw.Write((uint)frames);

                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(checked((uint)(sampleCount * 4)));

                for (int i = 0; i < sampleCount; i++)
                {
                    short pcm = BitConverter.ToInt16(source, info.DataStart + i * 2);
                    float value = (float)((pcm / 32768.0) * gain);
                    bw.Write(value);
                }

                long length = ms.Length;
                ms.Position = 4;
                bw.Write(checked((uint)(length - 8)));
                bw.Flush();
                return ms.ToArray();
            }
        }

        void RestoreBaseline()
        {
            StopInputStack();
            try
            {
                foreach (SoundAsset asset in assets)
                    ReplaceAsset(asset.SourcePath, asset.TargetPath);

                SaveState(0);
            }
            finally
            {
                StartTabTip();
            }
        }

        static void ReplaceAsset(string source, string target)
        {
            Run("takeown.exe", "/F \"" + target + "\" /A");
            Run("icacls.exe", "\"" + target + "\" /grant *S-1-5-32-544:F /C");

            Exception last = null;

            try
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    try
                    {
                        File.Copy(source, target, true);
                        last = null;
                        break;
                    }
                    catch (IOException ex)
                    {
                        last = ex;
                        KillProcess("TextInputHost");
                        Thread.Sleep(80);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        last = ex;
                        KillProcess("TextInputHost");
                        Thread.Sleep(80);
                    }
                }

                if (last != null)
                    throw new IOException("无法替换正在使用的键盘音效：" + target, last);
            }
            finally
            {
                try { Run("icacls.exe", "\"" + target + "\" /reset /C"); }
                catch { }

                try
                {
                    Run(
                        "icacls.exe",
                        "\"" + target + "\" /setowner \"NT SERVICE\\TrustedInstaller\" /C");
                }
                catch { }
            }
        }

        static void StopInputStack()
        {
            KillProcess("TabTip");

            for (int i = 0; i < 20; i++)
            {
                KillProcess("TextInputHost");
                if (Process.GetProcessesByName("TextInputHost").Length == 0)
                    break;
                Thread.Sleep(80);
            }

            Thread.Sleep(100);
        }

        static void KillProcess(string name)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(700);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }

        static void StartTabTip()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = @"C:\Program Files\Common Files\Microsoft Shared\ink\TabTip.exe",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        static void Run(string file, string args)
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(file + " 执行失败，退出代码 " + process.ExitCode);
            }
        }

        string FindPackageRoot()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemApps = Path.Combine(windows, "SystemApps");

            if (!Directory.Exists(systemApps))
                throw new DirectoryNotFoundException("找不到 Windows SystemApps 目录。");

            string[] candidates = Directory.GetDirectories(
                systemApps,
                "MicrosoftWindows.Client.CBS_*",
                SearchOption.TopDirectoryOnly);

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "TextInputHost.exe")) &&
                    File.Exists(Path.Combine(candidate, "InputApp", "Assets", "KbdKeyTap.wav")))
                    return candidate;
            }

            throw new DirectoryNotFoundException(
                "找不到 MicrosoftWindows.Client.CBS 的 TextInputHost 键盘音效目录。");
        }

        static List<SoundAsset> DiscoverAssets(string root)
        {
            string sourceDir = Path.Combine(root, "Assets");
            string targetDir = Path.Combine(root, "InputApp", "Assets");
            var result = new List<SoundAsset>();

            foreach (string name in SoundNames)
            {
                string source = Path.Combine(sourceDir, name);
                string target = Path.Combine(targetDir, name);

                if (!File.Exists(source))
                    throw new FileNotFoundException("找不到微软原版键盘音效。", source);

                if (!File.Exists(target))
                    throw new FileNotFoundException("找不到 TextInputHost 实际键盘音效。", target);

                ParsePcm16Wave(File.ReadAllBytes(source), source);

                result.Add(new SoundAsset
                {
                    Name = name,
                    SourcePath = source,
                    TargetPath = target
                });
            }

            return result;
        }

        void EnsureSafetyBackups()
        {
            foreach (SoundAsset asset in assets)
            {
                string path = Path.Combine(SafetyBackupDir, asset.Name + ".original.wav");
                if (!File.Exists(path))
                    File.Copy(asset.SourcePath, path, true);
            }
        }

        double GetBaselinePeak()
        {
            double peak = 0;

            foreach (SoundAsset asset in assets)
            {
                byte[] data = File.ReadAllBytes(asset.SourcePath);
                WaveInfo wave = ParsePcm16Wave(data, asset.SourcePath);

                for (int p = wave.DataStart; p + 1 < wave.DataStart + wave.DataSize; p += 2)
                {
                    double value = Math.Abs(BitConverter.ToInt16(data, p) / 32768.0);
                    if (value > peak)
                        peak = value;
                }
            }

            return peak;
        }

        static WaveInfo ParsePcm16Wave(byte[] data, string path)
        {
            if (data.Length < 12 ||
                Encoding.ASCII.GetString(data, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
                throw new InvalidOperationException("不是 RIFF/WAVE 文件：" + path);

            int p = 12;
            ushort format = 0;
            ushort channels = 0;
            uint sampleRate = 0;
            ushort bits = 0;
            int dataStart = -1;
            int dataSize = 0;

            while (p + 8 <= data.Length)
            {
                string id = Encoding.ASCII.GetString(data, p, 4);
                uint size = BitConverter.ToUInt32(data, p + 4);
                long chunkData = p + 8L;
                long chunkEnd = chunkData + size;

                if (chunkEnd > data.Length)
                    throw new InvalidOperationException("WAV chunk 越界：" + path);

                if (id == "fmt " && size >= 16)
                {
                    format = BitConverter.ToUInt16(data, (int)chunkData);
                    channels = BitConverter.ToUInt16(data, (int)chunkData + 2);
                    sampleRate = BitConverter.ToUInt32(data, (int)chunkData + 4);
                    bits = BitConverter.ToUInt16(data, (int)chunkData + 14);
                }
                else if (id == "data")
                {
                    dataStart = (int)chunkData;
                    dataSize = checked((int)size);
                    break;
                }

                p = checked((int)(chunkEnd + (size % 2)));
            }

            if (format != 1 || bits != 16 || channels == 0 || sampleRate == 0 || dataStart < 0)
                throw new InvalidOperationException("微软原版键盘音效不是预期的 PCM16 WAV：" + path);

            return new WaveInfo
            {
                Channels = channels,
                SampleRate = sampleRate,
                DataStart = dataStart,
                DataSize = dataSize
            };
        }

        bool TargetsMatchBaseline()
        {
            foreach (SoundAsset asset in assets)
            {
                if (!FilesEqual(asset.SourcePath, asset.TargetPath))
                    return false;
            }

            return true;
        }

        static bool FilesEqual(string a, string b)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] ha;
                byte[] hb;

                using (FileStream fa = File.OpenRead(a))
                    ha = sha.ComputeHash(fa);

                using (FileStream fb = File.OpenRead(b))
                    hb = sha.ComputeHash(fb);

                if (ha.Length != hb.Length)
                    return false;

                for (int i = 0; i < ha.Length; i++)
                    if (ha[i] != hb[i])
                        return false;

                return true;
            }
        }

        static double DbToGain(double db)
        {
            return Math.Pow(10.0, db / 20.0);
        }

        static string FormatDb(double db)
        {
            return Math.Abs(db) < .05
                ? "0.0 dB"
                : (db > 0 ? "+" : "") + db.ToString("N1") + " dB";
        }

        static void SaveState(double value)
        {
            File.WriteAllText(StatePath, value.ToString("R", CultureInfo.InvariantCulture));
        }

        static double LoadState()
        {
            double value;
            return File.Exists(StatePath) &&
                   double.TryParse(
                       File.ReadAllText(StatePath),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value)
                ? value
                : 0;
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
            catch
            {
                return false;
            }
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
                app.Run(new MainWindow());
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
