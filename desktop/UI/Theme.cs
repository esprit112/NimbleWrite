using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace NimbleWrite.Desktop.UI;

/// <summary>Colours shared with the web app, in a light and a dark palette (picked from the Windows app theme).</summary>
internal static class Theme
{
    public static bool Dark { get; private set; }
    public static Brush Surface { get; private set; } = Brushes.White;
    public static Brush Raised { get; private set; } = Brushes.White;
    public static Brush Text { get; private set; } = Brushes.Black;
    public static Brush Muted { get; private set; } = Brushes.Gray;
    public static Brush Line { get; private set; } = Brushes.LightGray;
    public static Brush Hover { get; private set; } = Brushes.WhiteSmoke;
    public static Brush Subtle { get; private set; } = Brushes.WhiteSmoke;
    public static Brush Brand { get; private set; } = Brushes.Green;
    public static Brush BrandHover { get; private set; } = Brushes.Green;
    public static Brush BrandText { get; private set; } = Brushes.Green;
    public static Brush OnBrand { get; } = Brushes.White;

    public static void Load()
    {
        Dark = ReadDark();
        if (Dark)
        {
            Surface = B("#1C211F"); Raised = B("#242A27"); Text = B("#EEF2F0"); Muted = B("#A5AFAA"); Line = B("#353C39");
            Hover = B("#2E3532"); Subtle = B("#272E2B"); Brand = B("#16824A"); BrandHover = B("#1C9757"); BrandText = B("#5FD394");
        }
        else
        {
            Surface = B("#FFFFFF"); Raised = B("#F6F8F7"); Text = B("#17201B"); Muted = B("#5C6862"); Line = B("#E2E7E4");
            Hover = B("#EAF0EC"); Subtle = B("#F1F4F2"); Brand = B("#0B6B3A"); BrandHover = B("#0E7D45"); BrandText = B("#0B6B3A");
        }
    }

    static bool ReadDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static Color CategoryColor(string category) => category switch
    {
        "correctness" => C("#E5484D"),
        "clarity" => C("#3E63DD"),
        "engagement" => C("#18A058"),
        "delivery" => C("#8E4EC6"),
        _ => C("#0B6B3A"),
    };

    public static Brush CategoryBrush(string category) => Frozen(new SolidColorBrush(CategoryColor(category)));

    public static string CategoryLabel(string category) => category switch
    {
        "correctness" => "Correctness",
        "clarity" => "Clarity",
        "engagement" => "Engagement",
        "delivery" => "Delivery",
        _ => "Suggestion",
    };

    public static int CategoryRank(string category) => category switch
    {
        "correctness" => 0, "clarity" => 1, "engagement" => 2, "delivery" => 3, _ => 4,
    };

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static SolidColorBrush B(string hex) => Frozen(new SolidColorBrush(C(hex)));

    static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
