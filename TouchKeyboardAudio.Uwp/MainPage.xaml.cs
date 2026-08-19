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

        public MainPage()
        {
            InitializeComponent();
            ConfigureTitleBar();

            object saved = ApplicationData.Current.LocalSettings.Values["LastDb"];
            double initial = saved == null ? DefaultDb : Convert.ToDouble(saved, CultureInfo.InvariantCulture);
            GainSlider.Value = Math.Max(GainSlider.Minimum, Math.Min(GainSlider.Maximum, initial));
            UpdatePreview();
        }

        void ConfigureTitleBar()
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            ApplicationViewTitleBar titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            Window.Current.SetTitleBar(DragRegion);
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

                try
                {
                    StorageFile stale = await folder.GetFileAsync("response.txt");
                    await stale.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch (FileNotFoundException) { }

                StatusText.Text = "等待管理员后端……";
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync(parameterGroup);

                BackendResponse response = await WaitForResponseAsync(folder, token);
                if (!response.Ok)
                    throw new InvalidOperationException(response.Message);

                ApplicationData.Current.LocalSettings.Values["LastDb"] = response.Db;
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
