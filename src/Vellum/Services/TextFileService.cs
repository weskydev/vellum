using System.Globalization;
using System.Text;
using Vellum.Models;

namespace Vellum.Services;

public sealed record LoadedText(string Text, Encoding Encoding, LineEnding LineEnding);

/// <summary>
/// Reads and writes text files, keeping their encoding and line endings.
/// Inside the editor every line break is a single '\r' (that is what TextBox uses).
/// </summary>
public static class TextFileService
{
    public static LoadedText Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        (Encoding encoding, int bomLength) = DetectEncoding(bytes);
        string raw = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        return new LoadedText(ToEditorText(raw), encoding, DetectLineEnding(raw));
    }

    /// <summary>Reads a file in an encoding the user picked (when the guess was wrong).</summary>
    public static LoadedText LoadWith(string path, Encoding encoding)
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[] bom = encoding.GetPreamble();
        int skip = bom.Length > 0 && bytes.AsSpan().StartsWith(bom) ? bom.Length : 0;
        string raw = encoding.GetString(bytes, skip, bytes.Length - skip);
        return new LoadedText(ToEditorText(raw), encoding, DetectLineEnding(raw));
    }

    /// <summary>Encodings offered in the status line, in menu order. Null marks a separator.</summary>
    public static readonly (int CodePage, bool Bom, string Name)?[] Choices =
    [
        (65001, false, "UTF-8"),
        (65001, true, "UTF-8 BOM"),
        (1200, true, "UTF-16 LE"),
        (1201, true, "UTF-16 BE"),
        null,
        (1251, false, "Windows-1251"),
        (20866, false, "KOI8-R"),
        (21866, false, "KOI8-U"),
        (866, false, "DOS-866"),
        (28595, false, "ISO-8859-5"),
        null,
        (1252, false, "Windows-1252"),
        (1250, false, "Windows-1250"),
        (1253, false, "Windows-1253"),
        (1254, false, "Windows-1254"),
        (28591, false, "ISO-8859-1"),
        null,
        (932, false, "Shift-JIS"),
        (936, false, "GBK"),
        (950, false, "Big5"),
        (949, false, "EUC-KR"),
    ];

    /// <summary>True if every character of the text survives saving in this encoding.</summary>
    public static bool CanEncode(string text, Encoding encoding)
    {
        if (encoding.CodePage is 65001 or 1200 or 1201) return true;
        try
        {
            var strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            strict.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public static bool SameEncoding(Encoding a, Encoding b) => a.CodePage == b.CodePage && HasBom(a) == HasBom(b);

    public static void Save(string path, string editorText, Encoding encoding, LineEnding lineEnding)
    {
        string text = lineEnding switch
        {
            LineEnding.LF => editorText.Replace('\r', '\n'),
            LineEnding.CR => editorText,
            _ => editorText.Replace("\r", "\r\n"),
        };

        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(text);
        byte[] bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);

        WriteAtomically(path, bytes);
    }

    /// <summary>
    /// Writes to a temp file first, so a crash never leaves a half-written file.
    /// With <paramref name="backup"/>, the previous version is kept as "file.bak" (used for Vellum's own data files).
    /// </summary>
    public static void WriteAtomically(string path, byte[] bytes, bool backup = false)
    {
        string temp = path + ".vellum-tmp";
        try
        {
            // Force the bytes onto the disk before swapping files: a power cut must not leave an empty file
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                File.Replace(temp, path, backup ? path + ".bak" : null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch (Exception) when (File.Exists(temp))
        {
            // Some drives don't support replace: fall back to a direct write
            File.Delete(temp);
            File.WriteAllBytes(path, bytes);
        }
    }

    public static string ToEditorText(string text) => text.Replace("\r\n", "\r").Replace('\n', '\r');

    public static LineEnding DetectLineEnding(string text)
    {
        int i = text.IndexOfAny(['\r', '\n']);
        if (i < 0) return LineEnding.CRLF;
        if (text[i] == '\n') return LineEnding.LF;
        return i + 1 < text.Length && text[i + 1] == '\n' ? LineEnding.CRLF : LineEnding.CR;
    }

    public static (Encoding Encoding, int BomLength) DetectEncoding(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (new UTF8Encoding(true), 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (new UnicodeEncoding(false, true), 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (new UnicodeEncoding(true, true), 2);

        try
        {
            new UTF8Encoding(false, true).GetString(b);
            return (new UTF8Encoding(false), 0);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8: most likely an old file in the system's ANSI code page (e.g. Windows-1251)
            return (Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage), 0);
        }
    }

    public static bool HasBom(Encoding encoding) => encoding.GetPreamble().Length > 0;

    public static Encoding FromCodePage(int codePage, bool bom) => codePage switch
    {
        65001 => new UTF8Encoding(bom),
        1200 => new UnicodeEncoding(false, bom),
        1201 => new UnicodeEncoding(true, bom),
        _ => Encoding.GetEncoding(codePage),
    };

    public static string Describe(Encoding encoding) => encoding.CodePage switch
    {
        65001 => HasBom(encoding) ? "UTF-8 BOM" : "UTF-8",
        1200 => "UTF-16 LE",
        1201 => "UTF-16 BE",
        1251 => "Windows-1251",
        866 => "DOS-866",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(encoding.WebName),
    };

    public static string Describe(LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.LF => "LF",
        LineEnding.CR => "CR",
        _ => "CRLF",
    };
}
