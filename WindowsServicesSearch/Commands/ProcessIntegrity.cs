// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System.Security.Principal;

namespace WindowsServicesSearch.Commands;

/// <summary>
/// Checks this extension process's token, rather than assuming it inherits the
/// Command Palette host's integrity level.
/// </summary>
internal static class ProcessIntegrity
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
