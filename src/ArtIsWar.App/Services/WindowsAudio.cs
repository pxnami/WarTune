using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArtIsWar.App.Services;

public sealed record PlaybackDevice(string Id, string Name, int SampleRate, int Channels, string SupportedRates)
{
    public override string ToString() => Name;
}
public sealed record ScanResult(List<PlaybackDevice> Devices, string DefaultId, string Windows, string Cpu, string Architecture, ulong AvailableMemory);

public sealed class WindowsAudio : IDisposable
{
    private IAudioMeter? cachedMeter;
    private string? meterId;
    public ScanResult Scan()
    {
        var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
        var devices = new List<PlaybackDevice>(); string defaultId = "";
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var defaultDevice) == 0)
            { defaultDevice.GetId(out defaultId); Marshal.ReleaseComObject(defaultDevice); }
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out var collection));
            try
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    collection.Item(i, out var device);
                    try
                    {
                        device.GetId(out string id);
                        string name = "Windows playback endpoint";
                        device.OpenPropertyStore(0, out var store);
                        try
                        {
                            var key = new PropertyKey { Id = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 };
                            if (store.GetValue(ref key, out var value) == 0)
                            { try { name = Marshal.PtrToStringUni(value.Pointer) ?? name; } finally { PropVariantClear(ref value); } }
                        }
                        finally { Marshal.ReleaseComObject(store); }
                        var iid = typeof(IAudioClient).GUID;
                        int rate = 0, channels = 0; string rates = "Unavailable";
                        if (device.Activate(ref iid, 23, IntPtr.Zero, out object instance) == 0)
                        {
                            var client = (IAudioClient)instance;
                            try
                            {
                                if (client.GetMixFormat(out var format) == 0)
                                {
                                    try
                                    {
                                        rate = Marshal.ReadInt32(format, 4); channels = (ushort)Marshal.ReadInt16(format, 2);
                                        var supported = new List<int>();
                                        int size = 18 + (ushort)Marshal.ReadInt16(format, 16);
                                        IntPtr candidate = Marshal.AllocCoTaskMem(size);
                                        try
                                        {
                                            var bytes = new byte[size]; Marshal.Copy(format, bytes, 0, size); Marshal.Copy(bytes, 0, candidate, size);
                                            int alignment = (ushort)Marshal.ReadInt16(format, 12);
                                            foreach (int r in new[] { 44100, 48000, 88200, 96000, 192000 })
                                            {
                                                Marshal.WriteInt32(candidate, 4, r); Marshal.WriteInt32(candidate, 8, r * alignment);
                                                int result = client.IsFormatSupported(0, candidate, out var closest);
                                                if (closest != IntPtr.Zero) Marshal.FreeCoTaskMem(closest);
                                                if (result == 0) supported.Add(r);
                                            }
                                            rates = supported.Count == 0 ? "No exact shared-mode rates reported" : string.Join(", ", supported.Select(r => $"{r / 1000.0:g} kHz"));
                                        }
                                        finally { Marshal.FreeCoTaskMem(candidate); }
                                    }
                                    finally { Marshal.FreeCoTaskMem(format); }
                                }
                            }
                            finally { Marshal.ReleaseComObject(instance); }
                        }
                        devices.Add(new(id, name, rate, channels, rates));
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        using var os = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() }; GlobalMemoryStatusEx(ref memory);
        return new(devices, defaultId, $"Windows {os?.GetValue("DisplayVersion")} · build {os?.GetValue("CurrentBuildNumber")}",
            cpu?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Not exposed", RuntimeInformation.OSArchitecture.ToString(), memory.AvailablePhysical);
    }
    public float Peak(string id)
    {
        if (id == meterId && cachedMeter != null) { Marshal.ThrowExceptionForHR(cachedMeter.GetPeakValue(out float current)); return current; }
        Dispose();
        var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(id, out var device));
            try
            {
                var iid = typeof(IAudioMeter).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out var instance));
                cachedMeter = (IAudioMeter)instance; meterId = id;
                Marshal.ThrowExceptionForHR(cachedMeter.GetPeakValue(out float peak)); return peak;
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
    public void Dispose() { if (cachedMeter != null) Marshal.ReleaseComObject(cachedMeter); cachedMeter = null; meterId = null; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
}
[StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Id; public uint Pid; }
[StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class DeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IDeviceCollection collection);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IAudioNotification callback);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IAudioNotification callback);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IDevice device); }
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(uint mode, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value); [PreserveSig] int Commit();
}
[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int mode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
    [PreserveSig] int GetBufferSize(out uint frames); [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long period, out long minimum);
    [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeter { [PreserveSig] int GetPeakValue(out float peak); }

[ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioNotification
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, NotificationKey key);
}
[StructLayout(LayoutKind.Sequential)] public struct NotificationKey { public Guid Id; public uint Pid; }
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class EndpointNotifications : IAudioNotification, IDisposable
{
    private readonly IDeviceEnumerator enumerator = (IDeviceEnumerator)new DeviceEnumerator();
    private readonly Action changed;
    public EndpointNotifications(Action changed) { this.changed = changed; Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(this)); }
    public int OnDeviceStateChanged(string id, uint state) { changed(); return 0; }
    public int OnDeviceAdded(string id) { changed(); return 0; }
    public int OnDeviceRemoved(string id) { changed(); return 0; }
    public int OnDefaultDeviceChanged(int flow, int role, string? id) { if (flow == 0) changed(); return 0; }
    public int OnPropertyValueChanged(string id, NotificationKey key) { changed(); return 0; }
    public void Dispose() { enumerator.UnregisterEndpointNotificationCallback(this); Marshal.ReleaseComObject(enumerator); }
}
