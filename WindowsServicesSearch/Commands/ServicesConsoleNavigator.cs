// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using Accessibility;

namespace WindowsServicesSearch.Commands;

internal static class ServicesConsoleNavigator
{
    private const int TimeoutMilliseconds = 15000;
    private const int PollMilliseconds = 100;
    private const int DoubleClickIntervalMilliseconds = 75;
    private const int MaximumScrollAttempts = 200;
    private const uint ObjidClient = 0xFFFFFFFC;
    private const int SelFlagTakeFocus = 0x1;
    private const int SelFlagTakeSelection = 0x2;
    private const uint LvmFirst = 0x1000;
    private const uint LvmEnsureVisible = LvmFirst + 19;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const int VkReturn = 0x0D;

    public static void NavigateAndWait(int processId, string displayName)
    {
        var worker = new Thread(() => NavigateOnStaThread(processId, displayName))
        {
            IsBackground = true,
            Name = "Navigate Windows Services",
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        worker.Join();
    }

    private static void NavigateOnStaThread(int processId, string displayName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var window = WaitForMainWindow(process);
            if (window is null)
            {
                HelperDiagnostics.Write("Navigator: MMC main window was not found before timeout.");
                return;
            }

            if (TryOpenWithMsaa(process.MainWindowHandle, displayName))
            {
                HelperDiagnostics.Write("Navigator: service opened through MSAA.");
                return;
            }

            var item = WaitForServiceItem(window, displayName);
            if (item is null)
            {
                HelperDiagnostics.Write($"Navigator: service item was not found: {displayName}");
                return;
            }

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
                HelperDiagnostics.Write("Navigator: service item invoked with InvokePattern.");
                return;
            }

            item.SetFocus();
            var point = item.GetClickablePoint();
            DoubleClickAt((int)point.X, (int)point.Y);
            HelperDiagnostics.Write("Navigator: service name cell opened with a double-click fallback.");
        }
        catch (ElementNotAvailableException exception)
        {
            HelperDiagnostics.Write($"Navigator: UIA element disappeared: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            HelperDiagnostics.Write($"Navigator: operation failed: {exception.Message}");
        }
    }

#pragma warning disable IL2050, CsWinRT1033 // MSAA is required for the classic cross-process Services list view.
    private static bool TryOpenWithMsaa(IntPtr rootWindow, string displayName)
    {
        foreach (var listView in FindListViews(rootWindow))
        {
            var iid = typeof(IAccessible).GUID;
            if (AccessibleObjectFromWindow(listView, ObjidClient, ref iid, out var accessibleObject) != 0 ||
                accessibleObject is not IAccessible accessible)
            {
                continue;
            }

            for (var childId = 1; childId <= accessible.accChildCount; childId++)
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

                if (!string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                accessible.accSelect(SelFlagTakeFocus | SelFlagTakeSelection, child);
                _ = SendMessage(listView, LvmEnsureVisible, new IntPtr(childId - 1), IntPtr.Zero);
                Thread.Sleep(PollMilliseconds);
                _ = SetForegroundWindow(rootWindow);

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

                _ = PostMessage(listView, WmKeyDown, new IntPtr(VkReturn), IntPtr.Zero);
                _ = PostMessage(listView, WmKeyUp, new IntPtr(VkReturn), new IntPtr(unchecked((int)0xC0000001)));
                HelperDiagnostics.Write("Navigator: service item opened with the Enter-key fallback.");
                return true;
            }
        }

        return false;
    }
#pragma warning restore IL2050, CsWinRT1033

    private static List<IntPtr> FindListViews(IntPtr rootWindow)
    {
        var listViews = new List<IntPtr>();
        _ = EnumChildWindows(rootWindow, (window, _) =>
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

    private static AutomationElement? WaitForMainWindow(Process process)
    {
        for (var attempt = 0; attempt < TimeoutMilliseconds / PollMilliseconds; attempt++)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return AutomationElement.FromHandle(process.MainWindowHandle);
            }

            Thread.Sleep(PollMilliseconds);
        }

        return null;
    }

    private static AutomationElement? WaitForServiceItem(AutomationElement root, string displayName)
    {
        for (var attempt = 0; attempt < TimeoutMilliseconds / PollMilliseconds; attempt++)
        {
            var item = FindVisibleServiceItem(root, displayName);
            if (item is not null)
            {
                return item;
            }

            var list = FindScrollableServiceList(root);
            if (list is not null)
            {
                return FindByScrolling(root, list, displayName);
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
        var nameCell = root.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.NameProperty, displayName)));
        if (nameCell is not null)
        {
            return nameCell;
        }

        var itemCondition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        var items = root.FindAll(TreeScope.Descendants, itemCondition);
        foreach (AutomationElement item in items)
        {
            if (string.Equals(item.Current.Name, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static void DoubleClickAt(int x, int y)
    {
        var hasOriginalPosition = GetCursorPos(out var originalPosition);
        try
        {
            _ = SetCursorPos(x, y);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(DoubleClickIntervalMilliseconds);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        }
        finally
        {
            if (hasOriginalPosition)
            {
                _ = SetCursorPos(originalPosition.X, originalPosition.Y);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsCallback callback, IntPtr parameter);

    [SuppressMessage("Performance", "CA1838:Avoid StringBuilder parameters for P/Invokes", Justification = "The fixed-size class-name buffer is used only to locate the Services list view.")]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr window,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object accessibleObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
}
