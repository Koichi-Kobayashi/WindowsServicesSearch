// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsServicesSearch.Commands;

/// <summary>
/// Starts the same packaged executable through UAC and sends a trusted service
/// name over a short-lived, per-request pipe. It never starts MMC itself, so the
/// elevated helper can open Services at the same integrity level it will automate.
/// </summary>
internal static class ElevatedServiceLauncher
{
    private const int ConnectionTimeoutMilliseconds = 30000;

    public static void Start(string serviceName)
    {
        var pipeName = $"WindowsServicesSearch.{Guid.NewGuid():N}";
        HelperDiagnostics.Write("Launcher: creating one-time request pipe.");
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var cancellation = new CancellationTokenSource(ConnectionTimeoutMilliseconds);

        _ = SendServiceNameAsync(server, cancellation, serviceName);

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new InvalidOperationException("The extension executable path is unavailable.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--elevated-service-helper {pipeName}",
                UseShellExecute = true,
                Verb = "runas",
            });
            HelperDiagnostics.Write("Launcher: elevated helper process requested.");
        }
        catch
        {
            HelperDiagnostics.Write("Launcher: elevated helper process was not started.");
            cancellation.Cancel();
            server.Dispose();
            cancellation.Dispose();
            throw;
        }
    }

    private static async Task SendServiceNameAsync(
        NamedPipeServerStream server,
        CancellationTokenSource cancellation,
        string serviceName)
    {
        try
        {
            await server.WaitForConnectionAsync(cancellation.Token).ConfigureAwait(false);
            HelperDiagnostics.Write("Launcher: helper connected to request pipe.");
            await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(serviceName).ConfigureAwait(false);
            HelperDiagnostics.Write("Launcher: service name sent to elevated helper.");
        }
        catch (OperationCanceledException)
        {
            HelperDiagnostics.Write("Launcher: pipe connection timed out or was cancelled.");
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
