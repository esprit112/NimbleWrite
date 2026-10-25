using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace NimbleWrite.Desktop.Core;

/// <summary>
/// Starts the local NimbleWrite server when it isn't running. An installed copy runs its bundled server
/// (server\node.exe + server\nimblewrite-server.mjs beside this exe) with the user's own NVIDIA key; a development copy
/// inside the project runs node + tsx with the project's .env.
/// </summary>
public static class ServerLauncher
{
    public const string BundleName = "nimblewrite-server.mjs";

    static readonly object Gate = new();
    static readonly object LogGate = new();
    static Process? started;
    static StreamWriter? serverLog;
    static int port = 4317;
    static Func<string?> keyProvider = () => null;

    /// <summary>The bundled server's folder in an installed copy; null when running from the project.</summary>
    public static string? BundledDir { get; } = FindBundled(AppContext.BaseDirectory);

    /// <summary>True for the installed app: it runs its own server, keeps its data in %APPDATA% and has the user's key.</summary>
    public static bool Packaged => BundledDir != null;

    /// <summary>True while a server this app started is running.</summary>
    public static bool OwnsServer
    {
        get
        {
            lock (Gate) return started is { HasExited: false };
        }
    }

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NimbleWrite", "data");

    /// <summary>The bundled server's own output (startup lines and error types; never request text).</summary>
    public static string ServerLogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NimbleWrite", "server.log");

    public static string? FindBundled(string appDir)
    {
        string dir = Path.Combine(appDir, "server");
        return File.Exists(Path.Combine(dir, "node.exe")) && File.Exists(Path.Combine(dir, BundleName)) ? dir : null;
    }

    /// <summary>The port comes from the configured server URL; the key (installed copies only) from <paramref name="key"/>.</summary>
    public static void Configure(string serverUrl, Func<string?> key)
    {
        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var u) && u.IsLoopback) port = u.Port;
        keyProvider = key;
    }

    /// <summary>The NimbleWrite project folder: the nearest parent with its package.json and server/index.ts.</summary>
    public static string? FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; dir != null && i < 8; i++, dir = dir.Parent)
        {
            string pkg = Path.Combine(dir.FullName, "package.json");
            if (!File.Exists(pkg) || !File.Exists(Path.Combine(dir.FullName, "server", "index.ts"))) continue;
            try
            {
                if (File.ReadAllText(pkg).Contains("\"nimblewrite\"", StringComparison.Ordinal)) return dir.FullName;
            }
            catch (IOException)
            {
            }
        }
        return null;
    }

    /// <summary>Start the server in the background. Returns false if it can't be found or is already running.</summary>
    public static bool TryStart()
    {
        lock (Gate)
        {
            if (started is { HasExited: false }) return false;
            try
            {
                Process p;
                if (BundledDir != null)
                {
                    p = new Process { StartInfo = BundledStartInfo(BundledDir) };
                    OpenServerLog();
                    p.OutputDataReceived += (_, e) => WriteServerLog(e.Data);
                    p.ErrorDataReceived += (_, e) => WriteServerLog(e.Data);
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }
                else
                {
                    var psi = DevelopmentStartInfo();
                    if (psi == null) return false;
                    p = Process.Start(psi) ?? throw new InvalidOperationException("node didn't start");
                }
                started?.Dispose();
                started = p;
                Log.Info($"started the NimbleWrite server (pid {p.Id}{(BundledDir != null ? ", bundled" : "")})");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("start server", e);
                return false;
            }
        }
    }

    /// <summary>Stop the server this app started and start it again, e.g. so it picks up a new key.</summary>
    public static bool Restart()
    {
        StopIfStarted();
        return TryStart();
    }

    /// <summary>Stop the server only if this app started it.</summary>
    public static void StopIfStarted()
    {
        lock (Gate)
        {
            try
            {
                if (started is { HasExited: false })
                {
                    started.Kill(entireProcessTree: true);
                    started.WaitForExit(5000);
                }
            }
            catch (Exception e)
            {
                Log.Error("stop server", e);
            }
            finally
            {
                started?.Dispose();
                started = null;
            }
        }
    }

    /// <summary>Environment changes for the bundled server; a null value removes the variable.</summary>
    public static IReadOnlyList<(string Name, string? Value)> BundledEnvironment(string serverDir, int port, string? key, int parentPid)
    {
        string appDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(serverDir)) ?? serverDir;
        return new (string, string?)[]
        {
            ("NIMBLEWRITE_PACKAGED", "1"),
            ("NIMBLEWRITE_DATA_DIR", DataDir),
            ("NIMBLEWRITE_WEB_DIR", Path.Combine(appDir, "web")),
            ("NIMBLEWRITE_PARENT_PID", parentPid.ToString(CultureInfo.InvariantCulture)),
            ("PORT", port.ToString(CultureInfo.InvariantCulture)),
            ("NODE_ENV", "production"),
            // A user's own NODE_OPTIONS (preloaded modules, inspector flags) must not change the bundled server.
            ("NODE_OPTIONS", null),
            // Only the key saved in NimbleWrite counts: a stray NVIDIA_API_KEY in the user's environment is not inherited.
            ("NVIDIA_API_KEY", string.IsNullOrWhiteSpace(key) ? null : key),
        };
    }

    static ProcessStartInfo BundledStartInfo(string dir)
    {
        var psi = new ProcessStartInfo(Path.Combine(dir, "node.exe"))
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(Path.Combine(dir, BundleName));
        foreach (var (name, value) in BundledEnvironment(dir, port, keyProvider(), Environment.ProcessId))
        {
            if (value == null) psi.Environment.Remove(name);
            else psi.Environment[name] = value;
        }
        return psi;
    }

    static ProcessStartInfo? DevelopmentStartInfo()
    {
        string? root = FindRoot();
        if (root == null) return null;
        string cli = Path.Combine(root, "node_modules", "tsx", "dist", "cli.mjs");
        if (!File.Exists(cli)) return null;
        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add(cli);
        psi.ArgumentList.Add(Path.Combine("server", "index.ts"));
        return psi;
    }

    static void OpenServerLog()
    {
        lock (LogGate)
        {
            try
            {
                serverLog?.Dispose();
                serverLog = null;
                string file = ServerLogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var info = new FileInfo(file);
                if (info.Exists && info.Length > 512 * 1024) File.Move(file, Path.ChangeExtension(file, ".old.log"), true);
                serverLog = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Log.Error("server log", e);
            }
        }
    }

    static void WriteServerLog(string? line)
    {
        if (line == null) return;
        lock (LogGate)
        {
            try
            {
                serverLog?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}");
            }
            catch (Exception)
            {
            }
        }
    }
}
