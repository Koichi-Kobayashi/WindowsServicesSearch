// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using WindowsServicesSearch.Models;

namespace WindowsServicesSearch.Commands;

internal static class ElevatedServiceHelper
{
    private const int ConnectionTimeoutMilliseconds = 30000;

    public static void Run(string pipeName)
    {
        HelperDiagnostics.Write("Helper: elevated helper started.");
        if (!IsElevated() || !IsValidPipeName(pipeName))
        {
            HelperDiagnostics.Write("Helper: elevation or pipe-name validation failed.");
            return;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
            client.Connect(ConnectionTimeoutMilliseconds);
            using var reader = new StreamReader(client);
            var serviceName = reader.ReadLine();
            var processIdText = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                HelperDiagnostics.Write("Helper: service name was missing.");
                return;
            }

            if (!int.TryParse(processIdText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var processId))
            {
                HelperDiagnostics.Write($"Helper: MMC process ID was invalid: {processIdText}");
                return;
            }

            if (ServiceManagerReader.TryGetDisplayName(serviceName, out var displayName))
            {
                HelperDiagnostics.Write($"Helper: navigating to service {serviceName} in MMC process {processId}.");
                ServicesConsoleNavigator.NavigateAndWait(processId, displayName);
            }
            else
            {
                HelperDiagnostics.Write($"Helper: service name could not be resolved: {serviceName}");
            }
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            HelperDiagnostics.Write($"Helper: pipe operation failed: {exception.Message}");
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsValidPipeName(string pipeName)
    {
        const string prefix = "WindowsServicesSearch.";
        return pipeName.StartsWith(prefix, StringComparison.Ordinal) && pipeName.Length == prefix.Length + 32;
    }
}
