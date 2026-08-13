using System.Runtime.InteropServices;
using Avalonia;

namespace RhythmIsland.Services;

internal static class DisplayRefreshRateProvider
{
    private const uint MonitorDefaultToNearest = 2;
    private const int VerticalRefresh = 116;

    internal static double? GetForBounds(PixelRect bounds)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var rectangle = new NativeRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
        var monitor = MonitorFromRect(ref rectangle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return null;

        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(monitor, ref info) || string.IsNullOrWhiteSpace(info.DeviceName)) return null;

        var deviceContext = CreateDC("DISPLAY", info.DeviceName, null, IntPtr.Zero);
        if (deviceContext == IntPtr.Zero) return null;
        try
        {
            var refreshRate = GetDeviceCaps(deviceContext, VerticalRefresh);
            return refreshRate > 1 ? refreshRate : null;
        }
        finally
        {
            DeleteDC(deviceContext);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rectangle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initializationData);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);
}
