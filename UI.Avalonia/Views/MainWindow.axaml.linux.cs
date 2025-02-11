#if !WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Threading;
using IrdLibraryClient;
using UI.Avalonia.Utils;
using UI.Avalonia.ViewModels;

namespace UI.Avalonia.Views;

public partial class MainWindow
{
    private Thread? dmesgMonitorThread;
    private bool closing;
    
    partial void OnLoadedPlatform()
    {
        if (!OperatingSystem.IsLinux())
            return;
        
#pragma warning disable CA1416
        dmesgMonitorThread = new(MonitorDmesg);
#pragma warning restore CA1416
        dmesgMonitorThread.Start();
    }

    partial void OnClosingPlatform()
    {
        if (!OperatingSystem.IsLinux())
            return;
        
        closing = true;
    }


    [SupportedOSPlatform("Linux")]
    private void MonitorDmesg()
    {
        try
        {
            using var stream = File.Open("/dev/kmsg", new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            using var reader = new StreamReader(stream);
            while (!closing)
            {
                Thread.Yield();
                if (reader.ReadLine() is { Length: > 0 } line)
                {
                    if (line.Contains("UDF-fs: INFO Mounting volume"))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (DataContext is MainWindowViewModel
                                {
                                    CurrentPage: MainViewModel
                                    {
                                        DumpingInProgress: false
                                    } vm and not
                                    {
                                        // still scanning
                                        FoundDisc: true,
                                        DumperIsReady: false
                                    }
                                })
                            {
                                Log.Debug($"Found udf mount message, trying to scan disc drives… ({nameof(vm.DumpingInProgress)}: {vm.DumpingInProgress}, {nameof(vm.FoundDisc)}: {vm.FoundDisc}, {nameof(vm.DumperIsReady)}: {vm.DumperIsReady})");
                                vm.ScanDiscsCommand.Execute(null);
                            }
                        }, DispatcherPriority.Background);
                    }
                    else if (line.Contains("busy inodes on changed media sr"))
                    {
                        var match = Regex.Match(line, @"media (?<device>sr\d+)", RegexOptions.Singleline | RegexOptions.ExplicitCapture);
                        if (match.Success && match.Groups["device"].Value is { Length: > 0 } mdn)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (DataContext is MainWindowViewModel
                                    {
                                        CurrentPage: MainViewModel
                                        {
                                            dumper: { SelectedPhysicalDevice: { Length: > 0 } spd } dumper
                                        } vm
                                    }
                                    && spd == $"/dev/{mdn}")
                                {
                                    dumper.Cts.Cancel();
                                    vm.ResetViewModelCommand.Execute(null);
                                }
                            }, DispatcherPriority.Background);
                        }
                    }
                    Thread.Yield();
                }
                else if (!closing)
                    Thread.Sleep(100);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to open dmesg");
        }
    }
}
#endif