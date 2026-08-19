using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace TouchKeyboardAudio.Uwp
{
    public sealed partial class MainPage : Page
    {
        const double DefaultDb = 20.0;
        const string LastDbKey = "LastDb";
        const string LastOperationKey = "LastOperation";

        public MainPage()
        {
            InitializeComponent();
            ConfigureTitleBar();

            object saved = ApplicationData.Current.LocalSettings.Values[LastDbKey];
            double initial = saved == null
                ? DefaultDb
                : Convert.ToDouble(saved, CultureInfo.InvariantCulture);

            GainSlider.Value = Math.Max(
                GainSlider.Minimum,
                Math.Min(GainSlider.Maximum, initial));

            RestorePersistedStatus(saved, initial);
            UpdatePreview();

            Loaded += async delegate
            {
                await DeleteBridgeFileIfPresentAsync("request.txt");
                await DeleteBridgeFileIfPresentAsync("response.txt");
            };
        }

        void ConfigureTitleBar()
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            ApplicationViewTitleBar titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            Window.Current.SetTitleBar(DragRegion);
        }

        void RestorePersistedStatus(object saved, double db)
        {
            object operationRaw = ApplicationData.Current.LocalSettings.Values[LastOperationKey];
            string operation = operationRaw as string;

            if (string.Equals(operation, "apply", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "已应用 " + FormatDb(db);
                return;
            }

            if (string.Equals(operation, "restore", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "已恢复微软原版 PCM16";
                return;
            }

            // Compatibility with builds that only persisted LastDb.
            if (saved != null)
            {
                StatusText.Text = Math.Abs(db) < .05
                    ? "上次记录：0.0 dB"
                    : "已应用 " + FormatDb(db);
                return;
            }

            StatusText.Text = "尚未应用";
        }

        void GainSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            UpdatePreview();
        }

        void UpdatePreview()
        {
            if (DbText == null || GainText == null || EstimateText == null)
                return;

            double db = Math.Round(GainSlider.Value * 2.0) / 2.0;
            double gain = Math.Pow(10.0, db / 20.0);

            DbText.Text = FormatDb(db);
            GainText.Text = gain.ToString("N2", CultureInfo.CurrentCulture) + "×";
            EstimateText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "按 TextInput 2% 内部增益估算，后级相对倍率约 {0:F3}×",
                gain * 0.02);
        }

        async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            double db = Math.Round(GainSlider.Value * 2.0) / 2.0;
            await RunBackendAsync("apply", "Apply", db);
        }

        async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            await RunBackendAsync("restore", "Restore", 0.0);
        }

        async Task RunBackendAsync(string command, string parameterGroup, double db)
        {
            SetBusy(true);
            string token = Guid.NewGuid().ToString("N");

            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                StorageFile request = await folder.CreateFileAsync(
                    "request.txt",
                    CreationCollisionOption.ReplaceExisting);

                await FileIO.WriteLinesAsync(
                    request,
                    new[]
                    {
                        command,
                        db.ToString("R", CultureInfo.InvariantCulture),
                        token
                    });

                await DeleteBridgeFileIfPresentAsync("response.txt");

                StatusText.Text = "等待管理员后端……";
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync(parameterGroup);

                BackendResponse response = await WaitForResponseAsync(folder, token);
                if (!response.Ok)
                    throw new InvalidOperationException(response.Message);

                ApplicationData.Current.LocalSettings.Values[LastDbKey] = response.Db;
                ApplicationData.Current.LocalSettings.Values[LastOperationKey] = command;

                if (command == "restore")
                    GainSlider.Value = 0;

                StatusText.Text = response.Message;
            }
            catch (Exception ex)
            {
                StatusText.Text = "操作失败：" + ex.Message;
            }
            finally
            {
                await DeleteBridgeFileIfPresentAsync("request.txt");
                await DeleteBridgeFileIfPresentAsync("response.txt");
                SetBusy(false);
                UpdatePreview();
            }
        }

        async Task<BackendResponse> WaitForResponseAsync(StorageFolder folder, string token)
        {
            for (int i = 0; i < 360; i++)
            {
                try
                {
                    StorageFile responseFile = await folder.GetFileAsync("response.txt");
                    IList<string> lines = await FileIO.ReadLinesAsync(responseFile);

                    if (lines.Count >= 4 && string.Equals(lines[0], token, StringComparison.Ordinal))
                    {
                        double db;
                        double.TryParse(
                            lines[2],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out db);

                        return new BackendResponse
                        {
                            Ok = string.Equals(lines[1], "ok", StringComparison.OrdinalIgnoreCase),
                            Db = db,
                            Message = lines[3]
                        };
                    }
                }
                catch (FileNotFoundException) { }

                await Task.Delay(250);
            }

            throw new TimeoutException("管理员后端没有返回结果。");
        }

        async Task DeleteBridgeFileIfPresentAsync(string name)
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(name);
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch (FileNotFoundException) { }
            catch (UnauthorizedAccessException) { }
        }

        void SetBusy(bool busy)
        {
            ApplyButton.IsEnabled = !busy;
            RestoreButton.IsEnabled = !busy;
            GainSlider.IsEnabled = !busy;
            BusyRing.IsActive = busy;
            BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        static string FormatDb(double db)
        {
            return Math.Abs(db) < .05
                ? "0.0 dB"
                : (db > 0 ? "+" : "") + db.ToString("N1", CultureInfo.CurrentCulture) + " dB";
        }

        sealed class BackendResponse
        {
            public bool Ok;
            public double Db;
            public string Message;
        }
    }
}
