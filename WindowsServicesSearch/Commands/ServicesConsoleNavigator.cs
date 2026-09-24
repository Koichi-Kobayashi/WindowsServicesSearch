// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using Accessibility;

namespace WindowsServicesSearch.Commands;

/// <summary>
/// Direct UIA navigator. It is called only from an elevated extension process,
/// so it has the same integrity level as the MMC process it starts.
/// </summary>
internal static class ServicesConsoleNavigator
{
    private const int TimeoutMilliseconds = 15000;
    private const int PollMilliseconds = 100;
    private const int MaximumScrollAttempts = 200;
    private const uint ObjidClient = 0xFFFFFFFC;
    private const int SelFlagTakeFocus = 0x1;
    private const int SelFlagTakeSelection = 0x2;
    private const uint LvmFirst = 0x1000;
    private const uint LvmGetItemCount = LvmFirst + 4;
    private const uint LvmEnsureVisible = LvmFirst + 19;
    private const byte VkReturn = 0x0D;
    private const uint KeyeventfKeyup = 0x0002;

    public static void NavigateAndWait(string displayName)
    {
        var process = StartConsole();
        var worker = new Thread(() => NavigateOnStaThread(process, displayName))
        {
            IsBackground = true,
            Name = "Navigate Windows Services",
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        worker.Join();
    }

    private static Process StartConsole()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "mmc.exe"),
            Arguments = $"\"{Path.Combine(Environment.SystemDirectory, "services.msc")}\"",
            UseShellExecute = true,
        });

        return process ?? throw new InvalidOperationException("Services could not be started.");
    }

    private static void NavigateOnStaThread(Process process, string displayName)
    {
        using (process)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var windowHandle = WaitForMainWindowHandle(process);
                if (windowHandle == IntPtr.Zero)
                {
                    HelperDiagnostics.Write("Navigator: MMC main window was not found before timeout.");
                    return;
                }

                var listView = WaitForServiceListView(process.Id);
                if (listView == IntPtr.Zero)
                {
                    HelperDiagnostics.Write("Navigator: Services list view was not found before timeout.");
                    return;
                }

                HelperDiagnostics.Write($"Navigator: MMC window ready after {stopwatch.ElapsedMilliseconds} ms.");

                if (TryOpenWithMsaa(windowHandle, listView, displayName))
                {
                    HelperDiagnostics.Write($"Navigator: invoked properties after {stopwatch.ElapsedMilliseconds} ms.");
                    return;
                }

                var window = WaitForAutomationElement(windowHandle);
                if (window is null)
                {
                    HelperDiagnostics.Write("Navigator: MMC UI Automation element was not available.");
                    return;
                }

                var item = WaitForServiceItem(window, displayName);
                if (item is null)
                {
                    HelperDiagnostics.Write($"Navigator: target ListItem was not found: {displayName}");
                    return;
                }

                HelperDiagnostics.Write($"Navigator: target ListItem found after {stopwatch.ElapsedMilliseconds} ms.");

                if (item.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scrollItemPattern))
                {
                    ((ScrollItemPattern)scrollItemPattern).ScrollIntoView();
                }

                if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPattern))
                {
                    ((SelectionItemPattern)selectionPattern).Select();
                }

                if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern))
                {
                    ((InvokePattern)invokePattern).Invoke();
                    HelperDiagnostics.Write($"Navigator: invoked properties after {stopwatch.ElapsedMilliseconds} ms.");
                    return;
                }

                HelperDiagnostics.Write("Navigator: target ListItem did not expose InvokePattern.");
                _ = SetForegroundWindow(windowHandle);
                item.SetFocus();
                SendEnter();
                HelperDiagnostics.Write($"Navigator: opened properties with Enter after {stopwatch.ElapsedMilliseconds} ms.");
            }
            // An automation failure leaves the newly opened console available for
            // the user; it never changes a service or destabilizes the extension.
            catch (ElementNotAvailableException exception)
            {
                HelperDiagnostics.Write($"Navigator: UIA element disappeared: {exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                HelperDiagnostics.Write($"Navigator: UIA operation failed: {exception.Message}");
            }
            catch (Win32Exception exception)
            {
                HelperDiagnostics.Write($"Navigator: Win32 operation failed: {exception.Message}");
            }
            catch (COMException exception)
            {
                HelperDiagnostics.Write($"Navigator: COM operation failed: {exception.Message}");
            }
        }
    }

#pragma warning disable IL2050, CsWinRT1033 // MSAA is required for the classic Services list view.
    private static bool TryOpenWithMsaa(IntPtr rootWindow, IntPtr listView, string displayName)
    {
        var iid = typeof(IAccessible).GUID;
        if (AccessibleObjectFromWindow(listView, ObjidClient, ref iid, out var accessibleObject) != 0 ||
            accessibleObject is not IAccessible accessible)
        {
            HelperDiagnostics.Write("Navigator: Services list view did not expose MSAA.");
            return false;
        }

        var childCount = accessible.accChildCount;
        for (var childId = 1; childId <= childCount; childId++)
        {
            object child = childId;
            string? name;
            try
            {
                name = accessible.get_accName(child);
            }
            catch (COMException)
            {
                continue;
            }

            if (!IsServiceRowName(name, displayName))
            {
                continue;
            }

            accessible.accSelect(SelFlagTakeFocus | SelFlagTakeSelection, child);
            _ = SendMessage(listView, LvmEnsureVisible, new IntPtr(childId - 1), IntPtr.Zero);
            Thread.Sleep(PollMilliseconds);
            BringToForeground(rootWindow);

            try
            {
                accessible.accDoDefaultAction(child);
                HelperDiagnostics.Write("Navigator: service item opened through the MSAA default action.");
                return true;
            }
            catch (COMException exception)
            {
                HelperDiagnostics.Write($"Navigator: MSAA default action was unavailable: {exception.Message}");
            }

            SendEnter();
            HelperDiagnostics.Write("Navigator: service item opened with the Enter-key fallback.");
            return true;
        }

        if (childCount > 0)
        {
            try
            {
                HelperDiagnostics.Write($"Navigator: MSAA did not match '{displayName}'. First row was '{accessible.get_accName(1)}' ({childCount} items).");
            }
            catch (COMException)
            {
                HelperDiagnostics.Write($"Navigator: MSAA did not match '{displayName}' ({childCount} items).");
            }
        }

        return false;
    }
#pragma warning restore IL2050, CsWinRT1033

    private static IntPtr WaitForMainWindowHandle(Process process)
    {
        for (var attempt = 0; attempt < TimeoutMilliseconds / PollMilliseconds; attempt++)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            Thread.Sleep(PollMilliseconds);
        }

        return IntPtr.Zero;
    }

    private static IntPtr WaitForServiceListView(int processId)
    {
        var bestListView = IntPtr.Zero;
        var bestCount = 0;
        for (var attempt = 0; attempt < TimeoutMilliseconds / PollMilliseconds; attempt++)
        {
            foreach (var listView in FindListViewsInProcess(processId))
            {
                var count = SendMessage(listView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (count > bestCount)
                {
                    bestCount = count;
                    bestListView = listView;
                }
            }

            if (bestCount > 0)
            {
                return bestListView;
            }

            Thread.Sleep(PollMilliseconds);
        }

        return IntPtr.Zero;
    }

    private static List<IntPtr> FindListViewsInProcess(int processId)
    {
        var listViews = new List<IntPtr>();

        _ = EnumWindows((window, parameter) =>
        {
            _ = GetWindowThreadProcessId(window, out uint windowProcessId);

            if (windowProcessId != (uint)processId)
            {
                return true;
            }

            var className = new StringBuilder(256);
            _ = GetClassName(window, className, className.Capacity);

            if (string.Equals(className.ToString(), "SysListView32", StringComparison.Ordinal))
            {
                listViews.Add(window);
            }

            listViews.AddRange(FindListViews(window));
            return true;
        }, IntPtr.Zero);

        return listViews;
    }

    private static List<IntPtr> FindListViews(IntPtr rootWindow)
    {
        var listViews = new List<IntPtr>();

        _ = EnumChildWindows(rootWindow, (window, parameter) =>
        {
            var className = new StringBuilder(256);
            _ = GetClassName(window, className, className.Capacity);

            if (string.Equals(className.ToString(), "SysListView32", StringComparison.Ordinal))
            {
                listViews.Add(window);
            }

            return true;
        }, IntPtr.Zero);

        return listViews;
    }

    private static AutomationElement? WaitForAutomationElement(IntPtr windowHandle)
    {
        for (var attempt = 0; attempt < TimeoutMilliseconds / PollMilliseconds; attempt++)
        {
            try
            {
                var element = AutomationElement.FromHandle(windowHandle);
                if (element is not null)
                {
                    return element;
                }
            }
            catch (ElementNotAvailableException)
            {
            }

            Thread.Sleep(PollMilliseconds);
        }

        return null;
    }

    private static AutomationElement? WaitForServiceItem(AutomationElement root, string displayName)
    {
        for (var attempt = 0; attempt < TimeoutMilliseconds / PollMilliseconds; attempt++)
        {
            try
            {
                var item = FindVisibleServiceItem(root, displayName);
                if (item is not null)
                {
                    return item;
                }

                var list = FindScrollableServiceList(root);
                if (list is not null)
                {
                    item = FindByScrolling(root, list, displayName);
                    if (item is not null)
                    {
                        return item;
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
            }

            Thread.Sleep(PollMilliseconds);
        }

        return null;
    }

    private static AutomationElement? FindScrollableServiceList(AutomationElement root)
    {
        var lists = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
        foreach (AutomationElement list in lists)
        {
            if (list.TryGetCurrentPattern(ScrollPattern.Pattern, out _))
            {
                return list;
            }
        }

        return null;
    }

    private static AutomationElement? FindByScrolling(AutomationElement root, AutomationElement list, string displayName)
    {
        if (!list.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern))
        {
            return null;
        }

        var scroll = (ScrollPattern)pattern;
        if (scroll.Current.VerticallyScrollable)
        {
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
            Thread.Sleep(PollMilliseconds);
        }

        for (var attempt = 0; attempt < MaximumScrollAttempts; attempt++)
        {
            var item = FindVisibleServiceItem(root, displayName);
            if (item is not null)
            {
                return item;
            }

            if (!scroll.Current.VerticallyScrollable || scroll.Current.VerticalScrollPercent >= 100)
            {
                return null;
            }

            var previousPosition = scroll.Current.VerticalScrollPercent;
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            Thread.Sleep(PollMilliseconds);
            if (scroll.Current.VerticalScrollPercent <= previousPosition)
            {
                return null;
            }
        }

        return null;
    }

    private static AutomationElement? FindVisibleServiceItem(AutomationElement root, string displayName)
    {
        var itemCondition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        var items = root.FindAll(TreeScope.Descendants, itemCondition);
        foreach (AutomationElement item in items)
        {
            if (IsServiceRowName(item.Current.Name, displayName))
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsServiceRowName(string? actual, string displayName)
    {
        if (string.IsNullOrEmpty(actual))
        {
            return false;
        }

        if (string.Equals(actual, displayName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!actual.StartsWith(displayName, StringComparison.OrdinalIgnoreCase) ||
            actual.Length == displayName.Length)
        {
            return false;
        }

        var next = actual[displayName.Length];
        return next is ' ' or '\t' or ',' or ';' or '|';
    }

    private static void BringToForeground(IntPtr window)
    {
        var currentThread = GetCurrentThreadId();
        var windowThread = GetWindowThreadProcessId(window, out _);
        if (currentThread != windowThread)
        {
            _ = AttachThreadInput(currentThread, windowThread, true);
        }

        _ = SetForegroundWindow(window);

        if (currentThread != windowThread)
        {
            _ = AttachThreadInput(currentThread, windowThread, false);
        }
    }

    private static void SendEnter()
    {
        keybd_event(VkReturn, 0, 0, UIntPtr.Zero);
        keybd_event(VkReturn, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsCallback callback, IntPtr parameter);

    [SuppressMessage("Performance", "CA1838:Avoid StringBuilder parameters for P/Invokes", Justification = "The fixed-size class-name buffer is used only to locate the Services list view.")]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attachId, uint attachToId, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr window,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object accessibleObject);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
}
