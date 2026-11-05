using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

internal enum RewriteKind { Paraphrase, Humanize, Translate }

/// <summary>
/// Paraphraser, Humanizer and Translate: one class for the three, like the web editor's RewritePanel. Each works on
/// the selection (or all the text), streams the result, and replaces through the engine's verified path.
/// </summary>
internal sealed class RewriteTab : PanelTab
{
    /// <summary>Same ids, labels and hints as PARAPHRASE_MODES in shared/meta.ts (a test keeps them in step).</summary>
    internal static readonly (string Id, string Label, string Hint)[] ParaphraseModes =
    {
        ("standard", "Standard", "Reword while keeping the meaning"),
        ("fluent", "Fluent", "Smooth and natural"),
        ("formal", "Formal", "Polished and professional"),
        ("academic", "Academic", "Scholarly and precise"),
        ("simple", "Simple", "Plain and easy to read"),
        ("creative", "Creative", "Fresh, vivid phrasing"),
        ("expand", "Expand", "Add depth and detail"),
        ("shorten", "Shorten", "Keep only what matters"),
        ("custom", "Custom voice", "Describe the voice you want"),
    };

    readonly RewriteKind kind;
    RewriteResult result = null!;
    ChipGroup? modes;
    InputBox? voice;
    LanguagePicker? from;
    LanguagePicker? to;

    public RewriteTab(IPanelActions host, RewriteKind kind) : base(host) => this.kind = kind;

    public override string Id => kind switch { RewriteKind.Paraphrase => "paraphrase", RewriteKind.Humanize => "humanize", _ => "translate" };

    public override string Label => kind switch { RewriteKind.Paraphrase => "Paraphraser", RewriteKind.Humanize => "Humanizer", _ => "Translate" };

    public override string Glyph => kind switch { RewriteKind.Paraphrase => Icons.Paraphrase, RewriteKind.Humanize => Icons.Humanize, _ => Icons.Translate };

    string Subtitle => kind switch
    {
        RewriteKind.Paraphrase => "Reword text for a different tone, audience or style while keeping the meaning.",
        RewriteKind.Humanize => "Make AI-assisted writing sound natural: strips stock AI phrasing, varies rhythm, keeps your meaning.",
        _ => "Translate text into another language with NVIDIA NIM.",
    };

    string Cta => kind switch { RewriteKind.Paraphrase => "Paraphrase", RewriteKind.Humanize => "Humanize", _ => "Translate" };

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint(Subtitle));
        if (kind == RewriteKind.Paraphrase)
        {
            root.Children.Add(Heading("Mode"));
            modes = new ChipGroup(ParaphraseModes, "standard") { Margin = new Thickness(0, 4, 0, 2) };
            voice = new InputBox("Describe the voice", 4000, placeholder: "e.g. witty British tech journalist") { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
            voice.Submitted += Go;
            modes.SelectedChanged += id => voice.Visibility = id == "custom" ? Visibility.Visible : Visibility.Collapsed;
            root.Children.Add(modes);
            root.Children.Add(voice);
        }
        else if (kind == RewriteKind.Translate)
        {
            root.Children.Add(Heading("From"));
            from = new LanguagePicker("Translate from", Languages.Sources, new[] { "English" }, null, autoLabel: "Detect") { Margin = new Thickness(0, 4, 0, 4) };
            root.Children.Add(from);
            root.Children.Add(Heading("To", 4));
            to = new LanguagePicker("Translate into", Languages.All, Host.RecentLanguages, Host.RecentLanguages.FirstOrDefault() ?? "French", autoLabel: null) { Margin = new Thickness(0, 4, 0, 4) };
            root.Children.Add(to);
        }
        root.Children.Add(Hint("Works on your selection, or on all the text if nothing is selected.", 6));
        var go = UiKit.Button(Cta, Go, Kind.Primary);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(go);
        result = new RewriteResult(Host);
        root.Children.Add(result);
        return root;
    }

    void Go()
    {
        var (request, problem) = BuildRequest(kind, modes?.Selected ?? "standard", voice?.Text ?? "", to?.Selected ?? "French", from?.Selected);
        if (request == null)
        {
            Host.Notice(problem!);
            return;
        }
        if (request.TargetLanguage != null) Host.UsedLanguage(request.TargetLanguage);
        result.Start(request);
    }

    /// <summary>The request for the current choices, or why there can't be one yet. Text and context are filled in by the engine.</summary>
    internal static (WriteRequest? Request, string? Problem) BuildRequest(RewriteKind kind, string mode, string voice, string target, string? source)
    {
        switch (kind)
        {
            case RewriteKind.Paraphrase:
                string m = ParaphraseModes.Any(x => x.Id == mode) ? mode : "standard";
                if (m != "custom") return (new WriteRequest("paraphrase", "", Mode: m), null);
                return voice.Trim().Length == 0 ? (null, "Describe the voice you want first.") : (new WriteRequest("paraphrase", "", Instruction: voice.Trim(), Mode: m), null);
            case RewriteKind.Humanize:
                return (new WriteRequest("humanize", ""), null);
            default:
                if (source != null && source == target) return (null, "Pick a different language to translate into.");
                return (new WriteRequest("translate", "", TargetLanguage: target, SourceLanguage: source), null);
        }
    }

    public override void OnNewField() => result.Discard();

    // For tests and render checks.
    internal ChipGroup? Modes => modes;
    internal LanguagePicker? To => to;
}
