using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>How the writing comes across. Tone for now; statistics, readability and insights join it in a later phase.</summary>
internal sealed class PerformanceTab : PanelTab
{
    const string TonePrompt = "How does this sound to a reader?";
    TextBlock toneText = null!;

    public PerformanceTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "performance";
    public override string Label => "Performance";
    public override string Glyph => Icons.Performance;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Heading("Tone"));
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 0) };
        var detect = UiKit.Button("Detect tone", DetectTone);
        detect.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(detect, Dock.Right);
        row.Children.Add(detect);
        toneText = UiKit.Label(TonePrompt, 12.5, null, Theme.Muted, wrap: true);
        toneText.VerticalAlignment = VerticalAlignment.Center;
        toneText.Margin = new Thickness(0, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(toneText, System.Windows.Automation.AutomationLiveSetting.Polite);
        row.Children.Add(toneText);
        root.Children.Add(row);
        return root;
    }

    void DetectTone()
    {
        toneText.Text = "Listening to your text…";
        Host.DetectTone(OnTone);
    }

    void OnTone(ToneResult? t, string? error)
    {
        if (error != null || t == null)
        {
            toneText.Text = error ?? "Couldn't detect the tone.";
            return;
        }
        var top = t.Tones.OrderByDescending(x => x.Score).Take(3).Select(x => $"{x.Emoji} {x.Name}");
        string sentiment = t.Sentiment.Length > 0 ? char.ToUpperInvariant(t.Sentiment[0]) + t.Sentiment[1..] : "Neutral";
        toneText.Text = $"{string.Join("  ·  ", top)}\nFormality {t.Formality}/100 · {sentiment}";
    }

    public override void OnNewField() => toneText.Text = TonePrompt;
}
