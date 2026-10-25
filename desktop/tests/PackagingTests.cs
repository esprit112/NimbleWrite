using System;
using System.IO;
using System.Linq;
using System.Text;
using NimbleWrite.Desktop.Core;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

// Key assertions compare booleans, so a failing test never prints a key.
public sealed class KeyStoreTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "nw-keystore-" + Guid.NewGuid().ToString("N"));

    string KeyFile => Path.Combine(dir, "nvidia.key");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void SavesTheKeyEncryptedAndReadsItBack()
    {
        var store = new KeyStore(KeyFile);
        Assert.False(store.HasKey);
        Assert.Null(store.Load());

        const string key = "nvapi-FAKE00000000000000000000000000000000000000000";
        store.Save(key);
        Assert.True(store.HasKey);
        Assert.True(store.Load() == key);

        byte[] raw = File.ReadAllBytes(KeyFile);
        Assert.False(Encoding.UTF8.GetString(raw).Contains("FAKE0000", StringComparison.Ordinal));
        Assert.False(Encoding.Unicode.GetString(raw).Contains("FAKE0000", StringComparison.Ordinal));

        store.Delete();
        Assert.False(store.HasKey);
        Assert.Null(store.Load());
    }

    [Fact]
    public void ADamagedOrForeignKeyFileCountsAsNoKey()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(KeyFile, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.Null(new KeyStore(KeyFile).Load());
    }

    [Theory]
    [InlineData("  nvapi-abcdefgh12345678  ")]
    [InlineData("\"nvapi-abcdefgh12345678\"")]
    [InlineData("NVIDIA_API_KEY=nvapi-abcdefgh12345678")]
    [InlineData("export NVIDIA_API_KEY='nvapi-abcdefgh12345678'")]
    [InlineData("$env:NVIDIA_API_KEY = \"nvapi-abcdefgh12345678\"")]
    [InlineData("Bearer nvapi-abcdefgh12345678")]
    public void TakesWhatPeopleActuallyPaste(string pasted) => Assert.True(KeyStore.Normalize(pasted) == "nvapi-abcdefgh12345678");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("short")]
    [InlineData("nvapi-abc def-12345678")]
    public void RefusesWhatCannotBeAKey(string? pasted) => Assert.Null(KeyStore.Normalize(pasted));
}

public sealed class ServerLauncherTests
{
    [Fact]
    public void TheBundledServerGetsItsFoldersPortParentAndTheUsersKey()
    {
        var env = ServerLauncher.BundledEnvironment(@"C:\Apps\NimbleWrite\server", 4317, "nvapi-FAKE-user-key-0000", 1234)
            .ToDictionary(e => e.Name, e => e.Value);
        Assert.Equal("1", env["NIMBLEWRITE_PACKAGED"]);
        Assert.Equal(@"C:\Apps\NimbleWrite\web", env["NIMBLEWRITE_WEB_DIR"]);
        Assert.EndsWith(Path.Combine("NimbleWrite", "data"), env["NIMBLEWRITE_DATA_DIR"]);
        Assert.Equal("4317", env["PORT"]);
        Assert.Equal("1234", env["NIMBLEWRITE_PARENT_PID"]);
        Assert.Equal("production", env["NODE_ENV"]);
        Assert.True(env["NVIDIA_API_KEY"] == "nvapi-FAKE-user-key-0000");
        Assert.True(env.ContainsKey("NODE_OPTIONS") && env["NODE_OPTIONS"] == null);
    }

    [Fact]
    public void WithoutASavedKeyTheVariableIsRemovedNotInherited()
    {
        var env = ServerLauncher.BundledEnvironment(@"C:\Apps\NimbleWrite\server\", 5000, null, 1).ToDictionary(e => e.Name, e => e.Value);
        Assert.True(env.ContainsKey("NVIDIA_API_KEY") && env["NVIDIA_API_KEY"] == null);
        Assert.Equal(@"C:\Apps\NimbleWrite\web", env["NIMBLEWRITE_WEB_DIR"]);
        Assert.Equal("5000", env["PORT"]);
    }

    [Fact]
    public void OnlyACompleteBundleMeansTheInstalledApp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nw-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "server"));
            Assert.Null(ServerLauncher.FindBundled(dir));
            File.WriteAllText(Path.Combine(dir, "server", "node.exe"), "");
            Assert.Null(ServerLauncher.FindBundled(dir));
            File.WriteAllText(Path.Combine(dir, "server", ServerLauncher.BundleName), "");
            Assert.Equal(Path.Combine(dir, "server"), ServerLauncher.FindBundled(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
