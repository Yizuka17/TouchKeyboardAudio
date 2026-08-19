using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace TouchKeyboardAudioBridge
{
    static class PackagedBackend
    {
        const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFamilyName(
            ref uint packageFamilyNameLength,
            StringBuilder packageFamilyName);

        public static bool TryRun(string[] args)
        {
            string command = FindCommand(args);
            if (command == null)
                return false;

            CleanupOrphanedFloatTemps();

            string token = string.Empty;
            string responsePath = null;

            try
            {
                string localState = GetPackageLocalState();
                string requestPath = Path.Combine(localState, "request.txt");
                responsePath = Path.Combine(localState, "response.txt");

                string[] request = File.ReadAllLines(requestPath);
                if (request.Length < 3)
                    throw new InvalidOperationException("UWP 请求文件不完整。");

                string requestedCommand = request[0].Trim();
                token = request[2].Trim();

                if (!string.Equals(requestedCommand, command, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("UWP 请求与后端启动参数不一致。");

                var engine = new TouchKeyboardAudioFloat.MainWindow();

                if (string.Equals(command, "apply", StringComparison.OrdinalIgnoreCase))
                {
                    double db;
                    if (!double.TryParse(
                        request[1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out db))
                    {
                        throw new InvalidOperationException("无效的 dB 参数。");
                    }

                    db = Math.Max(-20.0, Math.Min(30.0, Math.Round(db * 2.0) / 2.0));
                    Invoke(engine, "ApplyFloatGain", Math.Pow(10.0, db / 20.0), db);
                    WriteResponse(responsePath, token, true, db, "已应用 " + FormatDb(db));
                }
                else
                {
                    Invoke(engine, "RestoreBaseline");
                    WriteResponse(responsePath, token, true, 0.0, "已恢复微软原版 PCM16");
                }
            }
            catch (Exception ex)
            {
                Exception actual = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;

                try
                {
                    if (responsePath == null)
                        responsePath = Path.Combine(GetPackageLocalState(), "response.txt");

                    WriteResponse(responsePath, token, false, 0.0, actual.Message);
                }
                catch { }
            }
            finally
            {
                CleanupOrphanedFloatTemps();
            }

            return true;
        }

        static void CleanupOrphanedFloatTemps()
        {
            try
            {
                string temp = Path.GetTempPath();
                if (!Directory.Exists(temp))
                    return;

                DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
                foreach (string path in Directory.GetFiles(temp, "TKA_float_*.wav", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                        if (lastWrite <= cutoff)
                            File.Delete(path);
                    }
                    catch { }
                }
            }
            catch { }
        }

        static string FindCommand(string[] args)
        {
            if (args == null)
                return null;

            foreach (string arg in args)
            {
                if (string.Equals(arg, "/apply", StringComparison.OrdinalIgnoreCase))
                    return "apply";
                if (string.Equals(arg, "/restore", StringComparison.OrdinalIgnoreCase))
                    return "restore";
            }

            return null;
        }

        static void Invoke(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, name);

            method.Invoke(target, args);
        }

        static string GetPackageLocalState()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string packagesRoot = Path.Combine(localAppData, "Packages");

            uint length = 0;
            int first = GetCurrentPackageFamilyName(ref length, null);
            if (first == ErrorInsufficientBuffer && length > 0)
            {
                var family = new StringBuilder((int)length);
                int second = GetCurrentPackageFamilyName(ref length, family);
                if (second == 0 && family.Length > 0)
                {
                    string path = Path.Combine(packagesRoot, family.ToString(), "LocalState");
                    Directory.CreateDirectory(path);
                    return path;
                }
            }

            if (Directory.Exists(packagesRoot))
            {
                string[] matches = Directory.GetDirectories(
                    packagesRoot,
                    "Yizuka17.TouchKeyboardAudio_*",
                    SearchOption.TopDirectoryOnly);

                if (matches.Length > 0)
                {
                    string path = Path.Combine(matches[0], "LocalState");
                    Directory.CreateDirectory(path);
                    return path;
                }
            }

            throw new DirectoryNotFoundException("找不到 TouchKeyboardAudio UWP LocalState。请从 UWP 前端启动操作。");
        }

        static void WriteResponse(
            string path,
            string token,
            bool ok,
            double db,
            string message)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(
                path,
                new[]
                {
                    token ?? string.Empty,
                    ok ? "ok" : "error",
                    db.ToString("R", CultureInfo.InvariantCulture),
                    (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ")
                },
                Encoding.UTF8);
        }

        static string FormatDb(double db)
        {
            return Math.Abs(db) < .05
                ? "0.0 dB"
                : (db > 0 ? "+" : "") + db.ToString("N1") + " dB";
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (PackagedBackend.TryRun(args))
                return;

            try
            {
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var window = new TouchKeyboardAudioFloat.MainWindow();
                TouchKeyboardAudioUwp.UwpShell.Apply(window);
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
