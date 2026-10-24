using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace BindingAudit.Tests;

public class BindingAuditTests
{
    [Fact]
    public void Read_only_property_is_rejected_for_textbox_text()
    {
        var properties = BindingAudit.ParseProperties("""
            public string Log { get => _log; private set => Set(ref _log, value); }
            public string Host { get => _host; set => Set(ref _host, value); }
            public string Output { get => _output; private set => Set(ref _output, value); }
            public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
            """);

        Assert.False(properties["Log"]);
        Assert.True(properties["Host"]);
        Assert.False(properties["Output"]);
        Assert.True(properties["IsSelected"]);

        var issues = BindingAudit.Audit("""
            <TextBox Text="{Binding Log}"/>
            <TextBox Text="{Binding Host}"/>
            <TextBox Text="{Binding Log, Mode=OneWay}"/>
            <TextBox Text="{Binding Output, Mode=OneWay}" IsReadOnly="True"/>
            <CheckBox IsChecked="{Binding IsSelected}"/>
            <TextBlock Text="{Binding Output}"/>
            """, properties, "sample.xaml");

        var only = Assert.Single(issues);
        Assert.Contains("Log", only, StringComparison.Ordinal);
        Assert.Contains("TextBox.Text", only, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_setter_with_a_block_stays_read_only()
    {
        var properties = BindingAudit.ParseProperties("""
            public bool IsBusy
            {
                get => _busy;
                private set
                {
                    if (!Set(ref _busy, value))
                        return;
                    Raise(nameof(BoostRing));
                }
            }
            public Preset? SelectedPreset
            {
                get => _selected;
                set
                {
                    if (!Set(ref _selected, value) || value == null)
                        return;
                    Working = value.Tweaks.Clone();
                }
            }
            """);

        Assert.False(properties["IsBusy"]);
        Assert.True(properties["SelectedPreset"]);
    }

    [Fact]
    public void Every_page_binding_targets_a_known_writable_or_one_way_property()
    {
        var root = RepoRoot();
        var properties = BindingAudit.ReadProperties(Path.Combine(root, "src", "GameBooster"));
        Assert.True(properties.Count > 40, "the property scan returned too little to trust");
        Assert.False(properties["Log"]);
        Assert.False(properties["Output"]);

        var issues = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "GameBooster"), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            issues.AddRange(BindingAudit.Audit(File.ReadAllText(file), properties, Path.GetRelativePath(root, file)));
        }

        Assert.True(issues.Count == 0, string.Join(Environment.NewLine, issues));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "GameBooster", "GameBooster.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found from " + AppContext.BaseDirectory);
    }
}

public static class BindingAudit
{
    private static readonly Dictionary<string, string[]> TwoWayByDefault = new(StringComparer.Ordinal)
    {
        ["TextBox"] = ["Text"],
        ["ComboBox"] = ["SelectedItem", "SelectedValue", "Text"],
        ["ListBox"] = ["SelectedItem", "SelectedValue"],
        ["CheckBox"] = ["IsChecked"],
        ["PasswordBox"] = ["Password"],
        ["RadioButton"] = ["IsChecked"],
        ["Slider"] = ["Value"]
    };

    public static Dictionary<string, bool> ReadProperties(string sourceRoot)
    {
        var combined = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            combined.AppendLine(File.ReadAllText(file));
        }
        return ParseProperties(combined.ToString());
    }

    public static Dictionary<string, bool> ParseProperties(string code)
    {
        var stripped = Strip(code);
        var rx = new Regex(
            @"public\s+(?:(?:static|required|sealed|override|virtual|new|readonly|unsafe|abstract|extern|partial)\s+)*" +
            @"(?<head>[^{};]+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*" +
            @"(?<rest>=>\s*.+?;|\{\s*(?:get|set|init)\b(?:[^{}]|\{[^{}]*\})*\})",
            RegexOptions.Singleline);
        var properties = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (Match match in rx.Matches(stripped))
        {
            var head = match.Groups["head"].Value;
            if (Regex.IsMatch(head, @"\b(class|struct|record|interface|enum|namespace|event)\b"))
                continue;
            var name = match.Groups["name"].Value;
            var writable = IsWritable(match.Groups["rest"].Value);
            if (properties.TryGetValue(name, out var already))
                properties[name] = already || writable;
            else
                properties[name] = writable;
        }
        return properties;
    }

    public static List<string> Audit(string xaml, IReadOnlyDictionary<string, bool> properties, string file)
    {
        var issues = new List<string>();
        var index = 0;
        while (true)
        {
            var start = xaml.IndexOf("{Binding", index, StringComparison.Ordinal);
            if (start < 0)
                break;
            var end = EndOfMarkup(xaml, start);
            if (end < 0)
                break;
            var expr = xaml[start..(end + 1)];
            index = end + 1;
            if (expr.Contains("RelativeSource", StringComparison.Ordinal) ||
                expr.Contains("ElementName", StringComparison.Ordinal) ||
                Regex.IsMatch(expr, @"\bSource\s*="))
                continue;

            var path = BindingPath(expr);
            if (path == null)
                continue;
            var leaf = path.Split('.')[^1];
            if (!TryHost(xaml, start, out var element, out var attribute))
                continue;
            var mode = BindingMode(expr);
            var twoWay = mode switch
            {
                "TwoWay" or "OneWayToSource" => true,
                "OneWay" or "OneTime" => false,
                _ => TwoWayByDefault.TryGetValue(element, out var names) && names.Contains(attribute)
            };

            if (!properties.TryGetValue(leaf, out var writable))
            {
                issues.Add($"{file}: {element}.{attribute} binds '{path}', which is not a parsed property");
                continue;
            }
            if (twoWay && !writable)
                issues.Add($"{file}: {element}.{attribute} binds read-only '{leaf}' without Mode=OneWay ({expr})");
        }
        return issues;
    }

    private static bool IsWritable(string rest)
    {
        if (rest.TrimStart().StartsWith("=>", StringComparison.Ordinal))
            return false;
        foreach (Match match in Regex.Matches(rest, @"(?<mod>private|protected|internal)?\s*(?<acc>get|set|init)\b"))
        {
            if (match.Groups["acc"].Value == "set" && !match.Groups["mod"].Success)
                return true;
        }
        return false;
    }

    private static string? BindingPath(string expr)
    {
        var path = Regex.Match(expr, @"\bPath\s*=\s*([A-Za-z_][A-Za-z0-9_\.]*)");
        if (path.Success)
            return path.Groups[1].Value;
        var bare = Regex.Match(expr, @"^\{Binding\s+([A-Za-z_][A-Za-z0-9_\.]*)");
        return bare.Success ? bare.Groups[1].Value : null;
    }

    private static string? BindingMode(string expr)
    {
        var mode = Regex.Match(expr, @"\bMode\s*=\s*(OneWayToSource|TwoWay|OneTime|OneWay)\b");
        return mode.Success ? mode.Groups[1].Value : null;
    }

    private static bool TryHost(string xaml, int bindingStart, out string element, out string attribute)
    {
        element = "";
        attribute = "";
        var i = bindingStart - 1;
        while (i >= 0 && char.IsWhiteSpace(xaml[i]))
            i--;
        if (i < 0 || (xaml[i] != '"' && xaml[i] != '\''))
            return false;
        i--;
        while (i >= 0 && char.IsWhiteSpace(xaml[i]))
            i--;
        if (i < 0 || xaml[i] != '=')
            return false;
        i--;
        while (i >= 0 && char.IsWhiteSpace(xaml[i]))
            i--;
        var nameEnd = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(xaml[i]) || xaml[i] is '_' or '.' or ':'))
            i--;
        attribute = xaml[(i + 1)..nameEnd];
        if (attribute.Contains(':'))
            attribute = attribute.Split(':')[^1];
        if (attribute.Contains('.'))
            attribute = attribute.Split('.')[^1];

        var j = i;
        while (j >= 0 && xaml[j] != '<' && xaml[j] != '>')
            j--;
        if (j < 0 || xaml[j] != '<')
            return false;
        j++;
        if (j >= xaml.Length || xaml[j] is '/' or '!')
            return false;
        var start = j;
        while (j < xaml.Length && (char.IsLetterOrDigit(xaml[j]) || xaml[j] is '_' or '.' or ':'))
            j++;
        element = xaml[start..j];
        if (element.Contains(':'))
            element = element.Split(':')[^1];
        return element.Length > 0 && attribute.Length > 0;
    }

    private static int EndOfMarkup(string xaml, int open)
    {
        var depth = 0;
        for (var i = open; i < xaml.Length; i++)
        {
            if (xaml[i] == '{')
                depth++;
            else if (xaml[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    private static string Strip(string code)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n')
                    i++;
                sb.Append('\n');
                continue;
            }
            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/'))
                    i++;
                continue;
            }
            if (code[i] == '"')
            {
                sb.Append("\"\"");
                i++;
                while (i < code.Length && code[i] != '"')
                {
                    if (code[i] == '\\' && i + 1 < code.Length)
                        i++;
                    i++;
                }
                continue;
            }
            if (code[i] == '\'')
            {
                sb.Append("''");
                i++;
                while (i < code.Length && code[i] != '\'')
                {
                    if (code[i] == '\\' && i + 1 < code.Length)
                        i++;
                    i++;
                }
                continue;
            }
            sb.Append(code[i]);
        }
        return sb.ToString();
    }
}
