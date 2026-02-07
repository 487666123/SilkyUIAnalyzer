using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

internal class ComponentGeneratorLogic(ImmutableDictionary<string, INamedTypeSymbol> aliasToTypeSymbolMapping)
{
    private int _variableCounter = 0;
    private readonly HashSet<string> ValidMemberName = [];
    private Dictionary<string, IEnumerable<XAttribute>> _extendedAttributes = [];
    private readonly ImmutableDictionary<string, INamedTypeSymbol> AliasToTypeSymbolMapping = aliasToTypeSymbolMapping;

    // 生成完整的UI组件代码，包括命名空间、类定义和初始化方法
    public string GenerateComponentCode(XElement root, INamedTypeSymbol typeSymbol)
    {
        try
        {
            CollectExtendedAttributes(root);

            var code = new StringBuilder().AppendLine(
                $$"""
                  namespace {{typeSymbol.ContainingNamespace.ToDisplayString()}}
                  {
                      {{typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant()}} partial class {{typeSymbol.Name}}
                      {
                  {{GeneratePropertyDeclarationsRecursively(root, 8)}}

                          private bool _contentLoaded;

                          private void InitializeComponent()
                          {
                              if (_contentLoaded) return;
                              _contentLoaded = true;

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

    // 递归生成XML元素对应的C#属性声明
    private string GeneratePropertyDeclarationsRecursively(XElement element, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        foreach (var child in element.Elements().Where(e => e.IsCommonElement()))
        {
            if (child.Attribute("Name") is { } nameAttr && ParseHelper.IsValidMemberName(nameAttr.Value) &&
                ValidMemberName.Add(nameAttr.Value))
            {
                var typeSymbol = AliasToTypeSymbolMapping[child.Name.LocalName];
                var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                code.AppendLine($$"""{{indent}}public {{typeName}} {{nameAttr.Value}} { get; private set; }""");
            }

            if (child.HasElements)
            {
                code.Append(GeneratePropertyDeclarationsRecursively(child, indentLevel));
            }
        }

        return code.ToString();
    }

    // 递归生成UI元素的初始化代码，包括子元素的创建和属性设置
    private string GenerateElementInitialization(INamedTypeSymbol typeSymbol, XElement element,
        string variableName, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        variableName ??= "this";

        // 属性赋值
        code.Append(GeneratePropertyAssignments(typeSymbol, element, variableName, indentLevel));

        if (!element.HasElements) return code.ToString();

        foreach (var item in element.Elements().Where(e => e.IsCommonElement()))
        {
            if (!AliasToTypeSymbolMapping.TryGetValue(item.Name.LocalName, out var itemTypeSymbol)) continue;

            var itemVariableName = $"element{++_variableCounter}";

            code.AppendLine($"{indent}var {itemVariableName} = new {itemTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}();");
            code.Append(GenerateElementInitialization(itemTypeSymbol, item, itemVariableName, indentLevel));

            // 如果子元素有 Name 属性且名称有效，则生成属性赋值代码 (将对象映射到属性)
            if (item.Attribute("Name") is { } nameAttr && ValidMemberName.Contains(nameAttr.Value))
            {
                code.AppendLine($"{indent}{nameAttr.Value} = {itemVariableName};");
            }

            code.AppendLine($"{indent}{variableName}.AddChild({itemVariableName});");
        }

        return code.ToString();
    }

    // 生成XML属性到C#对象属性的赋值代码
    private string GeneratePropertyAssignments(INamedTypeSymbol typeSymbol, XElement element,
        string variableName, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        variableName ??= "this";

        // 想了半天，你就写了这点代码？！
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
        var styleAttr = attributes.FirstOrDefault(attr => attr.Name.LocalName.Equals("Style"));
        if (!string.IsNullOrWhiteSpace(styleAttr?.Value))
        {
            var parts = styleAttr.Value.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            attributes = GetExtendedAttributes(parts).Concat(attributes);
        }

        foreach (var (propertyName, value) in attributes.Where(a => a.IsCommonAttribute())
                     .Select(a => (a.Name.LocalName, a.Value)))
        {
            if (typeSymbol.GetFirstMembers(propertyName) is not IPropertySymbol propSymbol || propSymbol.SetMethod == null) continue;

            if (ParseHelper.TryParseProperty(propSymbol, value, out var rValue))
            {
                code.AppendLine($"{indent}{variableName}.{propertyName} = {rValue};");
            }
        }

        return code.ToString();
    }

    // 注册样式元素及其属性，用于后续样式应用
    private void RegisterStyleElement(string styleName, XElement element)
    {
        if (!string.IsNullOrWhiteSpace(styleName) && !_extendedAttributes.ContainsKey(styleName))
        {
            _extendedAttributes[styleName] = element.Attributes().Where(attr => !attr.Name.LocalName.Equals("Name"));
        }
    }

    // 递归收集XML中的所有样式定义，构建样式属性字典
    private void CollectExtendedAttributes(XElement element)
    {
        var elementName = element.Name.LocalName;
        if (string.IsNullOrWhiteSpace(elementName)) return;

        if (elementName.StartsWith("Style."))
        {
            var styleName = elementName.Substring(6);
            RegisterStyleElement(styleName, element);
            return;
        }
        else if (elementName.Equals("Style"))
        {
            var styleName = element.Attribute("Name")?.Value;
            RegisterStyleElement(styleName, element);

            foreach (var item in element.Elements())
            {
                RegisterStyleElement(item.Name.LocalName, item);
            }
            return;
        }


        foreach (var item in element.Elements()) CollectExtendedAttributes(item);
    }

    // 根据样式名称获取对应的扩展属性集合
    private IEnumerable<XAttribute> GetExtendedAttributes(string[] parts)
    {
        return parts
            .Where(_extendedAttributes.ContainsKey)
            .SelectMany(item => _extendedAttributes[item]);
    }

}