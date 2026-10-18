using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lexi;

// A temporary playback file, never a replacement for provider audio. Decode with the built-in
// Windows Media Foundation source reader, then put silence and every decoded sample in ONE stream.
// https://learn.microsoft.com/windows/win32/medfound/tutorial--decoding-audio
internal sealed class SpeechPcmFile : IDisposable
{
    public string Path { get; }
    private SpeechPcmFile(string path) => Path = path;
    public void Dispose() { try { File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public static Task<SpeechPcmFile> PrepareAsync(string source, CancellationToken token) => Task.Run(() => Create(source, token), token);
    private static SpeechPcmFile Create(string source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeXi-speech-playback");
        Directory.CreateDirectory(directory);
        var result = new SpeechPcmFile(System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wav"));
        try { Decode(source, result.Path, token); token.ThrowIfCancellationRequested(); return result; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        { result.Dispose(); throw new InvalidOperationException("当前 Windows 缺少音频解码组件，请安装媒体功能包后重试。"); }
        catch (COMException) { result.Dispose(); throw new InvalidOperationException("音频无法解码，请检查发音服务返回的音频格式。"); }
        catch { result.Dispose(); throw; }
    }

    private static unsafe void Decode(string source, string target, CancellationToken token)
    {
        // Task-pool work remains off the WPF dispatcher. Balance each native startup and COM reference.
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0));
        nint reader = 0, partial = 0, format = 0;
        var started = false;
        try
        {
            Check(MFStartup(0x20070, 0)); started = true;
            Check(MFCreateSourceReaderFromURL(source, 0, out reader));
            Check(((delegate* unmanaged[Stdcall]<nint,uint,int,int>)Slot(reader,4))(reader,0xfffffffe,0));
            Check(MFCreateMediaType(out partial));
            SetGuid(partial, new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), new("73647561-0000-0010-8000-00aa00389b71"));
            SetGuid(partial, new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), new("00000001-0000-0010-8000-00aa00389b71"));
            var bitsKey = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
            Check(((delegate* unmanaged[Stdcall]<nint,Guid*,uint,int>)Slot(partial,21))(partial,&bitsKey,16));
            const uint audio = 0xfffffffd;
            Check(((delegate* unmanaged[Stdcall]<nint,uint,nint,nint,int>)Slot(reader,7))(reader,audio,0,partial));
            Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int>)Slot(reader,6))(reader,audio,&format));
            Check(((delegate* unmanaged[Stdcall]<nint,uint,int,int>)Slot(reader,4))(reader,audio,1));
            var channels = GetUInt(format, new("37e48bf5-645e-4c5b-89de-ada9e29b696a"));
            var rate = GetUInt(format, new("5faeeae7-0290-4c31-9e8a-c534f68d9dba"));
            if (GetUInt(format,bitsKey) != 16 || channels is < 1 or > 8 || rate is < 8000 or > 192000)
                throw new InvalidOperationException("音频采样格式不受支持。");
            var alignment = checked((ushort)(channels * 2)); var bytesPerSecond = checked(rate * alignment);
            using var file = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);
            writer.Write("RIFF"u8); writer.Write(0u); writer.Write("WAVEfmt "u8); writer.Write(16u);
            writer.Write((ushort)1); writer.Write((ushort)channels); writer.Write(rate); writer.Write(bytesPerSecond);
            writer.Write(alignment); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(0u);
            var silenceBytes = checked((int)(rate * SpeechOutput.LeadIn.TotalSeconds) * alignment);
            writer.Write(new byte[silenceBytes]);
            long decoded = 0; int empty = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                nint sample = 0, buffer = 0; uint actual = 0, flags = 0; long timestamp = 0;
                try
                {
                    Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,uint*,uint*,long*,nint*,int>)Slot(reader,9))
                        (reader,audio,0,&actual,&flags,&timestamp,&sample));
                    if ((flags & (1 | 0x20)) != 0) throw new InvalidOperationException("音频解码失败或采样格式发生变化。");
                    if (sample != 0)
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)Slot(sample,41))(sample,&buffer));
                        nint data = 0; uint maximum = 0, length = 0;
                        Check(((delegate* unmanaged[Stdcall]<nint,nint*,uint*,uint*,int>)Slot(buffer,3))(buffer,&data,&maximum,&length));
                        try
                        {
                            if (length % alignment != 0 || decoded + length > 128 * 1024 * 1024)
                                throw new InvalidOperationException("音频长度或采样数据不受支持。");
                            file.Write(new ReadOnlySpan<byte>((void*)data,checked((int)length))); decoded += length;
                        }
                        finally { Check(((delegate* unmanaged[Stdcall]<nint,int>)Slot(buffer,4))(buffer)); }
                        empty = 0;
                    }
                    else if (++empty > 1000) throw new InvalidOperationException("音频没有可播放的数据。");
                    if ((flags & 2) != 0) break;
                }
                finally { Release(buffer); Release(sample); }
            }
            if (decoded == 0) throw new InvalidOperationException("音频没有可播放的数据。");
            file.Position = 4; writer.Write(checked((uint)(file.Length-8)));
            file.Position = 40; writer.Write(checked((uint)(file.Length-44)));
        }
        finally { Release(format); Release(partial); Release(reader); if (started) _ = MFShutdown(); CoUninitialize(); }
    }
    // COM vtable slots follow IMFAttributes, IMFSourceReader, IMFSample and IMFMediaBuffer in the Windows SDK.
    // Slots 0..2 are IUnknown. Keep native pointers local to this worker and release in reverse ownership order.
    private static nint Slot(nint value, int slot) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(value),slot*IntPtr.Size);
    private static unsafe void Release(nint value) { if (value != 0) _ = ((delegate* unmanaged[Stdcall]<nint,uint>)Slot(value,2))(value); }
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    private static unsafe void SetGuid(nint attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<nint,Guid*,Guid*,int>)Slot(attributes,24))(attributes,&key,&value));
    private static unsafe uint GetUInt(nint attributes, Guid key)
    { uint value = 0; Check(((delegate* unmanaged[Stdcall]<nint,Guid*,uint*,int>)Slot(attributes,7))(attributes,&key,&value)); return value; }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved,uint model);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version,uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out nint mediaType);
    [DllImport("mfreadwrite.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int MFCreateSourceReaderFromURL(string url,nint attributes,out nint reader);
}
