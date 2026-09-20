// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsServicesSearch.Commands;

internal static class ElevatedServiceLauncher
{
    private const int ConnectionTimeoutMilliseconds = 30000;

    public static void Start(string serviceName)
    {
        HelperDiagnostics.Write("Launcher: open-service command invoked.");
        using var consoleProcess = StartServicesConsole();
        var pipeName = $"WindowsServicesSearch.{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var cancellation = new CancellationTokenSource(ConnectionTimeoutMilliseconds);
        _ = SendRequestAsync(server, cancellation, serviceName, consoleProcess.Id);

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new InvalidOperationException("The extension executable path is unavailable.");
            }

            HelperDiagnostics.Write($"Launcher: requesting elevated helper: {executablePath}");
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--elevated-service-helper {pipeName}",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Exception exception)
        {
            HelperDiagnostics.Write($"Launcher: elevated helper failed to start: {exception.Message}");
            cancellation.Cancel();
            server.Dispose();
            cancellation.Dispose();
            throw;
        }
    }

    private static Process StartServicesConsole()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "mmc.exe"),
            Arguments = $"\"{Path.Combine(Environment.SystemDirectory, "services.msc")}\"",
            UseShellExecute = true,
        });

        if (process is null)
        {
            throw new InvalidOperationException("Services could not be started.");
        }

        HelperDiagnostics.Write($"Launcher: services.msc started with process ID {process.Id}.");
        return process;
    }

    private static async Task SendRequestAsync(
        NamedPipeServerStream server,
        CancellationTokenSource cancellation,
        string serviceName,
        int processId)
    {
        try
        {
            await server.WaitForConnectionAsync(cancellation.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(serviceName).ConfigureAwait(false);
            await writer.WriteLineAsync(processId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
            HelperDiagnostics.Write("Launcher: service name and MMC process ID sent to elevated helper.");
        }
        catch (OperationCanceledException)
        {
            HelperDiagnostics.Write("Launcher: helper connection timed out.");
        }
        catch (ObjectDisposedException)
        {
            HelperDiagnostics.Write("Launcher: pipe was disposed before connection.");
        }
        finally
        {
            server.Dispose();
            cancellation.Dispose();
        }
    }
}
