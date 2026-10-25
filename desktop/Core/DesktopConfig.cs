using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace NimbleWrite.Desktop.Core;

/// <summary>Desktop-only preferences, stored in %APPDATA%\NimbleWrite\desktop.json. Writing settings live on the server.</summary>
public sealed class DesktopConfig
{
    /// <summary>Apps where checking makes no sense or isn't wanted: terminals, code editors, password managers, the shell.</summary>
    public static readonly string[] DefaultDisabledApps =
    {
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh", "mintty", "Code", "Cursor", "devenv", "rider64", "idea64",
        "pycharm64", "webstorm64", "KeePass", "KeePassXC", "1Password", "Bitwarden", "mstsc", "explorer", "SearchHost", "StartMenuExperienceHost",
    };

    public bool Enabled { get; set; } = true;
    public List<string> DisabledApps { get; set; } = new(DefaultDisabledApps);
    public string Hotkey { get; set; } = "Ctrl+Alt+N";
    public bool ShowUnderlines { get; set; } = true;
    public string ServerUrl { get; set; } = "http://127.0.0.1:4317";
    public bool Welcomed { get; set; }
    /// <summary>Installed app: the user chose "Not now" on the NVIDIA key prompt, so it isn't shown again at startup.</summary>
    public bool KeyPromptDismissed { get; set; }

    [JsonIgnore] public DateTime PausedUntil { get; set; } = DateTime.MinValue;

    [JsonIgnore] public bool IsActive => Enabled && DateTime.Now >= PausedUntil;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NimbleWrite", "desktop.json");

    public static DesktopConfig Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<DesktopConfig>(File.ReadAllText(FilePath), Json) ?? new DesktopConfig();
        }
        catch (Exception e)
        {
            Log.Error("config load", e);
        }
        return new DesktopConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception e)
        {
            Log.Error("config save", e);
        }
    }

    public bool IsAppDisabled(string process) => DisabledApps.Any(a => string.Equals(a, process, StringComparison.OrdinalIgnoreCase));

    public void SetAppDisabled(string process, bool disabled)
    {
        var next = DisabledApps.Where(a => !string.Equals(a, process, StringComparison.OrdinalIgnoreCase)).ToList();
        if (disabled) next.Add(process);
        DisabledApps = next; // swap the whole list so the engine thread never sees it half-changed
        Save();
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("NimbleWrite") is string;
        }
    }

    /// <summary>True when the "start with Windows" entry starts this exe (not some other copy of NimbleWrite).</summary>
    public static bool StartupRunsThisCopy
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("NimbleWrite") is string v && Environment.ProcessPath is { } exe
                && v.Trim().Trim('"').Equals(exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue("NimbleWrite", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("NimbleWrite", false);
    }
}
