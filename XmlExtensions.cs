using System.Net.Security;
using System.Xml.Linq;

namespace SilkyUIAnalyzer;

internal static class XmlExtensions
{
    private static HashSet<string> SpecialAttributes { get; } = ["Name", "Class", "Style"];
    private static HashSet<string> SpecialElement { get; } = ["Style"];

    public static bool IsCommonAttribute(this XAttribute attribute) => !SpecialAttributes.Contains(attribute.Name.LocalName);

    /// <summary>
    /// 会过滤掉 Style 元素和 M. 开头的元素
    /// </summary>
    public static bool IsCommonElement(this XElement element)
    {
        var localName = element.Name.LocalName;

        if (localName.StartsWith("M.") ||
         SpecialElement.Contains(localName)) return false;

        return true;
    }
}
