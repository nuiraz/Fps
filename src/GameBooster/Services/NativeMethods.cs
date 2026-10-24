using System.Runtime.InteropServices;

namespace GameBooster.Services;

internal static class NativeMethods
{
    public const int BeginSystemChange = 100;
    public const int EndSystemChange = 101;
    public const int ModifySettings = 12;
    public const uint SpiSetDragFullWindows = 0x0025;
    public const uint SpiSetMenuAnimation = 0x1003;
    public const uint SpiSetClientAreaAnimation = 0x1043;
    public const uint SpifUpdateAndNotify = 0x01 | 0x02;
    public const uint WmSettingChange = 0x001A;
    public const uint SmtoAbortIfHung = 0x0002;
    public static readonly IntPtr HwndBroadcast = new(0xffff);
    public const uint SherbNoConfirmation = 0x00000001;
    public const uint SherbNoProgressUi = 0x00000002;
    public const uint SherbNoSound = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RestorePointInfo
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StateMgrStatus
    {
        public int Status;
        public long SequenceNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SRSetRestorePointW(ref RestorePointInfo info, out StateMgrStatus status);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wParam, string? lParam, uint flags, uint timeout, out UIntPtr result);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
