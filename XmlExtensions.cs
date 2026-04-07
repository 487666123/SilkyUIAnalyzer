using System.Xml.Linq;

namespace SilkyUIAnalyzer;

internal static class XmlExtensions
{
    private static HashSet<string> SpecialAttributes { get; } = ["Name", "Class", "Style"];
    private static HashSet<string> SpecialElement { get; } = ["Style"];
    private static HashSet<string> SpecialElementHeader { get; } = ["Style", "M"];
    // Bind.Text="Title" 这种属性名使用 Bind. 前缀声明数据绑定。
    private const string BindingAttributePrefix = "Bind.";

    public static bool IsCommonAttribute(this XAttribute attribute) => !SpecialAttributes.Contains(attribute.Name.LocalName);

    /// <summary>
    /// 尝试获取绑定目标属性名称
    /// </summary>
    public static bool TryGetBindingTargetPropertyName(this XAttribute attribute, out string propertyName)
    {
        var localName = attribute.Name.LocalName;

        if (!localName.StartsWith(BindingAttributePrefix, StringComparison.Ordinal))
        {
            propertyName = string.Empty;
            return false;
        }

        propertyName = localName.Substring(BindingAttributePrefix.Length);
        return !string.IsNullOrWhiteSpace(propertyName);
    }

    /// <summary>
    /// 会过滤掉 Style 元素和 M. 开头的元素
    /// </summary>
    public static bool IsCommonElement(this XElement element)
    {
        var localName = element.Name.LocalName;

        if (SpecialElementHeader.Any(header => localName.StartsWith($"{header}."))) return false;
        if (SpecialElement.Contains(localName)) return false;

        return true;
    }
}
