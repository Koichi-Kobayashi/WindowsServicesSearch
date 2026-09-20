// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WindowsServicesSearch.Models;
using WindowsServicesSearch.Resources;

namespace WindowsServicesSearch.Commands;

internal sealed partial class OpenServiceCommand(ServiceItem service) : InvokableCommand
{
    private static readonly object InvocationGate = new();
    private static readonly TimeSpan DuplicateInvocationWindow = TimeSpan.FromSeconds(5);
    private static string? _lastServiceName;
    private static DateTimeOffset _lastInvocation;

    public override string Name => Strings.Get("Command.OpenServiceProperties");

    public override CommandResult Invoke()
    {
        lock (InvocationGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (string.Equals(_lastServiceName, service.ServiceName, StringComparison.OrdinalIgnoreCase) &&
                now - _lastInvocation < DuplicateInvocationWindow)
            {
                return CommandResult.Hide();
            }

            _lastServiceName = service.ServiceName;
            _lastInvocation = now;
        }

        try
        {
            ElevatedServiceLauncher.Start(service.ServiceName);
            return CommandResult.Hide();
        }
        catch (Win32Exception)
        {
            return CommandResult.ShowToast(Strings.Get("Error.ServicesCouldNotOpen"));
        }
        catch (InvalidOperationException)
        {
            return CommandResult.ShowToast(Strings.Get("Error.ServicePropertiesCouldNotOpen"));
        }
    }
}
