using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NimbleWrite.Desktop.Core;

/// <summary>
/// The user's own NVIDIA API key for the installed app, in %APPDATA%\NimbleWrite\nvidia.key, encrypted with Windows
/// DPAPI for the current user: only this Windows account on this PC can decrypt it. The app hands it to its local
/// server as an environment variable. It is never logged or shown, and only ever sent to NVIDIA.
/// </summary>
public sealed class KeyStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NimbleWrite.NvidiaApiKey.v1");
    static readonly Regex AssignmentPrefix = new(@"^(?:export\s+|set\s+|\$env:)?[A-Za-z_][A-Za-z0-9_]*\s*[=:]\s*", RegexOptions.CultureInvariant);
    readonly string path;

    public KeyStore(string? path = null) => this.path = path ?? DefaultPath;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NimbleWrite", "nvidia.key");

    public bool HasKey => File.Exists(path);

    /// <summary>The saved key, or null if there isn't one or it can't be decrypted (another PC or account, or damaged).</summary>
    public string? Load()
    {
        byte[]? plain = null;
        try
        {
            if (!File.Exists(path)) return null;
            plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Normalize(Encoding.UTF8.GetString(plain));
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"the saved NVIDIA key can't be read ({e.GetType().Name})");
            return null;
        }
        finally
        {
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Save(string key)
    {
        string k = Normalize(key) ?? throw new ArgumentException("That isn't a usable API key.", nameof(key));
        byte[] plain = Encoding.UTF8.GetBytes(k);
        try
        {
            byte[] blob = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, blob);
            File.Move(tmp, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("delete NVIDIA key", e);
        }
    }

    /// <summary>
    /// What the user meant to paste: trims spaces and quotes, and a "Bearer " prefix or a whole NVIDIA_API_KEY=… line.
    /// Null when what's left can't be a key.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw == null) return null;
        string k = AssignmentPrefix.Replace(raw.Trim(), "");
        k = k.Trim().Trim('"', '\'', '`').Trim();
        if (k.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) k = k[7..].Trim();
        if (k.Length < 8 || k.Length > 300 || k.Any(char.IsWhiteSpace)) return null;
        return k;
    }
}
