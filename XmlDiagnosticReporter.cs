using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SilkyUIAnalyzer;

internal sealed class XmlDiagnosticReporter(string path, SourceText text, Action<Diagnostic> reportDiagnostic)
{
    public static readonly DiagnosticDescriptor InvalidXml = new(
        "SUI008", "XML 格式错误", "无法解析 XML：{0}",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerationFailed = new(
        "SUI009", "XML 代码生成失败", "无法生成 XML 初始化代码：{0}",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Report(DiagnosticDescriptor descriptor, XObject node, params object[] arguments) =>
        reportDiagnostic(Diagnostic.Create(descriptor, GetLocation(node), arguments));

    public void ReportXmlException(XmlException exception) => reportDiagnostic(Diagnostic.Create(
        InvalidXml, GetLocation(exception.LineNumber, exception.LinePosition), exception.Message));

    public Location GetLocation(XObject node) => node is IXmlLineInfo info && info.HasLineInfo()
        ? GetLocation(info.LineNumber, info.LinePosition)
        : GetLocation(1, 1);

    private Location GetLocation(int lineNumber, int linePosition)
    {
        var lineIndex = Math.Max(0, Math.Min(lineNumber - 1, text.Lines.Count - 1));
        var line = text.Lines[lineIndex];
        var position = line.Start + Math.Max(0, Math.Min(linePosition - 1, line.Span.Length));
        var span = new TextSpan(position, position < line.End ? 1 : 0);
        return Location.Create(path, span, text.Lines.GetLinePositionSpan(span));
    }
}
