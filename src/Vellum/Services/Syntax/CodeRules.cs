using System.Text.RegularExpressions;

namespace Vellum.Services.Syntax;

/// <summary>
/// Regex-based highlighting: a list of (pattern, kind) rules joined into one expression.
/// Earlier rules win, so a "//" inside a string stays part of the string.
/// </summary>
public sealed class RegexRules
{
    private readonly Regex _regex;
    private readonly TokenKind[] _kinds;

    public RegexRules(params (string Pattern, TokenKind Kind)[] rules)
    {
        _kinds = rules.Select(r => r.Kind).ToArray();
        string joined = string.Join("|", rules.Select((r, i) => $"(?<g{i}>{r.Pattern})"));
        _regex = new Regex(joined, RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(2));
    }

    public List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        try
        {
            for (Match m = _regex.Match(text); m.Success; m = m.NextMatch())
            {
                if (m.Length == 0) continue;
                for (int i = 0; i < _kinds.Length; i++)
                {
                    if (m.Groups[i + 1].Success)
                    {
                        tokens.Add(new Token(m.Index, m.Length, _kinds[i]));
                        break;
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological text: keep what we have
        }
        return tokens;
    }
}

/// <summary>Rules for every supported code language.</summary>
public static class CodeRules
{
    private static string Words(string list) => @"\b(?:" + string.Join("|", list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + @")\b";

    private static string WordsIgnoreCase(string list) => "(?i:" + Words(list) + ")";

    // Shared pieces
    private const string LineComment = @"//[^\r]*";
    private const string BlockComment = @"/\*[\s\S]*?(?:\*/|\z)";
    private const string HashComment = @"#[^\r]*";
    private const string DoubleQuoted = "\"(?:[^\"\\\\\\r]|\\\\.)*\"?";
    private const string SingleQuoted = "'(?:[^'\\\\\\r]|\\\\.)*'?";
    private const string BackQuoted = "`(?:[^`\\\\]|\\\\[\\s\\S])*`?";
    private const string Number = @"(?<![\w.])(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][+-]?\d+)?)[a-zA-Z]{0,3}\b";
    private const string PascalType = @"\b[A-Z][A-Za-z0-9_]*\b";
    private const string FunctionCall = @"\b[a-z_][A-Za-z0-9_]*(?=\s*\()";

    public static readonly RegexRules CSharp = new(
        (LineComment, TokenKind.Comment),
        (BlockComment, TokenKind.Comment),
        ("\\$?@\"(?:[^\"]|\"\")*\"?", TokenKind.String),
        ("\"\"\"[\\s\\S]*?(?:\"\"\"|\\z)", TokenKind.String),
        ("\\$?" + DoubleQuoted, TokenKind.String),
        (SingleQuoted, TokenKind.String),
        (@"^[ \t]*#[ \t]*\w+[^\r]*", TokenKind.Meta),
        (@"\[(?=[A-Z]\w*(?:\(|\]))", TokenKind.Meta),
        (Number, TokenKind.Number),
        (Words("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while var async await record init get set yield when where nameof dynamic partial required with not and or global file value nint nuint add remove"), TokenKind.Keyword),
        (PascalType, TokenKind.Type),
        (FunctionCall, TokenKind.Function));

    private const string JsKeywords = "break case catch class const continue debugger default delete do else export extends false finally for function if import in instanceof let new null return super switch this throw true try typeof var void while with yield async await of static get set undefined from as";

    public static readonly RegexRules JavaScript = new(
        (LineComment, TokenKind.Comment),
        (BlockComment, TokenKind.Comment),
        (BackQuoted, TokenKind.String),
        (DoubleQuoted, TokenKind.String),
        (SingleQuoted, TokenKind.String),
        (@"@\w+", TokenKind.Meta),
        (Number, TokenKind.Number),
        (Words(JsKeywords), TokenKind.Keyword),
        (PascalType, TokenKind.Type),
        (FunctionCall, TokenKind.Function));

    public static readonly RegexRules TypeScript = new(
        (LineComment, TokenKind.Comment),
        (BlockComment, TokenKind.Comment),
        (BackQuoted, TokenKind.String),
        (DoubleQuoted, TokenKind.String),
        (SingleQuoted, TokenKind.String),
        (@"@\w+", TokenKind.Meta),
        (Number, TokenKind.Number),
        (Words(JsKeywords + " type interface enum implements private public protected readonly declare namespace keyof any number string boolean never unknown abstract satisfies infer is asserts module"), TokenKind.Keyword),
        (PascalType, TokenKind.Type),
        (FunctionCall, TokenKind.Function));

    public static readonly RegexRules CLike = new(
        (LineComment, TokenKind.Comment),
        (BlockComment, TokenKind.Comment),
        ("\"\"\"[\\s\\S]*?(?:\"\"\"|\\z)", TokenKind.String),
        (BackQuoted, TokenKind.String),
        (DoubleQuoted, TokenKind.String),
        (SingleQuoted, TokenKind.String),
        (@"^[ \t]*#[ \t]*\w+[^\r]*", TokenKind.Meta),
        (@"@\w+", TokenKind.Meta),
        (@"\$\w+", TokenKind.Key),
        (Number, TokenKind.Number),
        (Words("if else for while do switch case default break continue return goto try catch finally throw throws new delete class struct enum interface union typedef namespace using import package public private protected internal static const final readonly virtual override abstract sealed extern inline volatile mutable true false null nil void int long short char byte float double bool boolean string var let val fun func fn def mut impl trait pub use mod crate self super this match where async await yield in is as typeof sizeof instanceof unsafe dyn move ref go defer chan select range map type lambda auto signed unsigned template typename operator friend explicit export extends implements guard init protocol extension some any weak lazy nullptr constexpr noexcept echo function foreach elseif endif require include object companion data sealed open lateinit when loop"), TokenKind.Keyword),
        (PascalType, TokenKind.Type),
        (FunctionCall, TokenKind.Function));

    public static readonly RegexRules Python = new(
        (HashComment, TokenKind.Comment),
        ("(?i:[rbuf]{0,2})(?:\"\"\"[\\s\\S]*?(?:\"\"\"|\\z)|'''[\\s\\S]*?(?:'''|\\z))", TokenKind.String),
        ("(?i:[rbuf]{0,2})(?:" + DoubleQuoted + "|" + SingleQuoted + ")", TokenKind.String),
        (@"^[ \t]*@[\w.]+", TokenKind.Meta),
        (Number, TokenKind.Number),
        (Words("and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield match case self print len range"), TokenKind.Keyword),
        (PascalType, TokenKind.Type),
        (FunctionCall, TokenKind.Function));

    public static readonly RegexRules Json = new(
        (LineComment, TokenKind.Comment),
        (BlockComment, TokenKind.Comment),
        (DoubleQuoted + @"(?=\s*:)", TokenKind.Key),
        (DoubleQuoted, TokenKind.String),
        (@"-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", TokenKind.Number),
        (Words("true false null"), TokenKind.Keyword));

    public static readonly RegexRules Css = new(
        (BlockComment, TokenKind.Comment),
        (LineComment, TokenKind.Comment),
        (DoubleQuoted, TokenKind.String),
        (SingleQuoted, TokenKind.String),
        (@"@[\w-]+", TokenKind.Meta),
        (@"#[0-9a-fA-F]{3,8}\b", TokenKind.Number),
        (@"(?<![\w-])-?\d*\.?\d+(?:%|[a-zA-Z]+)?", TokenKind.Number),
        (@"!important\b", TokenKind.Keyword),
        (@"(?<=[{;\s])--?[\w-]+(?=\s*:)", TokenKind.Key),
        (@"(?<=[{;]\s*)[\w-]+(?=\s*:)", TokenKind.Key),
        (@"^[ \t]*[\w-]+(?=\s*:[^\r{]*;)", TokenKind.Key),
        (@"[.#][\w-]+", TokenKind.Type),
        (@"\b[a-z-]+(?=\()", TokenKind.Function));

    public static readonly RegexRules Yaml = new(
        (@"(?<=^|\s)#[^\r]*", TokenKind.Comment),
        (@"^(?:---|\.\.\.)[ \t]*$", TokenKind.Meta),
        (@"^[ \t]*(?:-[ \t]+)?[^\s#:\r][^:\r]*?(?=[ \t]*:(?:[ \t]|$))", TokenKind.Key),
        (DoubleQuoted, TokenKind.String),
        (SingleQuoted, TokenKind.String),
        (@"[&*][\w-]+", TokenKind.Meta),
        (@"![\w!-]+", TokenKind.Meta),
        (@"(?<=[:\-\[,][ \t]*)-?\d+(?:\.\d+)?\b", TokenKind.Number),
        (Words("true false null yes no on off True False Null"), TokenKind.Keyword),
        (@"^[ \t]*-(?=[ \t])", TokenKind.Keyword));

    public static readonly RegexRules Ini = new(
        (@"^[ \t]*[;#][^\r]*", TokenKind.Comment),
        (@"^[ \t]*\[[^\]\r]*\]", TokenKind.Tag),
        (@"^[ \t]*[\w.\-""' ]+?(?=[ \t]*[=:])", TokenKind.Key),
        (DoubleQuoted, TokenKind.String),
        (SingleQuoted, TokenKind.String),
        (@"(?<==[ \t]*)-?\d+(?:\.\d+)?\b", TokenKind.Number),
        (Words("true false yes no on off"), TokenKind.Keyword));

    public static readonly RegexRules Sql = new(
        (@"--[^\r]*", TokenKind.Comment),
        (BlockComment, TokenKind.Comment),
        ("'(?:[^']|'')*'?", TokenKind.String),
        ("\"[^\"\\r]*\"?", TokenKind.Key),
        (@"\[[^\]\r]*\]", TokenKind.Key),
        (@"@@?\w+", TokenKind.Meta),
        (Number, TokenKind.Number),
        (WordsIgnoreCase("select from where insert update delete into values create table alter drop index join left right inner outer full cross on group by order having limit offset top as and or not null is in exists like between case when then else end distinct union all primary key foreign references default set begin commit rollback transaction view procedure function returns return declare if while with asc desc count sum avg min max cast convert unique check constraint database schema grant revoke truncate merge using over partition"), TokenKind.Keyword),
        (WordsIgnoreCase("int integer bigint smallint tinyint varchar nvarchar char nchar text ntext boolean bit date datetime datetime2 time timestamp decimal numeric float real money uuid uniqueidentifier json blob serial"), TokenKind.Type),
        (FunctionCall, TokenKind.Function));

    public static readonly RegexRules PowerShell = new(
        (@"<#[\s\S]*?(?:#>|\z)", TokenKind.Comment),
        (HashComment, TokenKind.Comment),
        ("@\"[\\s\\S]*?(?:^\"@|\\z)", TokenKind.String),
        ("@'[\\s\\S]*?(?:^'@|\\z)", TokenKind.String),
        ("\"(?:[^\"`\\r]|`.)*\"?", TokenKind.String),
        ("'(?:[^'\\r]|'')*'?", TokenKind.String),
        (@"\$(?:\{[^}\r]*\}|[\w:]+)", TokenKind.Key),
        (@"\[[\w.]+(?:\[\])?\]", TokenKind.Type),
        (@"(?<=\s)-[A-Za-z]\w*", TokenKind.Attribute),
        (Number, TokenKind.Number),
        (WordsIgnoreCase("if else elseif foreach for while do until switch function filter param return begin process end try catch finally throw break continue in trap exit class enum using"), TokenKind.Keyword),
        (@"\b[A-Z][a-z]+-[A-Z]\w+", TokenKind.Function));

    public static readonly RegexRules Shell = new(
        (@"(?<=^|\s)#[^\r]*", TokenKind.Comment),
        (DoubleQuoted, TokenKind.String),
        ("'[^'\\r]*'?", TokenKind.String),
        (@"\$(?:\{[^}\r]*\}|\w+|[@#?$!*-])", TokenKind.Key),
        (@"(?<=\s)--?[A-Za-z][\w-]*", TokenKind.Attribute),
        (Number, TokenKind.Number),
        (Words("if then else elif fi for while until do done case esac function in return export local readonly declare select time sudo source alias unset shift exit"), TokenKind.Keyword),
        (@"^[ \t]*[\w-]+(?=\s*\(\))", TokenKind.Function));

    public static readonly RegexRules Batch = new(
        (@"^[ \t]*(?i:rem\b|::)[^\r]*", TokenKind.Comment),
        (@"^[ \t]*:\w+", TokenKind.Tag),
        ("\"[^\"\\r]*\"?", TokenKind.String),
        (@"%~?\w+%?|%%\w|![\w]+!", TokenKind.Key),
        (WordsIgnoreCase("if else for in do goto call set echo exit not exist defined errorlevel setlocal endlocal enabledelayedexpansion off on start pushd popd shift equ neq lss leq gtr geq"), TokenKind.Keyword),
        (Number, TokenKind.Number));

    public static readonly RegexRules Log = new(
        (@"\b\d{4}-\d\d-\d\d(?:[T ]\d\d:\d\d(?::\d\d)?(?:[.,]\d+)?(?:Z|[+-]\d\d:?\d\d)?)?|\b\d\d:\d\d:\d\d(?:[.,]\d+)?", TokenKind.Comment),
        (@"(?i:\b(?:error|err|fatal|critical|crit|exception|fail(?:ed|ure)?|panic)\b)", TokenKind.Error),
        (@"(?i:\b(?:warn(?:ing)?)\b)", TokenKind.Warning),
        (@"(?i:\b(?:info|notice)\b)", TokenKind.Key),
        (@"(?i:\b(?:debug|trace|verbose)\b)", TokenKind.Comment),
        (DoubleQuoted, TokenKind.String),
        (@"\bhttps?://[^\s""'<>]+", TokenKind.Function));
}
