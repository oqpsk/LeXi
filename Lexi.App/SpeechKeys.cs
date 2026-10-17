using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Lexi;

// Only keys explicitly entered for Lexi speech are stored here. Provider CLI
// subscription credentials are never inspected or copied.
internal sealed class SpeechKeys(string root)
{
    private string FileFor(string provider) => provider is "openai" or "xai" ? Path.Combine(root, "speech-keys", provider + ".bin")
        : throw new ArgumentException("Unknown speech provider.");
    public bool HasSaved(string provider) => File.Exists(FileFor(provider));
    public string Read(string provider)
    {
        var file = FileFor(provider);
        if (File.Exists(file))
        {
            var clear = Protect(File.ReadAllBytes(file), false);
            try { return Encoding.UTF8.GetString(clear); } finally { CryptographicOperations.ZeroMemory(clear); }
        }
        return Environment.GetEnvironmentVariable(provider == "openai" ? "OPENAI_API_KEY" : "XAI_API_KEY") ?? "";
    }
    public void Save(string provider, string value)
    {
        var file = FileFor(provider);
        if (string.IsNullOrWhiteSpace(value)) { if (File.Exists(file)) File.Delete(file); return; }
        var clear = Encoding.UTF8.GetBytes(value.Trim());
        try
        {
            var encrypted = Protect(clear, true); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file + ".tmp", encrypted); File.Move(file + ".tmp", file, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "语音密钥无法解密或保存，请在设置中重新填写。");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (int i = 0; i < input.Size; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            { for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
