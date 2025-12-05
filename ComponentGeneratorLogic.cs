using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

internal static class ComponentGeneratorLogic
{
    private static int _variableCounter;
    private static HashSet<string> ValidMemberName { get; } = [];
    public static Dictionary<string, INamedTypeSymbol> AliasToTypeSymbolMapping { get; set; }

    /// <summary>
    /// 从 XML 根元素生成目标类型的完整组件代码，包括属性声明、初始化逻辑和子元素处理。
    /// </summary>
    /// <param name="root">XML 根元素，其结构定义生成的代码结构。</param>
    /// <param name="typeSymbol">目标类型的符号信息，用于生成类定义和成员访问。</param>
    /// <returns>
    /// 生成的完整 C# 代码字符串，包含以下部分：
    /// 1. 命名空间和分部类定义。<br/>
    /// 2. 属性声明（通过 <see cref="GeneratePropertyDeclarationsRecursively"/>）。<br/>
    /// 3. 初始化方法（<c>InitializeComponent</c>），包括：<br/>
    ///    - 属性赋值（通过 <see cref="GeneratePropertyAssignments"/>）。<br/>
    ///    - 子元素实例化和添加逻辑（通过 <see cref="GenerateElementInitialization"/>）。<br/>
    /// 若 XML 解析失败，返回空字符串。
    /// </returns>
    /// <remarks>
    /// 1. 自动重置全局状态（<see cref="_variableCounter"/> 和 <see cref="ValidMemberName"/>）。<br/>
    /// 2. 生成的类为分部类（<c>partial</c>），便于扩展。<br/>
    /// 3. 使用 <c>_contentLoaded</c> 标志避免重复初始化。<br/>
    /// 4. 子元素通过 <c>Add</c> 方法添加到父实例（适用于集合类）。
    /// </remarks>
    public static string GenerateComponentCode(XElement root, INamedTypeSymbol typeSymbol)
    {
        try
        {
            _variableCounter = 0;
            ValidMemberName.Clear();
            ExtendedAttributes.Clear();
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

    /// <summary>
    /// 递归生成给定 XML 元素及其子元素对应的属性声明代码。
    /// </summary>
    /// <param name="element">要处理的 XML 元素，其子元素将转换为属性声明。</param>
    /// <param name="indentLevel">代码缩进级别（空格数），用于控制生成代码的格式化缩进。</param>
    /// <returns>生成的属性声明代码字符串，包含 public 属性和递归处理的子元素声明。</returns>
    /// <remarks>
    /// 1. 仅处理具有有效 Name 属性的子元素（通过 <see cref="ParseHelper.IsValidMemberName"/> 校验）。<br/>
    /// 2. 自动跳过重复名称（通过 <see cref="ValidMemberName"/> 集合去重）。<br/>
    /// 3. 属性类型通过 <see cref="AliasToTypeSymbolMapping"/> 字典映射解析。<br/>
    /// 4. 生成的属性均为 public，且包含 private set 访问器。
    /// </remarks>
    private static string GeneratePropertyDeclarationsRecursively(XElement element, int indentLevel)
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

    /// <summary>
    /// 递归生成 XML 元素对应的对象初始化代码，并返回生成的实例变量名和初始化逻辑。
    /// </summary>
    /// <param name="typeSymbol">目标类型的符号信息，用于创建实例。</param>
    /// <param name="element">XML 元素，其结构和属性将用于初始化对象。</param>
    /// <param name="variableName"></param>
    /// <param name="indentLevel">代码缩进级别（空格数），控制生成代码的格式化。</param>
    /// <returns>
    /// 包含生成的初始化代码和实例变量名的元组，格式示例：
    /// <code>
    /// (
    ///     "var element1 = new global::MyType();\n    element1.Property = value;",
    ///     "element1"
    /// )
    /// </code>
    /// </returns>
    /// <remarks>
    /// 1. 自动生成唯一的变量名（如 "element1"、"element2"）。<br/>
    /// 2. 调用 <see cref="GeneratePropertyAssignments"/> 处理 XML 属性赋值。<br/>
    /// 3. 递归处理子元素，并调用 <see cref="AliasToTypeSymbolMapping"/> 解析子元素类型。<br/>
    /// 4. 若子元素有 "Name" 属性且名称有效（<see cref="ValidMemberName"/>），则额外生成属性赋值代码。<br/>
    /// 5. 默认调用 `Add` 方法将子元素添加到父实例（适用于集合类）。
    /// </remarks>
    private static string GenerateElementInitialization(INamedTypeSymbol typeSymbol, XElement element,
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

    /// <summary>
    /// 生成属性赋值代码，将 XML 属性值赋给目标对象的对应属性。
    /// </summary>
    /// <param name="typeSymbol">目标类型的符号信息，用于反射获取属性元数据。</param>
    /// <param name="kvs">XML 属性集合，其值将被解析并赋值给目标属性。</param>
    /// <param name="variableName">目标实例的变量名（如 "obj" 或 "this"），用于生成赋值语句。</param>
    /// <param name="indentLevel">代码缩进级别（空格数），控制生成代码的格式化。</param>
    /// <returns>生成的属性赋值代码（如 "obj.Property = value;"）。</returns>
    /// <remarks>
    /// 1. 仅处理可写属性（<see cref="IPropertySymbol.SetMethod"/> 不为 null）。
    /// 2. 跳过 <see cref="SpecialAttributes"/> 中定义的保留属性（如 "Name"）。
    /// 3. 使用 <see cref="ParseHelper.TryParseProperty"/> 解析属性值，确保类型安全。
    /// 4. 生成的代码格式示例：<c>"    target.Property = parsedValue;"</c>。
    /// </remarks>
    private static string GeneratePropertyAssignments(INamedTypeSymbol typeSymbol, XElement element,
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

    public static Dictionary<string, IEnumerable<XAttribute>> ExtendedAttributes = [];

    private static void RegisterStyleElement(string styleName, XElement element)
    {
        if (!string.IsNullOrWhiteSpace(styleName) && !ExtendedAttributes.ContainsKey(styleName))
        {
            ExtendedAttributes[styleName] = element.Attributes().Where(attr => !attr.Name.LocalName.Equals("Name"));
        }
    }

    /// <summary>
    /// 遍历指定的 <see cref="XElement"/> 及其子元素，
    /// 当遇到 <c>Style</c> 元素时，
    /// 以其 <c>Name</c> 属性值作为键，
    /// 将该元素除 <c>Name</c> 外的所有属性收集并存入 <see cref="ExtendedAttributes"/> 字典。
    /// </summary>
    /// <param name="element">要处理的 XML 元素。</param>
    private static void CollectExtendedAttributes(XElement element)
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

    /// <summary>
    /// 根据指定的名称数组，从 <see cref="ExtendedAttributes"/> 字典中获取对应的属性集合。
    /// </summary>
    /// <param name="parts">要查询的名称数组。</param>
    /// <returns>
    /// 返回一个 <see cref="IEnumerable{XAttribute}"/>，包含所有匹配名称对应的属性。
    /// 如果没有匹配项，则返回空序列。
    /// </returns>
    private static IEnumerable<XAttribute> GetExtendedAttributes(string[] parts)
    {
        return parts
            .Where(ExtendedAttributes.ContainsKey)
            .SelectMany(item => ExtendedAttributes[item]);
    }

}