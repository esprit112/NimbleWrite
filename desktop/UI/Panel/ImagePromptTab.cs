using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>Image Prompt Formatter: turn a rough idea into a prompt for FLUX or Z-Image. Restructures only, unless asked to improve.</summary>
internal sealed class ImagePromptTab : PanelTab
{
    /// <summary>Same as IMAGE_TARGETS in src/editor/panels/AgentPanels.tsx (FLUX.3 Klein first).</summary>
    internal static readonly (string Id, string Label, string Hint)[] Targets =
    {
        ("flux3-klein", "FLUX.3 Klein", "Distilled Klein: one caption in BFL's upsampler grammar (medium first, then scene/light/camera, then subjects anchored to frame position, exact counts, quoted text)"),
        ("flux2-klein", "FLUX.2 Klein", "Distilled Klein: subject-first prose, lighting described as source/quality/direction/temperature, hex colours, no negative prompt"),
        ("zimage-turbo", "Z-Image Turbo", "Long concrete prose with every constraint in the positive prompt, quoted on-image text, no quality tags or negatives"),
        ("flux3", "FLUX.3 (generic)", "Caption-grammar prose: style first, then scene/lighting/camera, then subjects anchored to frame position"),
        ("flux2", "FLUX.2 (generic)", "Flexible keyword/physics-first style: medium, Kelvin lighting, camera, subject, palette"),
    };

    /// <summary>Restructure what was written rather than add to it (Martin's preference; the web defaults to improving).</summary>
    internal const bool EnhanceByDefault = false;

    ChipGroup target = null!;
    TextBlock targetHint = null!;
    InputBox idea = null!;
    ToggleRow enhance = null!;
    AgentRunner runner = null!;
    StackPanel results = null!;
    TextScope? ideaScope;

    public ImagePromptTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "image-prompt";
    public override string Label => "Image Prompt Formatter";
    public override string Glyph => Icons.ImagePrompt;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Turn a rough idea into a correctly-formatted prompt for FLUX.2 Klein, FLUX.3 Klein or Z-Image Turbo."));
        root.Children.Add(Heading("Target model"));
        target = new ChipGroup(Targets, Targets[0].Id) { Margin = new Thickness(0, 4, 0, 0) };
        targetHint = Widgets.Fine(Targets[0].Hint);
        targetHint.Margin = new Thickness(0, 0, 0, 6);
        target.SelectedChanged += id => targetHint.Text = Targets.First(t => t.Id == id).Hint;
        root.Children.Add(target);
        root.Children.Add(targetHint);
        root.Children.Add(Heading("Your idea"));
        idea = new InputBox("Your idea", 4000, multiline: true, "Leave empty to use your selection (or all the text)") { Margin = new Thickness(0, 4, 0, 4) };
        idea.Submitted += Go;
        root.Children.Add(idea);
        enhance = new ToggleRow("Improve and add detail (lighting, camera, texture) instead of just reformatting what I wrote", EnhanceByDefault);
        root.Children.Add(enhance);
        var go = UiKit.Button("Format prompt", Go, Kind.Primary);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        go.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(go);
        runner = new AgentRunner(Host);
        root.Children.Add(runner);
        results = new StackPanel();
        root.Children.Add(results);
        return root;
    }

    void Go()
    {
        results.Children.Clear();
        string t = target.Selected;
        bool more = enhance.On;
        string typed = idea.Text.Trim();
        if (typed.Length > 0)
        {
            ideaScope = null;
            _ = runner.Run("Writing the prompt… (usually 5–15 s)", ct => Host.Api.ImagePromptAsync(typed, t, more, ct), r => Show(r, null), $"Formatted at {DateTime.Now:HH:mm}.");
            return;
        }
        _ = runner.Run(4000, "format", "Writing the prompt… (usually 5–15 s)", null, (scope, ct) => Host.Api.ImagePromptAsync(scope.Text.Trim(), t, more, ct), (r, scope) => Show(r, scope));
    }

    internal void Show(ImagePromptResult r, TextScope? from)
    {
        _ = View;
        ideaScope = from;
        results.Children.Clear();
        var s = new StackPanel();
        string label = Targets.FirstOrDefault(x => x.Id == target.Selected).Label ?? "";
        s.Children.Add(UiKit.Label($"{label} prompt · {r.Ms / 1000.0:0.0} s", 11.5, FontWeights.SemiBold, Theme.BrandText));
        var text = UiKit.Label(r.Prompt, 13, null, Theme.Text, wrap: true);
        text.Margin = new Thickness(0, 4, 0, 8);
        s.Children.Add(text);
        var actions = new WrapPanel();
        actions.Children.Add(UiKit.Spaced(UiKit.Button("Copy", () => Copy(r.Prompt), Kind.Primary)));
        if (from != null)
        {
            string what = from.Whole ? "Replace the text" : "Replace the selection";
            actions.Children.Add(UiKit.Spaced(UiKit.Button(what, () => Replace(from, r.Prompt), Kind.Secondary, "Put the prompt where your idea was")));
        }
        actions.Children.Add(UiKit.Spaced(UiKit.Button("Retry", Go, Kind.Ghost)));
        s.Children.Add(actions);
        results.Children.Add(Widgets.Card(s, Theme.Brand, Theme.Raised));
    }

    void Copy(string prompt)
    {
        try
        {
            Clipboard.SetText(prompt);
            Host.Notice("Prompt copied.");
        }
        catch (Exception)
        {
            Host.Notice("Couldn't use the clipboard just now.");
        }
    }

    void Replace(TextScope scope, string prompt)
    {
        Host.ApplyRewrite(scope, prompt, "Replaced with the prompt.");
        results.Children.Clear();
        runner.Reset();
    }

    public override void OnNewField()
    {
        if (ideaScope == null) return; // a typed idea has nothing to do with the text box
        runner.Reset();
        results.Children.Clear();
        ideaScope = null;
    }
}
