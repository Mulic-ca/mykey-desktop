using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Markup;

namespace MyKey.Desktop;

public static class ThemeManager
{
    public static readonly ThemeChoice[] Choices =
    [
        new("light", "经典", "#15191F", "#F4F6F8"),
        new("dark", "深色", "#E1E6EB", "#191B1F"),
        new("jade", "青竹", "#237362", "#F3F7F5"),
        new("ocean", "晴蓝", "#326DAD", "#F3F6FA"),
        new("rose", "玫瑰", "#A04F68", "#FAF4F6")
    ];

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static void Apply(string id)
    {
        var choice = Choices.FirstOrDefault(t => t.Id == id) ?? Choices[0];
        var dark = choice.Id == "dark";
        var colors = new Dictionary<string, string>
        {
            ["Ink"] = dark ? "#ECEEF1" : "#15191F",
            ["Muted"] = dark ? "#A9B0BA" : "#6D737D",
            ["Panel"] = dark ? "#24272C" : "#FFFFFF",
            ["Canvas"] = choice.Canvas,
            ["Sidebar"] = dark ? "#202226" : choice.Id == "light" ? "#FBFCFC" : choice.Canvas,
            ["Subtle"] = dark ? "#30343B" : "#F3F5F6",
            ["SoftBorder"] = dark ? "#454B54" : "#E2E6EA",
            ["Primary"] = choice.Accent,
            ["OnPrimary"] = dark ? "#191B1F" : "#FFFFFF",
            ["Accent"] = dark ? "#7BCBAE" : choice.Id == "light" ? "#13795B" : choice.Accent,
            ["Danger"] = dark ? "#FF9191" : "#B42318",
            ["DangerSurface"] = dark ? "#472C30" : "#FFF0F0",
            ["ScrollThumb"] = dark ? "#626A77" : "#C9CED4",
            ["ScrollHover"] = dark ? "#838E9F" : "#AEB5BD",
            ["Overlay"] = dark ? "#66000000" : "#40F4F6F8"
        };
        foreach (var (key, hex) in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
                brush.Color = color;
            else
                Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }

    public static Brush Legacy(byte r, byte g, byte b)
    {
        if (r > g + 30 && r > b + 30) return Brush("Danger");
        var brightness = (r + g + b) / 3;
        return Brush(brightness < 90 ? "Ink" : brightness < 170 ? "Muted" : brightness < 235 ? "SoftBorder" : brightness < 253 ? "Subtle" : "Panel");
    }

    public static object Parse(string xaml)
    {
        // Dialog templates are constructed at runtime; resolve their legacy palette too.
        xaml = Regex.Replace(xaml, "(?<prefix>(?:Value|Background|Foreground|BorderBrush|Stroke|Fill)=\")(?<hex>#[0-9A-Fa-f]{6}|White)\"", match =>
        {
            var hex = match.Groups["hex"].Value;
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var foregroundWhite = hex == "White" && (match.Groups["prefix"].Value.StartsWith("Foreground") || xaml[..match.Index].EndsWith("Property=\"Foreground\" "));
            var brush = foregroundWhite ? Brush("OnPrimary") : Legacy(color.R, color.G, color.B);
            var key = Application.Current.Resources.Keys.Cast<object>().OfType<string>().First(k => ReferenceEquals(Application.Current.Resources[k], brush));
            return match.Groups["prefix"].Value + "{DynamicResource " + key + "}\"";
        });
        return XamlReader.Parse(xaml);
    }
}

public sealed record ThemeChoice(string Id, string Name, string Accent, string Canvas);
