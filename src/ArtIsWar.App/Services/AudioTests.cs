using System.Runtime.InteropServices;

namespace ArtIsWar.App.Services;

public sealed class AudioTests
{
    public async Task Play(string endpointId, string kind, CancellationToken token)
    {
        await Task.Run(() => Render(endpointId, kind, token), token);
    }
    private static void Render(string id, string kind, CancellationToken token)
    {
        var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(id, out var device));
            try
            {
                var iid = typeof(IAudioClient).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out var instance));
                try
                {
                    var client = (IAudioClient)instance;
                    Marshal.ThrowExceptionForHR(client.GetMixFormat(out var format));
                    try
                    {
                        int channels = (ushort)Marshal.ReadInt16(format, 2), rate = Marshal.ReadInt32(format, 4);
                        int bits = (ushort)Marshal.ReadInt16(format, 14), tag = (ushort)Marshal.ReadInt16(format, 0);
                        bool floating = tag == 3 || tag == 65534 && Marshal.PtrToStructure<Guid>(format + 24) == new Guid("00000003-0000-0010-8000-00aa00389b71");
                        if (!(floating && bits == 32) && !(bits is 16 or 24 or 32 && !floating)) throw new InvalidOperationException("Audio test does not support this mix format.");
                        Marshal.ThrowExceptionForHR(client.Initialize(0, 0, 1000000, 0, format, IntPtr.Zero));
                        client.GetBufferSize(out uint capacity);
                        var renderId = typeof(IAudioRenderClient).GUID;
                        Marshal.ThrowExceptionForHR(client.GetService(ref renderId, out var renderObject));
                        try
                        {
                            var render = (IAudioRenderClient)renderObject;
                            client.Start();
                            try
                            {
                                int frame = 0, total = rate * 4; var random = new Random(123);
                                while (frame < total && !token.IsCancellationRequested)
                                {
                                    client.GetCurrentPadding(out uint padding);
                                    int count = Math.Min((int)(capacity - padding), total - frame);
                                    if (count == 0) { Thread.Sleep(10); continue; }
                                    Marshal.ThrowExceptionForHR(render.GetBuffer((uint)count, out var data));
                                    int bytesPerSample = bits / 8; byte[] bytes = new byte[count * channels * bytesPerSample];
                                    for (int i = 0; i < count; i++, frame++)
                                    {
                                        double t = frame / (double)rate;
                                        double fade = Math.Min(1, t * 20) * Math.Min(1, (4 - t) * 20);
                                        double sample = kind switch {
                                            "Movement" => .07 * (random.NextDouble() * 2 - 1) * Math.Exp(-((t * 2) % 1) * 12),
                                            "Dynamic range" => (t < 2 ? .008 : .16) * Math.Sin(2 * Math.PI * 650 * t),
                                            "Peak protection" => .2 * Math.Sin(2 * Math.PI * 500 * t),
                                            _ => .06 * Math.Sin(2 * Math.PI * 550 * t) };
                                        for (int channel = 0; channel < channels; channel++)
                                        {
                                            double pan = kind == "Left" ? (channel == 0 ? 1 : 0) : kind == "Right" ? (channel == 1 ? 1 : 0) : kind == "Stereo" ? (channel == ((int)t % 2) ? 1 : 0) : channel < 2 ? 1 : 0;
                                            double value = sample * fade * pan; int offset = (i * channels + channel) * bytesPerSample;
                                            if (floating) BitConverter.GetBytes((float)value).CopyTo(bytes, offset);
                                            else
                                            {
                                                int integer = (int)(value * (Math.Pow(2, bits - 1) - 1));
                                                for (int b = 0; b < bytesPerSample; b++) bytes[offset + b] = (byte)(integer >> (b * 8));
                                            }
                                        }
                                    }
                                    Marshal.Copy(bytes, 0, data, bytes.Length);
                                    Marshal.ThrowExceptionForHR(render.ReleaseBuffer((uint)count, 0));
                                }
                                if (!token.IsCancellationRequested) Thread.Sleep(120);
                            }
                            finally { client.Stop(); }
                        }
                        finally { Marshal.ReleaseComObject(renderObject); }
                    }
                    finally { Marshal.FreeCoTaskMem(format); }
                }
                finally { Marshal.ReleaseComObject(instance); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
}
[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
    [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
}
