using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using HoodieCompanion.Platform;
using HoodieCompanion.Storage;
using HoodieCompanion.UI;

namespace HoodieCompanion;

public static class Program
{
    private const string MutexName = @"Local\HoodieCompanion.Instance.v1";
    private const string ActivateEventName = @"Local\HoodieCompanion.Activate.v1";

    [STAThread]
    public static int Main(string[] args)
    {
        DpiService.EnablePerMonitorV2();

        var dataRoot = Arg(args, "--data") ?? AppStorage.DefaultRoot();
        Directory.CreateDirectory(dataRoot);
        Log.Init(dataRoot);
        Log.Verbose = args.Contains("--verbose");

        var poseSheet = Arg(args, "--render-poses");
        if (poseSheet is not null)
        {
            var app0 = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Ui.LoadTheme(app0);
            PoseSheet.Render(poseSheet);
            return 0;
        }

        // Single instance: a second launch asks the running Hoodie to come forward and exits.
        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ActivateEventName);
                ev.Set();
            }
            catch
            {
                // The first instance is still starting; nothing else to do.
            }
            return 0;
        }

        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("unhandled UI exception", e.Exception);
            e.Handled = true; // stay alive; the companion must never take the desktop down with it
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("fatal exception", e.ExceptionObject as Exception);

        Ui.LoadTheme(app);
        var host = new AppHost(app, new AppStorage(dataRoot));

        var watcher = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    activate.WaitOne();
                }
                catch
                {
                    return;
                }
                app.Dispatcher.BeginInvoke(() =>
                {
                    host.SetEmergencyHidden(false);
                    host.Command(Companion.Behavior.PetCommand.ComeBack);
                    host.OpenPanel(PanelPage.Home);
                });
            }
        }) { IsBackground = true, Name = "activate-watcher" };
        watcher.Start();

        var qaDir = Arg(args, "--qa");
        if (qaDir is not null) host.Settings.FirstRunDone = true;
        app.Startup += (_, _) =>
        {
            host.Start();
            var qa = qaDir;
            if (qa is not null) new QaRunner(host, qa, args.Contains("--qa-keep")).Start();
        };
        var code = app.Run();
        host.Dispose();
        return code;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
