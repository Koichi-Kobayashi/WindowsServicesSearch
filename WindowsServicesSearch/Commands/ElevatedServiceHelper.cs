// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Pipes;
using WindowsServicesSearch.Models;

namespace WindowsServicesSearch.Commands;

/// <summary>
/// The UAC-elevated half of the service-opening flow.
/// </summary>
internal static class ElevatedServiceHelper
{
    private const int ConnectionTimeoutMilliseconds = 30000;

    public static void Run(string pipeName)
    {
        HelperDiagnostics.Write("Helper: process started.");
        try
        {
            if (!ProcessIntegrity.IsElevated || !IsValidPipeName(pipeName))
            {
                HelperDiagnostics.Write("Helper: rejected because elevation or pipe validation failed.");
                return;
            }
        }
        catch (Exception exception)
        {
            HelperDiagnostics.Write($"Helper: elevation check failed: {exception}");
            return;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
            client.Connect(ConnectionTimeoutMilliseconds);
            HelperDiagnostics.Write("Helper: connected to request pipe.");
            using var reader = new StreamReader(client);
            var serviceName = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(serviceName))
            {
                HelperDiagnostics.Write("Helper: service name was missing.");
                return;
            }

            if (ServiceManagerReader.TryGetDisplayName(serviceName, out var displayName))
            {
                HelperDiagnostics.Write($"Helper: service name accepted; starting MMC UI Automation for {serviceName} ({displayName}).");
                ServicesConsoleNavigator.NavigateAndWait(displayName);
                HelperDiagnostics.Write("Helper: MMC UI Automation completed.");
            }
            else
            {
                HelperDiagnostics.Write($"Helper: service name could not be resolved: {serviceName}");
            }
        }
        catch (IOException exception)
        {
            HelperDiagnostics.Write($"Helper: pipe I/O failed: {exception.Message}");
        }
        catch (TimeoutException exception)
        {
            HelperDiagnostics.Write($"Helper: pipe connection timed out: {exception.Message}");
        }
    }

    private static bool IsValidPipeName(string pipeName)
    {
        const string prefix = "WindowsServicesSearch.";
        return pipeName.StartsWith(prefix, StringComparison.Ordinal) &&
               pipeName.Length == prefix.Length + 32;
    }
}
