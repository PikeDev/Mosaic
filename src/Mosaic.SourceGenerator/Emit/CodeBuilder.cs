using System.Text;

namespace Mosaic.SourceGenerator.Emit;

/// <summary>
/// Minimal indenting code writer. Avoids pulling in <c>System.CodeDom.Compiler.IndentedTextWriter</c>
/// to keep the generator dependency-light.
/// </summary>
internal sealed class CodeBuilder
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _atLineStart = true;

    public CodeBuilder Append(string text)
    {
        if (_atLineStart)
        {
            WriteIndent();
        }
        _sb.Append(text);
        _atLineStart = false;
        return this;
    }

    public CodeBuilder AppendLine(string text = "")
    {
        if (text.Length > 0)
        {
            if (_atLineStart)
            {
                WriteIndent();
            }
            _sb.Append(text);
        }
        _sb.Append('\n');
        _atLineStart = true;
        return this;
    }

    public CodeBuilder OpenBrace()
    {
        AppendLine("{");
        _indent++;
        return this;
    }

    public CodeBuilder CloseBrace()
    {
        _indent--;
        AppendLine("}");
        return this;
    }

    public CodeBuilder CloseBrace(string suffix)
    {
        _indent--;
        AppendLine("}" + suffix);
        return this;
    }

    public IndentScope Indent()
    {
        _indent++;
        return new IndentScope(this);
    }

    private void WriteIndent()
    {
        for (int i = 0; i < _indent; i++)
        {
            _sb.Append("    ");
        }
        _atLineStart = false;
    }

    public override string ToString() => _sb.ToString();

    internal struct IndentScope(CodeBuilder builder) : IDisposable
    {
        public void Dispose() => builder._indent--;
    }
}
