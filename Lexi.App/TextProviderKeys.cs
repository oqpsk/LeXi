using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lexi.Core;

namespace Lexi;

// Explicitly entered text-service credentials, protected for the current Windows user and
// bound to the complete normalized endpoint. Changing hosts never forwards an old saved key.
internal sealed class TextProviderKeys(string root)
{
    private string FileFor(string url) => Path.Combine(root, "text-provider-keys", Cache.Hash(CompatibleText.NormalizeBase(url)) + ".bin");
    public bool HasSaved(string url) => File.Exists(FileFor(url));
    public CompatibleCredentials Read(string url)
    {
        var file = FileFor(url); if (!File.Exists(file)) return new();
        byte[]? clear = null;
        try
        {
            clear = Protect(File.ReadAllBytes(file), false);
            using var data = JsonDocument.Parse(clear);
            var value = data.RootElement;
            return new(value.GetProperty("clientId").GetString()!, value.GetProperty("clientSecret").GetString()!, value.GetProperty("apiKey").GetString()!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new CompatibleException("credential_store_failed", CompatibleText.StorageFailure); }
        finally { if (clear != null) CryptographicOperations.ZeroMemory(clear); }
    }
    public void Remove(string url) { try { var path = FileFor(url); if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new CompatibleException("credential_store_failed", CompatibleText.StorageFailure); } }
    public void Save(string url, CompatibleCredentials value)
    {
        CompatibleText.ValidateCredentials(value);
        var clear = JsonSerializer.SerializeToUtf8Bytes(new { clientId = value.ClientId, clientSecret = value.ClientSecret, apiKey = value.ApiKey });
        string? temp = null;
        try
        {
            var file = FileFor(url); temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var encrypted = Protect(clear, true); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(temp, encrypted); File.Move(temp, file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        { throw new CompatibleException("credential_store_failed", CompatibleText.StorageFailure); }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            try { if (temp != null && File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
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
