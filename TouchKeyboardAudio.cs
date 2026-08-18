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

namespace TouchKeyboardAudio
{
    enum DspMode
    {
        Limiter,
        LinearSafe,
        HardClip
    }

    sealed class WaveInfo
    {
        public int DataStart;
        public int DataSize;
        public int Channels;
        public int SampleRate;
    }

    sealed class SoundAsset
    {
        public string Name;
        public string SourcePath;
        public string TargetPath;
        public string BaselinePath;
    }

    sealed class PreviewStats
    {
        public double RequestedGain;
        public double SafeDb;
        public int Over;
        public int Total;
        public double OverPercent;
        public double MaxReductionDb;
    }

    sealed class ProcessStats
    {
        public int Limited;
        public int HardClipped;
        public int Total;
        public double MaxReductionDb;
        public double EffectiveDb;
    }

    sealed class AppState
    {
        public double Db = 20;
        public DspMode Mode = DspMode.Limiter;
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
        const double LimiterCeilingDb = -1.0;
        const double LimiterLookAheadMs = 1.5;
        const double LimiterReleaseMs = 10.0;

        static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TouchKeyboardAudio");

        static readonly string StatePath = Path.Combine(DataDir, "wav-dsp-state.txt");
        static readonly string SafetyBackupDir = Path.Combine(DataDir, "TextInputAssets");

        static readonly string[] SoundNames =
        {
            "KbdAccentPicker.wav",
            "KbdFunction.wav",
            "KbdKeyTap.wav",
            "KbdSpacebar.wav",
            "KbdSwipeGesture.wav"
        };

        readonly List<SoundAsset> assets;
        readonly List<short> baselineSamples;
        readonly double baselinePeak;

        Slider slider;
        ComboBox modeCombo;
        TextBlock dbText;
        TextBlock gainText;
        TextBlock processText;
        TextBlock detailText;
        TextBlock statusText;
        Button applyButton;
        Button restoreButton;
        bool dark;

        public MainWindow()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(SafetyBackupDir);

            dark = IsDarkMode();
            string packageRoot = FindPackageRoot();
            assets = DiscoverAssets(packageRoot);
            EnsureSafetyBackups();
            baselineSamples = LoadBaselineSamples();
            baselinePeak = FindPeak(baselineSamples);

            BuildUi();

            SourceInitialized += delegate
            {
                try { Native.Acrylic(new WindowInteropHelper(this).Handle, dark); }
                catch { }
            };

            AppState state = LoadState();
            slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, state.Db));
            SelectMode(state.Mode);
            UpdatePreview();
        }

        void BuildUi()
        {
            Title = "Touch Keyboard Audio";
            Width = 650;
            Height = 535;
            MinWidth = 650;
            MinHeight = 535;
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
                Text = "Float DSP 放大 · Look-ahead limiter · PCM16 dither",
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
            for (int i = 0; i < 9; i++)
            {
                panel.RowDefinitions.Add(new RowDefinition
                {
                    Height = (i == 1 || i == 3 || i == 6)
                        ? new GridLength(i == 3 ? 8 : 16)
                        : GridLength.Auto
                });
            }

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

            var modeRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            modeRow.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(modeRow, 5);
            panel.Children.Add(modeRow);

            modeRow.Children.Add(new TextBlock
            {
                Text = "处理方式",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SubBrush()
            });

            modeCombo = new ComboBox
            {
                Height = 34,
                MinWidth = 245,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0, 8, 0),
                FontSize = 13
            };
            modeCombo.Items.Add(ModeItem("智能限制器（推荐）", DspMode.Limiter));
            modeCombo.Items.Add(ModeItem("线性安全（无削顶）", DspMode.LinearSafe));
            modeCombo.Items.Add(ModeItem("硬削顶（A/B 对比）", DspMode.HardClip));
            modeCombo.SelectionChanged += delegate { UpdatePreview(); };
            Grid.SetColumn(modeCombo, 2);
            modeRow.Children.Add(modeCombo);

            var info = new StackPanel();
            Grid.SetRow(info, 7);
            panel.Children.Add(info);

            processText = new TextBlock { FontSize = 13 };
            detailText = new TextBlock
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
            info.Children.Add(processText);
            info.Children.Add(detailText);
            info.Children.Add(statusText);

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(bottom, 4);
            root.Children.Add(bottom);

            bottom.Children.Add(new TextBlock
            {
                Text = "仅改 5 个 InputApp WAV · 不碰 TextInput.dll",
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

        ComboBoxItem ModeItem(string text, DspMode mode)
        {
            return new ComboBoxItem { Content = text, Tag = mode, Padding = new Thickness(5) };
        }

        void SelectMode(DspMode mode)
        {
            foreach (object item in modeCombo.Items)
            {
                var comboItem = item as ComboBoxItem;
                if (comboItem != null && (DspMode)comboItem.Tag == mode)
                {
                    modeCombo.SelectedItem = comboItem;
                    return;
                }
            }
            modeCombo.SelectedIndex = 0;
        }

        DspMode SelectedMode()
        {
            var item = modeCombo == null ? null : modeCombo.SelectedItem as ComboBoxItem;
            return item == null ? DspMode.Limiter : (DspMode)item.Tag;
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
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'><Grid Height='34'><Track x:Name='PART_Track' VerticalAlignment='Center'>
<Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='#FF0078D4'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
<Track.Thumb><Thumb Width='20' Height='20'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Grid><Ellipse Width='20' Height='20' Fill='#FF0078D4'/><Ellipse Width='8' Height='8' Fill='White'/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
<Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='" + track + @"'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
        }

        static string ButtonStyle(string bg, string fg)
        {
            return @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
<Setter Property='Background' Value='" + bg + @"'/><Setter Property='Foreground' Value='" + fg + @"'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'><Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='5'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Opacity' Value='.88'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Opacity' Value='.72'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='B' Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter></Style>";
        }

        void UpdatePreview()
        {
            if (slider == null || modeCombo == null || modeCombo.SelectedItem == null)
                return;

            double db = Math.Round(slider.Value, 1);
            DspMode mode = SelectedMode();
            PreviewStats stats = GetPreviewStats(db);

            dbText.Text = FormatDb(db);
            gainText.Text = stats.RequestedGain.ToString("N2") + "×";

            if (mode == DspMode.Limiter)
            {
                processText.Text = string.Format(
                    "预计需限制的峰值样本：{0:N2}%  ({1:N0} / {2:N0})",
                    stats.OverPercent, stats.Over, stats.Total);
                detailText.Text = string.Format(
                    "Look-ahead {0:N1} ms · release {1:N0} ms · ceiling {2:N1} dBFS · 最大压低约 {3:N1} dB",
                    LimiterLookAheadMs, LimiterReleaseMs, LimiterCeilingDb, stats.MaxReductionDb);
                processText.Foreground = stats.OverPercent > 0 ? Brush("#FF0078D4") : Foreground;
            }
            else if (mode == DspMode.LinearSafe)
            {
                double effective = Math.Min(db, stats.SafeDb);
                processText.Text = db <= stats.SafeDb + .001
                    ? "线性放大处于安全范围：不会削顶"
                    : "请求增益超过线性余量，将自动限制到 " + FormatDb(effective);
                detailText.Text = "全局安全上限约 " + FormatDb(stats.SafeDb) + "，保持 5 个音效之间的相对响度";
                processText.Foreground = db <= stats.SafeDb + .001 ? Foreground : Brushes.DarkOrange;
            }
            else
            {
                processText.Text = string.Format(
                    "预计硬削顶：{0:N2}%  ({1:N0} / {2:N0} samples)",
                    stats.OverPercent, stats.Over, stats.Total);
                detailText.Text = "仅用于 A/B 对比；超过 -1 dBFS 的峰值会被直接截断";
                processText.Foreground = stats.OverPercent > 0 ? Brushes.IndianRed : Foreground;
            }

            AppState state = LoadState();
            statusText.Text = Math.Abs(state.Db - db) < .05 && state.Mode == mode
                ? "当前记录的是这个设置"
                : "尚未应用";
        }

        PreviewStats GetPreviewStats(double db)
        {
            double gain = DbToGain(db);
            double ceiling = DbToGain(LimiterCeilingDb);
            int over = 0;

            foreach (short sample in baselineSamples)
            {
                double value = Math.Abs(sample / 32768.0) * gain;
                if (value > ceiling)
                    over++;
            }

            double safeGain = baselinePeak <= 0 ? gain : ceiling / baselinePeak;
            double safeDb = GainToDb(safeGain);
            double maxReduction = baselinePeak <= 0
                ? 0
                : Math.Max(0, GainToDb(baselinePeak * gain / ceiling));

            return new PreviewStats
            {
                RequestedGain = gain,
                SafeDb = safeDb,
                Over = over,
                Total = baselineSamples.Count,
                OverPercent = baselineSamples.Count == 0 ? 0 : 100.0 * over / baselineSamples.Count,
                MaxReductionDb = maxReduction
            };
        }

        void ApplyClicked()
        {
            try
            {
                SetButtons(false);
                double db = Math.Round(slider.Value, 1);
                DspMode mode = SelectedMode();

                statusText.Text = "正在生成并写入 DSP 处理后的键盘音效……";
                Dispatcher.Invoke(delegate { }, System.Windows.Threading.DispatcherPriority.Background);

                ProcessStats result = ApplyDsp(db, mode);
                SaveState(new AppState { Db = db, Mode = mode });

                if (mode == DspMode.Limiter)
                {
                    statusText.Text = string.Format(
                        "已应用 {0} · limiter 作用于 {1:N2}% samples · 硬削顶 0",
                        FormatDb(db), Percent(result.Limited, result.Total));
                }
                else if (mode == DspMode.LinearSafe)
                {
                    statusText.Text = "已应用线性安全增益 " + FormatDb(result.EffectiveDb) + " · 无削顶";
                }
                else
                {
                    statusText.Text = string.Format(
                        "已应用 {0} · 硬削顶 {1:N2}%",
                        FormatDb(db), Percent(result.HardClipped, result.Total));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "应用失败；已尝试自动回滚原版 WAV";
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
                statusText.Text = "正在恢复微软原版 WAV……";
                Dispatcher.Invoke(delegate { }, System.Windows.Threading.DispatcherPriority.Background);

                RestoreBaseline();
                SaveState(new AppState { Db = 0, Mode = DspMode.Limiter });
                slider.Value = 0;
                SelectMode(DspMode.Limiter);
                statusText.Text = "已恢复微软原版";
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
            modeCombo.IsEnabled = enabled;
            slider.IsEnabled = enabled;
        }

        ProcessStats ApplyDsp(double requestedDb, DspMode mode)
        {
            PreviewStats preview = GetPreviewStats(requestedDb);
            double effectiveDb = mode == DspMode.LinearSafe
                ? Math.Min(requestedDb, preview.SafeDb)
                : requestedDb;
            double gain = DbToGain(effectiveDb);

            var generated = new Dictionary<SoundAsset, string>();
            var totalStats = new ProcessStats { EffectiveDb = effectiveDb };

            try
            {
                foreach (SoundAsset asset in assets)
                {
                    byte[] data = File.ReadAllBytes(asset.BaselinePath);
                    WaveInfo wave = ParsePcm16Wave(data, asset.BaselinePath);
                    ProcessStats stats = ProcessWave(data, wave, gain, mode, SeedFor(asset.Name));

                    totalStats.Limited += stats.Limited;
                    totalStats.HardClipped += stats.HardClipped;
                    totalStats.Total += stats.Total;
                    totalStats.MaxReductionDb = Math.Max(totalStats.MaxReductionDb, stats.MaxReductionDb);

                    string temp = Path.Combine(
                        Path.GetTempPath(),
                        "TKA_" + asset.Name + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    File.WriteAllBytes(temp, data);
                    generated.Add(asset, temp);
                }

                StopInputStack();
                try
                {
                    foreach (KeyValuePair<SoundAsset, string> pair in generated)
                        ReplaceAsset(pair.Value, pair.Key.TargetPath);
                }
                catch
                {
                    foreach (SoundAsset asset in assets)
                    {
                        try { ReplaceAsset(asset.BaselinePath, asset.TargetPath); }
                        catch { }
                    }
                    throw;
                }
                finally
                {
                    StartTabTip();
                }
            }
            finally
            {
                foreach (string temp in generated.Values)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch { }
                }
            }

            return totalStats;
        }

        static ProcessStats ProcessWave(byte[] data, WaveInfo wave, double gain, DspMode mode, int seed)
        {
            int sampleCount = wave.DataSize / 2;
            int channels = Math.Max(1, wave.Channels);
            int frames = sampleCount / channels;
            var input = new double[sampleCount];
            var output = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
                input[i] = BitConverter.ToInt16(data, wave.DataStart + i * 2) / 32768.0;

            double ceiling = DbToGain(LimiterCeilingDb);
            var result = new ProcessStats { Total = sampleCount };

            if (mode == DspMode.Limiter)
            {
                double[] required = new double[frames];
                for (int frame = 0; frame < frames; frame++)
                {
                    double peak = 0;
                    for (int ch = 0; ch < channels; ch++)
                        peak = Math.Max(peak, Math.Abs(input[frame * channels + ch] * gain));
                    required[frame] = peak > ceiling ? ceiling / peak : 1.0;
                }

                int lookAhead = Math.Max(1, (int)Math.Round(wave.SampleRate * LimiterLookAheadMs / 1000.0));
                double[] target = new double[frames];
                for (int frame = 0; frame < frames; frame++)
                {
                    double min = 1.0;
                    int end = Math.Min(frames - 1, frame + lookAhead);
                    for (int j = frame; j <= end; j++)
                        if (required[j] < min) min = required[j];
                    target[frame] = min;
                }

                double releaseStep = 1.0 - Math.Exp(-1.0 / Math.Max(1.0, wave.SampleRate * LimiterReleaseMs / 1000.0));
                double envelope = 1.0;

                for (int frame = 0; frame < frames; frame++)
                {
                    if (target[frame] < envelope)
                        envelope = target[frame];
                    else
                        envelope += (target[frame] - envelope) * releaseStep;

                    if (envelope < 0.999999)
                    {
                        result.Limited += channels;
                        result.MaxReductionDb = Math.Max(result.MaxReductionDb, -GainToDb(envelope));
                    }

                    for (int ch = 0; ch < channels; ch++)
                        output[frame * channels + ch] = input[frame * channels + ch] * gain * envelope;
                }
            }
            else
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    double value = input[i] * gain;
                    if (mode == DspMode.HardClip && Math.Abs(value) > ceiling)
                    {
                        value = Math.Sign(value) * ceiling;
                        result.HardClipped++;
                    }
                    output[i] = value;
                }
            }

            var random = new Random(seed);
            for (int i = 0; i < sampleCount; i++)
            {
                double value = Math.Max(-ceiling, Math.Min(ceiling, output[i]));
                double tpdf = random.NextDouble() - random.NextDouble();
                int quantized = (int)Math.Round(value * 32768.0 + tpdf);
                if (quantized > 32767) quantized = 32767;
                if (quantized < -32768) quantized = -32768;

                short sample = (short)quantized;
                int p = wave.DataStart + i * 2;
                data[p] = (byte)(sample & 0xFF);
                data[p + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return result;
        }

        void RestoreBaseline()
        {
            StopInputStack();
            try
            {
                foreach (SoundAsset asset in assets)
                    ReplaceAsset(asset.BaselinePath, asset.TargetPath);
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
                    Run("icacls.exe", "\"" + target + "\" /setowner \"NT SERVICE\\TrustedInstaller\" /C");
                }
                catch { }
            }
        }

        static void StopInputStack()
        {
            KillProcess("TabTip");
            for (int i = 0; i < 15; i++)
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
                finally { process.Dispose(); }
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

        static string FindPackageRoot()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemApps = Path.Combine(windows, "SystemApps");
            if (!Directory.Exists(systemApps))
                throw new DirectoryNotFoundException("找不到 Windows SystemApps 目录。");

            string[] candidates = Directory.GetDirectories(systemApps, "MicrosoftWindows.Client.CBS_*", SearchOption.TopDirectoryOnly);
            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "TextInputHost.exe")) &&
                    File.Exists(Path.Combine(candidate, "InputApp", "Assets", "KbdKeyTap.wav")))
                    return candidate;
            }
            throw new DirectoryNotFoundException("找不到 MicrosoftWindows.Client.CBS 的 TextInputHost 键盘音效目录。");
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
                result.Add(new SoundAsset { Name = name, SourcePath = source, TargetPath = target });
            }
            return result;
        }

        void EnsureSafetyBackups()
        {
            foreach (SoundAsset asset in assets)
            {
                string baseline = Path.Combine(SafetyBackupDir, "Baseline_" + asset.Name);
                if (!File.Exists(baseline))
                    File.Copy(asset.SourcePath, baseline, true);

                ParsePcm16Wave(File.ReadAllBytes(baseline), baseline);
                asset.BaselinePath = baseline;
            }
        }

        List<short> LoadBaselineSamples()
        {
            var result = new List<short>();
            foreach (SoundAsset asset in assets)
            {
                byte[] data = File.ReadAllBytes(asset.BaselinePath);
                WaveInfo wave = ParsePcm16Wave(data, asset.BaselinePath);
                for (int p = wave.DataStart; p + 1 < wave.DataStart + wave.DataSize; p += 2)
                    result.Add(BitConverter.ToInt16(data, p));
            }
            return result;
        }

        static double FindPeak(List<short> samples)
        {
            double peak = 0;
            foreach (short sample in samples)
                peak = Math.Max(peak, Math.Abs(sample / 32768.0));
            return peak;
        }

        static WaveInfo ParsePcm16Wave(byte[] data, string path)
        {
            if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
                throw new InvalidOperationException("不是 RIFF/WAVE 文件：" + path);

            int p = 12;
            ushort format = 0;
            ushort bits = 0;
            ushort channels = 0;
            int sampleRate = 0;
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
                    sampleRate = BitConverter.ToInt32(data, (int)chunkData + 4);
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

            if (format != 1 || bits != 16 || channels < 1 || sampleRate < 8000 || dataStart < 0)
                throw new InvalidOperationException("键盘音效不是预期的 PCM16 WAV：" + path);
            if ((dataSize / 2) % channels != 0)
                throw new InvalidOperationException("PCM sample 数与声道数不匹配：" + path);

            return new WaveInfo
            {
                DataStart = dataStart,
                DataSize = dataSize,
                Channels = channels,
                SampleRate = sampleRate
            };
        }

        static double DbToGain(double db)
        {
            return Math.Pow(10.0, db / 20.0);
        }

        static double GainToDb(double gain)
        {
            return gain <= 0 ? -120 : 20.0 * Math.Log10(gain);
        }

        static double Percent(int value, int total)
        {
            return total <= 0 ? 0 : 100.0 * value / total;
        }

        static string FormatDb(double db)
        {
            return Math.Abs(db) < .05 ? "0.0 dB" : (db > 0 ? "+" : "") + db.ToString("N1") + " dB";
        }

        static int SeedFor(string text)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in text)
                    hash = hash * 31 + c;
                return hash;
            }
        }

        static void SaveState(AppState state)
        {
            File.WriteAllLines(StatePath, new[]
            {
                "db=" + state.Db.ToString("R", CultureInfo.InvariantCulture),
                "mode=" + state.Mode
            });
        }

        static AppState LoadState()
        {
            var state = new AppState();
            if (!File.Exists(StatePath))
                return state;

            try
            {
                foreach (string line in File.ReadAllLines(StatePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (key.Equals("db", StringComparison.OrdinalIgnoreCase))
                    {
                        double db;
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out db))
                            state.Db = db;
                    }
                    else if (key.Equals("mode", StringComparison.OrdinalIgnoreCase))
                    {
                        DspMode mode;
                        if (Enum.TryParse(value, true, out mode))
                            state.Mode = mode;
                    }
                }
            }
            catch { }
            return state;
        }

        static bool IsDarkMode()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
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
                app.Run(new MainWindow());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Touch Keyboard Audio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
