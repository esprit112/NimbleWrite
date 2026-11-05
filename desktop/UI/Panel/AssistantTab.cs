using System.Windows;
using System.Windows.Controls;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.UI.Panels;

/// <summary>The web editor's AI Assistant: its 14 quick actions, or an instruction in your own words.</summary>
internal sealed class AssistantTab : PanelTab
{
    /// <summary>Same tasks, labels and order as QUICK in src/editor/panels/AgentPanels.tsx.</summary>
    internal static readonly (string Task, string Label)[] QuickActions =
    {
        ("improve", "Improve it"), ("shorten", "Shorten it"), ("expand", "Make it more detailed"), ("simplify", "Simplify it"),
        ("professional", "Sound professional"), ("friendly", "Sound friendly"), ("confident", "Sound confident"),
        ("persuasive", "Make it persuasive"), ("fix_grammar", "Fix grammar only"), ("summarize", "Summarise it"),
        ("continue", "Continue writing"), ("reply", "Write a reply"), ("outline", "Create an outline"), ("brainstorm", "Brainstorm ideas"),
    };

    RewriteResult result = null!;
    InputBox instruction = null!;

    public AssistantTab(IPanelActions host) : base(host)
    {
    }

    public override string Id => "assistant";
    public override string Label => "AI Assistant";
    public override string Glyph => Icons.Assistant;

    protected override FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Hint("Works on your selection, or on all the text if nothing is selected."));
        root.Children.Add(Heading("Quick actions"));
        var quick = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        foreach (var (task, label) in QuickActions) quick.Children.Add(UiKit.Spaced(UiKit.Button(label, () => Run(new WriteRequest(task, "")))));
        root.Children.Add(quick);

        root.Children.Add(Heading("Or tell NimbleWrite what to do", 4));
        instruction = new InputBox("Instruction for the AI", 4000, multiline: true, "e.g. Make this sound like a friendly update for customers");
        instruction.Margin = new Thickness(0, 4, 0, 6);
        instruction.Submitted += RunInstruction;
        root.Children.Add(instruction);
        var go = UiKit.Button("Run", RunInstruction, Kind.Primary, "Run the instruction (Ctrl+Enter)");
        go.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(go);

        result = new RewriteResult(Host);
        root.Children.Add(result);
        return root;
    }

    void Run(WriteRequest request) => result.Start(request);

    void RunInstruction()
    {
        string text = instruction.Text.Trim();
        if (text.Length == 0)
        {
            Host.Notice("Type what you'd like NimbleWrite to do first.");
            return;
        }
        Run(new WriteRequest("custom", "", Instruction: text));
    }

    public override void OnNewField() => result.Discard();
}
