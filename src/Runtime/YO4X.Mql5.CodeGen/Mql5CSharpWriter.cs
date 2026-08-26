using System.Globalization;
using System.Text;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// An indenting text sink that also tracks emitted <c>#line</c> state.
///
/// Line directives are deduplicated: re-stating the position the compiler already
/// believes it is at would add noise without adding information, and would make the
/// output depend on how many times a caller happened to ask. Suppressing the
/// redundant ones keeps emission deterministic and the mapping readable.
/// </summary>
internal sealed class Mql5CSharpWriter
{
    private const string IndentUnit = "    ";
    /// <summary>
    /// Emission always uses LF, never the platform separator: generated source has to
    /// be byte-identical for the same module wherever it is produced.
    /// </summary>
    private const char NewLine = '\n';

    private readonly StringBuilder _builder = new();
    private readonly string _sourcePath;
    private int _indent;
    private int _lastDirectiveLine = -1;

    public Mql5CSharpWriter(string sourcePath)
    {
        _sourcePath = SanitizePath(sourcePath);
    }

    /// <summary>Increases the indent by one level.</summary>
    public void Indent() => _indent++;

    /// <summary>Decreases the indent by one level, never below zero.</summary>
    public void Outdent() => _indent = _indent == 0 ? 0 : _indent - 1;

    /// <summary>Writes one indented line.</summary>
    public void Line(string text)
    {
        if (text.Length != 0)
        {
            for (int level = 0; level < _indent; level++)
            {
                _builder.Append(IndentUnit);
            }

            _builder.Append(text);
        }

        _builder.Append(NewLine);
    }

    /// <summary>Writes an empty line.</summary>
    public void Blank() => _builder.Append(NewLine);

    /// <summary>Writes <c>{</c> and indents.</summary>
    public void OpenBrace()
    {
        Line("{");
        Indent();
    }

    /// <summary>Writes <c>}</c> after outdenting.</summary>
    public void CloseBrace()
    {
        Outdent();
        Line("}");
    }

    /// <summary>
    /// Emits <c>#line n "path"</c> so that a fault in the generated strategy points at
    /// the original .mq5 position. A non-positive line, or a repeat of the position
    /// already in force, writes nothing.
    /// </summary>
    public void LineDirective(int line)
    {
        if (line <= 0 || line == _lastDirectiveLine)
        {
            return;
        }

        _lastDirectiveLine = line;
        _builder
            .Append("#line ")
            .Append(line.ToString(CultureInfo.InvariantCulture))
            .Append(" \"")
            .Append(_sourcePath)
            .Append('"')
            .Append(NewLine);
    }

    /// <summary>Returns line mapping to the generated file itself.</summary>
    public void EndLineDirectives()
    {
        if (_lastDirectiveLine < 0)
        {
            return;
        }

        _lastDirectiveLine = -1;
        _builder.Append("#line default").Append(NewLine);
    }

    /// <summary>The accumulated source text.</summary>
    public override string ToString() => _builder.ToString();

    /// <summary>
    /// A <c>#line</c> file name is delimited by quotes and is not otherwise escaped by
    /// the C# lexer, so an embedded quote would terminate it early. Backslashes are
    /// normalised to forward slashes so that the same module emits the same text on
    /// every operating system.
    /// </summary>
    private static string SanitizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "unknown.mq5";
        }

        var sanitized = new StringBuilder(path.Length);
        foreach (char character in path)
        {
            if (character is '"' or '\r' or '\n')
            {
                continue;
            }

            sanitized.Append(character == (char)92 ? '/' : character);
        }

        return sanitized.Length == 0 ? "unknown.mq5" : sanitized.ToString();
    }
}
