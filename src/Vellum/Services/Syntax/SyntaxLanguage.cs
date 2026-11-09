namespace Vellum.Services.Syntax;

/// <summary>What a piece of text is, so the editor knows how to paint it.</summary>
public enum TokenKind : byte
{
    // Code
    Comment,
    String,
    Number,
    Keyword,
    Type,
    Function,
    Tag,
    Attribute,
    Key,
    Meta,
    Error,
    Warning,

    // Markdown
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    Marker,
    Bold,
    Italic,
    BoldItalic,
    Strike,
    CodeInline,
    CodeBlock,
    Link,
    Quote,
    ListMarker,
}

public readonly record struct Token(int Start, int Length, TokenKind Kind)
{
    public int End => Start + Length;
}

/// <summary>A language the editor can highlight.</summary>
public sealed class SyntaxLanguage
{
    private readonly Func<string, List<Token>> _tokenize;

    private SyntaxLanguage(string id, string name, bool isCode, string[] extensions, Func<string, List<Token>> tokenize)
    {
        Id = id;
        Name = name;
        IsCode = isCode;
        Extensions = extensions;
        _tokenize = tokenize;
    }

    public string Id { get; }

    /// <summary>Shown in the status line. Plain text and Markdown are translated, code names are not.</summary>
    public string Name { get; }

    /// <summary>Code gets a monospace font (if enabled); Markdown and plain text keep the reading font.</summary>
    public bool IsCode { get; }

    public bool IsMarkdown => Id == "markdown";

    public bool IsPlain => Id == "text";

    public string[] Extensions { get; }

    public string DisplayName => Id switch
    {
        "text" => Strings.Get("SyntaxPlain"),
        _ => Name,
    };

    public List<Token> Tokenize(string text) => _tokenize(text);

    public static readonly SyntaxLanguage Plain = new("text", "Text", false, [".txt"], _ => []);

    public static readonly IReadOnlyList<SyntaxLanguage> All =
    [
        Plain,
        new("markdown", "Markdown", false, [".md", ".markdown", ".mdx"], MarkdownTokenizer.Tokenize),
        new("csharp", "C#", true, [".cs", ".csx"], CodeRules.CSharp.Tokenize),
        new("javascript", "JavaScript", true, [".js", ".mjs", ".cjs", ".jsx"], CodeRules.JavaScript.Tokenize),
        new("typescript", "TypeScript", true, [".ts", ".tsx", ".mts", ".cts"], CodeRules.TypeScript.Tokenize),
        new("python", "Python", true, [".py", ".pyw", ".pyi"], CodeRules.Python.Tokenize),
        new("json", "JSON", true, [".json", ".jsonc", ".json5", ".webmanifest"], CodeRules.Json.Tokenize),
        new("xml", "XML / HTML", true, [".xml", ".xaml", ".html", ".htm", ".svg", ".csproj", ".props", ".targets", ".config", ".resw", ".resx", ".appxmanifest", ".slnx", ".vue"], XmlTokenizer.Tokenize),
        new("css", "CSS", true, [".css", ".scss", ".less"], CodeRules.Css.Tokenize),
        new("yaml", "YAML", true, [".yml", ".yaml"], CodeRules.Yaml.Tokenize),
        new("ini", "INI / TOML", true, [".ini", ".cfg", ".conf", ".toml", ".properties", ".editorconfig", ".gitconfig", ".env"], CodeRules.Ini.Tokenize),
        new("sql", "SQL", true, [".sql"], CodeRules.Sql.Tokenize),
        new("powershell", "PowerShell", true, [".ps1", ".psm1", ".psd1"], CodeRules.PowerShell.Tokenize),
        new("shell", "Shell", true, [".sh", ".bash", ".zsh"], CodeRules.Shell.Tokenize),
        new("batch", "Batch", true, [".bat", ".cmd"], CodeRules.Batch.Tokenize),
        new("c", "C / C++", true, [".c", ".h", ".cpp", ".hpp", ".cc", ".cxx", ".hh"], CodeRules.CLike.Tokenize),
        new("java", "Java / Kotlin", true, [".java", ".kt", ".kts", ".gradle", ".scala"], CodeRules.CLike.Tokenize),
        new("go", "Go", true, [".go"], CodeRules.CLike.Tokenize),
        new("rust", "Rust", true, [".rs"], CodeRules.CLike.Tokenize),
        new("swift", "Swift / Dart", true, [".swift", ".dart"], CodeRules.CLike.Tokenize),
        new("php", "PHP", true, [".php"], CodeRules.CLike.Tokenize),
        new("log", "Log", false, [".log"], CodeRules.Log.Tokenize),
    ];

    public static SyntaxLanguage FromId(string? id) => All.FirstOrDefault(l => l.Id == id) ?? Plain;

    public static SyntaxLanguage ForFile(string? path)
    {
        if (path is null) return Plain;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext.Length == 0)
        {
            ext = Path.GetFileName(path).ToLowerInvariant(); // ".gitignore"-like names, "Dockerfile"
        }
        return All.FirstOrDefault(l => l.Extensions.Contains(ext)) ?? Plain;
    }

    /// <summary>Language for the info string of a Markdown code block, like ```js.</summary>
    public static SyntaxLanguage? ForFence(string info)
    {
        string name = info.Trim().ToLowerInvariant();
        if (name.Length == 0) return null;
        string ext = name switch
        {
            "c#" or "cs" or "csharp" => ".cs",
            "js" or "javascript" or "jsx" or "node" => ".js",
            "ts" or "typescript" or "tsx" => ".ts",
            "py" or "python" => ".py",
            "json" or "jsonc" => ".json",
            "xml" or "html" or "xaml" or "svg" or "vue" => ".xml",
            "css" or "scss" or "less" => ".css",
            "yml" or "yaml" => ".yml",
            "ini" or "toml" or "cfg" => ".ini",
            "sql" => ".sql",
            "ps" or "ps1" or "powershell" or "pwsh" => ".ps1",
            "sh" or "bash" or "shell" or "zsh" or "console" => ".sh",
            "bat" or "cmd" or "batch" => ".bat",
            "c" or "cpp" or "c++" or "h" => ".c",
            "java" or "kotlin" or "kt" or "scala" => ".java",
            "go" or "golang" => ".go",
            "rust" or "rs" => ".rs",
            "swift" or "dart" => ".swift",
            "php" => ".php",
            "md" or "markdown" => ".md",
            "log" => ".log",
            _ => "",
        };
        if (ext.Length == 0) return null;
        return All.FirstOrDefault(l => l.Extensions.Contains(ext));
    }
}
