using System.Xml.Linq;

namespace SilkyUIAnalyzer;

internal static class XmlExtensions
{
    private static HashSet<string> SpecialElementHeader { get; } = ["Style", "M"];

    public const string SilkyUINamespace = "https://github.com/487666123/SilkyUIFramework";
    public const string BindingNamespace = "https://github.com/487666123/SilkyUIFramework/Binding";

    public static bool IsSuiNameSpace(this XName name) => name.NamespaceName == SilkyUINamespace;

    public static bool TryGetSuiAttribute(this XElement element, string localName, out XAttribute attribute)
    {
        attribute = element.Attributes()
            .FirstOrDefault(attr =>
                attr.Name.IsSuiNameSpace() &&
                string.Equals(attr.Name.LocalName, localName));
        return attribute != null;
    }

    public static HashSet<string> GetBindingPropertyNames(this IEnumerable<XAttribute> attributes)
    {
        var bindingPropertyNames = new HashSet<string>();

        // 记录所有 bind:* 的目标属性，后续遇到同名静态赋值时直接跳过。
        foreach (var attribute in attributes)
        {
            if (attribute.TryGetBindingPropertyName(out var propertyName))
            {
                bindingPropertyNames.Add(propertyName);
            }
        }

        return bindingPropertyNames;
    }

    /// <summary>
    /// 尝试获取绑定目标属性名称
    /// </summary>
    public static bool TryGetBindingPropertyName(this XAttribute attribute, out string propertyName)
    {
        if (!string.Equals(attribute.Name.NamespaceName, BindingNamespace, StringComparison.Ordinal))
        {
            propertyName = string.Empty;
            return false;
        }

        propertyName = attribute.Name.LocalName;
        return !string.IsNullOrWhiteSpace(propertyName);
    }
}
