using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace Taskboard.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--ai-hook" && args[1] == "claude")
        {
            var index = Array.IndexOf(args, "--ai-events-root");
            return ClaudeHook.Run(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
        }
        var startupInvocation = false;
        var resetOnboarding = false;
        var data = DataPaths.DefaultDataDirectory;
        var isolated = false; var hasDataDirectory = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data-dir" && i + 1 < args.Length) { data = Path.GetFullPath(args[++i]); isolated = true; hasDataDirectory = true; }
            else if (args[i] == "--no-integrations") isolated = true;
            else if (args[i] == "--startup") startupInvocation = true;
            else if (args[i] == "--reset-onboarding") resetOnboarding = true;
            else { Console.Error.WriteLine("不支持的启动参数：" + args[i]); return 2; }
        }
        if (!hasDataDirectory)
        {
            if (!isolated)
            {
                try
                {
                    var latest = StartupLinks.RegisterCurrent();
                    if (!string.Equals(latest, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    { Process.Start(new ProcessStartInfo(latest) { WorkingDirectory = Path.GetDirectoryName(latest)!, UseShellExecute = false }); return 0; }
                }
                catch (Exception e) { MessageBox.Show("程序版本登记失败：" + e.Message, Brand.Name); return 1; }
            }
            try
            {
                if (!isolated && DataPaths.NeedsLegacyChoice())
                {
                    var choice = MessageBox.Show("默认在系统用户目录保存任务和配置。如果从旧版升级，请选择原data目录，复制后原件保留。是否现在选择原数据？\n首次使用请选择‘否’。", Brand.Name + "首次保存路径", MessageBoxButton.YesNoCancel);
                    if (choice == MessageBoxResult.Cancel) return 0;
                    if (choice == MessageBoxResult.Yes)
                    {
                        var legacy = new Microsoft.Win32.OpenFolderDialog { Title = "选择旧版原任务数据目录" }; if (legacy.ShowDialog() != true) return 0;
                        data = DataPaths.MigrateSelectedDirectory(legacy.FolderName, DataPaths.SystemRoot);
                    }
                    else data = DataPaths.DefaultDirectory();
                }
                else data = DataPaths.DefaultDirectory();
            }
            catch (DirectoryNotFoundException e)
            {
                if (MessageBox.Show(e.Message + "\n是否选择原数据的新位置？取消会退出，不创建空数据。", Brand.Name, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return 1;
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择原任务数据所在目录" };
                if (dialog.ShowDialog() != true) return 1;
                data = Path.GetFullPath(dialog.FolderName);
                if (!File.Exists(Path.Combine(data, "board.json")) && !File.Exists(Path.Combine(data, "board.json.bak"))) { MessageBox.Show("所选目录没有原任务数据，程序不会建立空数据替代。", Brand.Name); return 1; }
                try { Directory.CreateDirectory(Path.Combine(DataPaths.SystemRoot, "config")); DataPaths.Save(DataPaths.SelectionFile, data, Path.Combine(DataPaths.SystemRoot, "config")); }
                catch (Exception error) { MessageBox.Show(error.Message, Brand.Name); return 1; }
            }
            catch (Exception e) { MessageBox.Show(e.Message, Brand.Name); return 1; }
        }
        if (resetOnboarding)
        {
            var deviceDirectory = isolated ? Path.Combine(data, "device") : Path.Combine(DataPaths.SystemRoot, "device");
            var (ok, error) = new DeviceOnboarding(deviceDirectory, StartupLinks.MachineIdentity()).Reset();
            if (!ok && !isolated) MessageBox.Show("新手指引状态未能重置：" + error, Brand.Name);
        }
        using var mutex = DataPaths.Lock(data, out var acquired);
        if (!acquired) { MessageBox.Show($"这一份任务数据已经打开，请从托盘找到{Brand.Name}。"); return 0; }
        var configurationAcquired = true;
        using var configurationMutex = isolated ? null : DataPaths.Lock(DataPaths.ConfigurationDirectory(), out configurationAcquired);
        if (!configurationAcquired) { MessageBox.Show("程序配置已由另一份任务看板打开，请从托盘找到已运行的程序。"); return 0; }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline),new FrameworkPropertyMetadata(120));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) });
        BoardController? controller = null;
        application.Startup += async (_, _) =>
        {
            try
            {
                controller = new BoardController(data, isolated, false); await controller.Start();
                if (startupInvocation && !isolated) File.WriteAllText(Path.Combine(DataPaths.SystemRoot, "last-startup.json"), System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, version = typeof(Program).Assembly.GetName().Version!.ToString(), executable = Environment.ProcessPath }));

            }
            catch (Exception e)
            {
                Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "startup-error.txt"), e.ToString());
                Ui.Error(Brand.Name + "不能启动：" + e.Message);
                controller?.Dispose(); application.Shutdown(1);
            }
        };
        application.DispatcherUnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(data); File.AppendAllText(Path.Combine(data, "errors.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
            Ui.Error("操作失败，未提交内容仍在窗口中：" + e.Exception.Message);
            e.Handled = true;
        };
        application.SessionEnding += (_, e) =>
        {
            // Windows logout is not a normal app exit: flush without waiting on this UI dispatcher.
            if (controller is null) return;
            foreach (var column in controller.Columns) column.PrepareHide();
            foreach (var editor in controller.Editors) editor.PrepareHide();
            try { controller.Store.SaveAsync(Taskboard.Core.Copies.Of(controller.Engine.State)).GetAwaiter().GetResult(); }
            catch (Exception error) { e.Cancel = true; Ui.Error("退出前保存失败：" + error.Message); }
            var soundErrors = controller.Listening.RestoreOriginals();
            if (soundErrors.Length > 0) { e.Cancel = true; Ui.Error(string.Join("；", soundErrors)); }
        };
        try { return application.Run(); }
        finally { mutex.ReleaseMutex(); }
    }
}




