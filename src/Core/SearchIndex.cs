using ToolGood.Words.Pinyin;

namespace MyKey.Desktop;

public static class SearchIndex
{
    public static string Build(params string?[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        if (parts.Length == 0)
            return "";

        var original = string.Join(' ', parts);
        var chineseText = string.Join(' ', parts.Where(ContainsChinese));
        if (string.IsNullOrEmpty(chineseText))
            return original;

        var fullPinyin = WordsHelper.GetPinyin(chineseText);
        var firstPinyin = WordsHelper.GetFirstPinyin(chineseText);
        return $"{original} {fullPinyin} {firstPinyin}";
    }

    public static bool Matches(string searchText, string keyword)
    {
        var terms = keyword
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length == 0 || terms.All(term =>
            searchText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsChinese(string value)
    {
        return value.Any(character =>
            character is >= '\u3400' and <= '\u4DBF' or
            >= '\u4E00' and <= '\u9FFF');
    }
}
