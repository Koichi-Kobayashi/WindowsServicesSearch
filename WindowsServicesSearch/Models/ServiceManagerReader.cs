// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using WindowsServicesSearch.Resources;

namespace WindowsServicesSearch.Models;

internal static partial class ServiceManagerReader
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerEnumerateService = 0x0004;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceWin32 = 0x00000030;
    private const uint ServiceStateAll = 0x00000003;

    private const int ScEnumProcessInfo = 0;
    private const uint ServiceConfigDescription = 1;
    private const int ErrorMoreData = 234;

    public static IReadOnlyList<ServiceItem> ReadServices()
    {
        using var manager = OpenSCManager(null, null, ScManagerConnect | ScManagerEnumerateService);
        if (manager.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _ = EnumServicesStatusEx(manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll, IntPtr.Zero, 0, out var required, out _, IntPtr.Zero, null);
        if (Marshal.GetLastWin32Error() != ErrorMoreData || required == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!EnumServicesStatusEx(manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll, buffer, required, out _, out var count, IntPtr.Zero, null))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var services = new List<ServiceItem>(checked((int)count));
            var itemSize = Marshal.SizeOf<EnumServiceStatusProcess>();
            for (var index = 0; index < count; index++)
            {
                var native = Marshal.PtrToStructure<EnumServiceStatusProcess>(IntPtr.Add(buffer, checked((int)index * itemSize)));
                var serviceName = Marshal.PtrToStringUni(native.ServiceName) ?? string.Empty;
                var displayName = ExpandIndirectString(Marshal.PtrToStringUni(native.DisplayName) ?? serviceName);
                var (description, startType) = ReadConfiguration(manager, serviceName);
                services.Add(new ServiceItem(displayName, serviceName, description, GetStatus(native.Status.CurrentState), startType));
            }

            services.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayName, right.DisplayName));
            return services;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool TryGetDisplayName(string serviceName, [NotNullWhen(true)] out string? displayName)
    {
        using var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager.IsInvalid)
        {
            displayName = null;
            return false;
        }

        using var service = OpenService(manager, serviceName, ServiceQueryConfig);
        if (service.IsInvalid)
        {
            displayName = null;
            return false;
        }

        _ = QueryServiceConfig(service, IntPtr.Zero, 0, out var configSize);
        if (configSize == 0)
        {
            displayName = null;
            return false;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)configSize));
        try
        {
            if (!QueryServiceConfig(service, buffer, configSize, out _))
            {
                displayName = null;
                return false;
            }

            var config = Marshal.PtrToStructure<QueryServiceConfigData>(buffer);
            displayName = ExpandIndirectString(Marshal.PtrToStringUni(config.DisplayName) ?? serviceName);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (string Description, string StartType) ReadConfiguration(SafeServiceHandle manager, string serviceName)
    {
        using var service = OpenService(manager, serviceName, ServiceQueryConfig);
        if (service.IsInvalid)
        {
            return (string.Empty, Strings.Get("StartType.Unknown"));
        }

        _ = QueryServiceConfig(service, IntPtr.Zero, 0, out var configSize);
        var configBuffer = Marshal.AllocHGlobal(checked((int)configSize));
        try
        {
            var startType = Strings.Get("StartType.Unknown");
            if (QueryServiceConfig(service, configBuffer, configSize, out _))
            {
                startType = GetStartType(Marshal.PtrToStructure<QueryServiceConfigData>(configBuffer).StartType);
            }

            _ = QueryServiceConfig2(service, ServiceConfigDescription, IntPtr.Zero, 0, out var descriptionSize);
            if (descriptionSize == 0)
            {
                return (string.Empty, startType);
            }

            var descriptionBuffer = Marshal.AllocHGlobal(checked((int)descriptionSize));
            try
            {
                if (!QueryServiceConfig2(service, ServiceConfigDescription, descriptionBuffer, descriptionSize, out _))
                {
                    return (string.Empty, startType);
                }

                var descriptionPointer = Marshal.ReadIntPtr(descriptionBuffer);
                var description = Marshal.PtrToStringUni(descriptionPointer) ?? string.Empty;
                return (ExpandIndirectString(description), startType);
            }
            finally
            {
                Marshal.FreeHGlobal(descriptionBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(configBuffer);
        }
    }

    private static string ExpandIndirectString(string value)
    {
        if (!value.StartsWith('@'))
        {
            return value;
        }

        var result = new StringBuilder(32768);
        return SHLoadIndirectString(value, result, (uint)result.Capacity, IntPtr.Zero) == 0 ? result.ToString() : value;
    }

    private static string GetStatus(uint value) => value switch
    {
        1 => Strings.Get("Status.Stopped"),
        2 => Strings.Get("Status.StartPending"),
        3 => Strings.Get("Status.StopPending"),
        4 => Strings.Get("Status.Running"),
        5 => Strings.Get("Status.ContinuePending"),
        6 => Strings.Get("Status.PausePending"),
        7 => Strings.Get("Status.Paused"),
        _ => Strings.Get("Status.Unknown"),
    };

    private static string GetStartType(uint value) => value switch
    {
        0 => Strings.Get("StartType.Boot"),
        1 => Strings.Get("StartType.System"),
        2 => Strings.Get("StartType.Automatic"),
        3 => Strings.Get("StartType.Manual"),
        4 => Strings.Get("StartType.Disabled"),
        _ => Strings.Get("StartType.Unknown"),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatusProcess Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryServiceConfigData
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    private sealed partial class SafeServiceHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(SafeServiceHandle manager, int infoLevel, uint serviceType, uint serviceState, IntPtr services, uint bufferSize, out uint bytesNeeded, out uint servicesReturned, IntPtr resumeHandle, string? groupName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(SafeServiceHandle service, IntPtr config, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig2(SafeServiceHandle service, uint infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);

    [SuppressMessage("Performance", "CA1838:Avoid StringBuilder parameters for P/Invokes", Justification = "The API writes a variable-length localized string and this call is made only while building the cache.")]
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, uint outputSize, IntPtr reserved);
}
