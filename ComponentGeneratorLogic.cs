using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

/// <summary>
/// 组件生成器逻辑类，负责将 XML 元素映射为 C# 代码
/// </summary>
/// <param name="aliasToTypeSymbolMapping">别名到类型符号的映射字典</param>
internal class ComponentGeneratorLogic(ImmutableDictionary<string, INamedTypeSymbol> aliasToTypeSymbolMapping)
{
    private int _variableCounter = 0;

    private readonly HashSet<string> ValidMemberName = [];

    public Dictionary<string, XAttribute[]> StaticStyles { get; } = [];

    private ImmutableDictionary<string, INamedTypeSymbol> AliasToTypeSymbolMapping { get; } = aliasToTypeSymbolMapping;

    public bool TryGetNamedTypeSymbol(XElement element, out INamedTypeSymbol namedType)
    {
        return AliasToTypeSymbolMapping.TryGetValue(element.Name.LocalName, out namedType);
    }

    /// <summary>
    /// 生成完整的UI组件代码，包括命名空间、类定义和初始化方法
    /// </summary>
    public string GenerateComponentCode(XElement root, INamedTypeSymbol typeSymbol)
    {
        try
        {
            CollectStaticStyles(root);

            var code = new StringBuilder().AppendLine(
                $$"""
                  namespace {{typeSymbol.ContainingNamespace.ToDisplayString()}}
                  {
                      {{typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant()}} partial class {{typeSymbol.Name}}
                      {
                          // GeneratePropertyDeclarationsRecursively
                  {{GeneratePropertyDeclarationsRecursively(root, 8)}}

                          private bool _contentLoaded;

                          private void InitializeComponent()
                          {
                              if (_contentLoaded) return;
                              _contentLoaded = true;

                              // GenerateElementInitialization
                  {{GenerateElementInitialization(typeSymbol, root, "this", 12)}}
                  """);

            return code.AppendLine(
                $$"""
                          }
                      }
                  }
                  """).ToString();
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// 收集 XML 中的所有样式定义，构建样式属性字典
    /// </summary>
    /// <param name="element"></param>
    private void CollectStaticStyles(XElement element)
    {
        if (!element.Name.IsSuiNameSpace())
        {
            foreach (var item in element.Elements()) CollectStaticStyles(item);
            return;
        }

        var styleName = element.TryGetSuiAttribute("Name", out var styleNameAttribute)
            ? styleNameAttribute.Value
            : string.Empty;

        if (string.IsNullOrWhiteSpace(styleName) || !string.Equals(element.Name.LocalName, "Style"))
        {
            foreach (var item in element.Elements()) CollectStaticStyles(item);
            return;
        }

        var attributes = element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration &&
                string.IsNullOrEmpty(attribute.Name.NamespaceName))
            .ToArray();

        StaticStyles.TryAdd(styleName, attributes);
    }

    /// <summary>
    /// 递归生成 XML 元素对应的 C# 属性声明
    /// </summary>
    private StringBuilder GeneratePropertyDeclarationsRecursively(XElement parent, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        foreach (var element in parent.Elements().Where(e => !e.Name.IsSuiNameSpace()))
        {
            if (element.TryGetSuiAttribute("Name", out var nameAttr) &&
                ParseHelper.IsValidMemberName(nameAttr.Value) && ValidMemberName.Add(nameAttr.Value) &&
                TryGetNamedTypeSymbol(element, out var typeSymbol))
            {
                var typeGlobalName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                code.AppendLine($$"""{{indent}}public {{typeGlobalName}} {{nameAttr.Value}} { get; private set; }""");
            }

            if (element.HasElements)
            {
                code.Append(GeneratePropertyDeclarationsRecursively(element, indentLevel));
            }
        }

        return code;
    }

    // 递归生成初始化代码，包括子元素的创建和属性设置
    private string GenerateElementInitialization(INamedTypeSymbol typeSymbol, XElement element,
        string variableName, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        variableName ??= "this";

        // 属性赋值
        code.Append(GeneratePropertyAssignments(typeSymbol, element, variableName, indentLevel));

        if (!element.HasElements) return code.ToString();

        foreach (var item in element.Elements().Where(e => !e.Name.IsSuiNameSpace()))
        {
            if (!TryGetNamedTypeSymbol(item, out var itemTypeSymbol)) continue;

            var itemVariableName = $"element{++_variableCounter}";

            code.AppendLine($"{indent}var {itemVariableName} = new {itemTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}();");
            code.Append(GenerateElementInitialization(itemTypeSymbol, item, itemVariableName, indentLevel));

            // 如果子元素有 sui:Name 属性且名称有效，则生成属性赋值代码 (将对象映射到属性)
            if (item.TryGetSuiAttribute("Name", out var nameAttr) && ValidMemberName.Contains(nameAttr.Value))
            {
                code.AppendLine($"{indent}{nameAttr.Value} = {itemVariableName};");
            }

            code.AppendLine($"{indent}{variableName}.AddChild({itemVariableName});");
        }

        return code.ToString();
    }

    // 生成 XML 属性到 C# 对象属性的赋值代码
    private string GeneratePropertyAssignments(INamedTypeSymbol typeSymbol,
        XElement element, string variableName, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        variableName ??= "this";

        // 特殊匹配 M.* 的成员属性，递归处理子元素
        foreach (var item in element.Elements().Where(e => e.Name.LocalName.StartsWith("M.")))
        {
            var memberName = item.Name.LocalName.Substring(2);

            if (string.IsNullOrWhiteSpace(memberName)) continue;
            if (typeSymbol.GetFirstMembers(memberName) is not IPropertySymbol propSymbol) continue;
            if (propSymbol.Type is not INamedTypeSymbol pts) continue;

            code.Append(GenerateElementInitialization(pts, item, $"{variableName}.{memberName}", indentLevel));
        }

        // 附加属性
        var attributes = element.Attributes();
        // 检查是否存在 SilkyUI:Style 属性，如果存在，则将其值解析为样式名称，并获取对应的扩展属性集合

        var styleAttr = attributes.FirstOrDefault(
            attr => attr.Name.IsSuiNameSpace() && string.Equals(attr.Name.LocalName, "Style"));

        if (!string.IsNullOrWhiteSpace(styleAttr?.Value))
        {
            var parts = styleAttr.Value.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            attributes = GetExtendedAttributes(parts).Concat(attributes);
        }

        // 过滤掉带命名空间的属性，只处理常规属性
        var commonAttributes = attributes.Where(a => string.IsNullOrEmpty(a.Name.NamespaceName)).ToArray();

        var bindingAttributes = attributes
            .Where(attribute => attribute.TryGetBindingPropertyName(out _))
            .ToArray();
        var bindingPropertyNames = bindingAttributes.GetBindingPropertyNames();

        foreach (var attribute in bindingAttributes)
        {
            if (!attribute.TryGetBindingPropertyName(out var bindingPropertyName))
                continue;

            // bind:Text="Title" -> view.Bind("Title", "Text")
            if (typeSymbol.GetFirstMembers(bindingPropertyName) is not IPropertySymbol bindingPropSymbol ||
                bindingPropSymbol.SetMethod == null)
                continue;

            var value = ParseHelper.EscapeString(attribute.Value);
            code.AppendLine($"{indent}{variableName}.Bind(\"{value}\", \"{bindingPropertyName}\");");
        }

        foreach (var attribute in commonAttributes)
        {
            var propertyName = attribute.Name.LocalName;

            if (bindingPropertyNames.Contains(propertyName)) continue;

            if (typeSymbol.GetFirstMembers(propertyName) is not IPropertySymbol propSymbol ||
                propSymbol.SetMethod == null) continue;

            if (ParseHelper.TryParseProperty(propSymbol, attribute.Value, out var rValue))
            {
                code.AppendLine($"{indent}{variableName}.{propertyName} = {rValue};");
            }
        }

        return code.ToString();
    }

    /// <summary>
    /// 根据样式名称集合获取对应的扩展属性集合
    /// </summary>
    /// <param name="parts">样式名称集合</param>
    /// <returns>扩展属性集合</returns>
    private IEnumerable<XAttribute> GetExtendedAttributes(string[] parts)
    {
        return parts
            .Where(StaticStyles.ContainsKey)
            .SelectMany(item => StaticStyles[item]);
    }

}
