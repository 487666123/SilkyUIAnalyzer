using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

internal static class ComponentGeneratorLogic
{
    private static int _variableCounter;
    private static HashSet<string> ValidMemberName = [];
    public static Dictionary<string, INamedTypeSymbol> AliasToTypeSymbolMapping { get; set; }

    /// <summary> 特殊属性，不需要生成字段或赋值语句的属性名列表。 </summary>
    public static HashSet<string> SpecialAttributes { get; } = ["Name", "Class"];

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

            var code = new StringBuilder().AppendLine($$"""
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

            {{GeneratePropertyAssignments(typeSymbol, root.Attributes(), "this", 12)}}
            """);

            if (root.HasElements)
            {
                var indent = new string(' ', 12);
                foreach (var item in root.Elements())
                {
                    if (!AliasToTypeSymbolMapping.TryGetValue(item.Name.LocalName, out var itemTypeSymbol)) continue;

                    var (elementCode, variableName) = GenerateElementInitialization(itemTypeSymbol, item, 12);
                    code.Append(elementCode);

                    // 映射
                    if (item.Attribute("Name") is { } nameAttr && ValidMemberName.Contains(nameAttr.Value))
                    {
                        code.AppendLine($"{indent}{nameAttr.Value} = {variableName};");
                    }

                    // 添加到类中
                    code.AppendLine($"{indent}Add({variableName});");
                }
            }

            return code.AppendLine($$"""
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

        foreach (var child in element.Elements())
        {
            if (child.Attribute("Name") is { } nameAttr && ParseHelper.IsValidMemberName(nameAttr.Value) && ValidMemberName.Add(nameAttr.Value))
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
    /// 生成属性赋值代码，将 XML 属性值赋给目标对象的对应属性。
    /// </summary>
    /// <param name="targetTypeSymbol">目标类型的符号信息，用于反射获取属性元数据。</param>
    /// <param name="attributes">XML 属性集合，其值将被解析并赋值给目标属性。</param>
    /// <param name="targetVariable">目标实例的变量名（如 "obj" 或 "this"），用于生成赋值语句。</param>
    /// <param name="indentLevel">代码缩进级别（空格数），控制生成代码的格式化。</param>
    /// <returns>生成的属性赋值代码（如 "obj.Property = value;"）。</returns>
    /// <remarks>
    /// 1. 仅处理可写属性（<see cref="IPropertySymbol.SetMethod"/> 不为 null）。
    /// 2. 跳过 <see cref="SpecialAttributes"/> 中定义的保留属性（如 "Name"）。
    /// 3. 使用 <see cref="ParseHelper.TryParseProperty"/> 解析属性值，确保类型安全。
    /// 4. 生成的代码格式示例：<c>"    target.Property = parsedValue;"</c>。
    /// </remarks>
    private static string GeneratePropertyAssignments(INamedTypeSymbol targetTypeSymbol, IEnumerable<XAttribute> attributes, string targetVariable, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        foreach (var attribute in attributes)
        {
            var propertyName = attribute.Name.LocalName;
            if (SpecialAttributes.Contains(propertyName)) continue;

            var memberSymbols = targetTypeSymbol.GetOnlyMembers(propertyName);
            if (memberSymbols.Count == 0 ||
                memberSymbols.First() is not IPropertySymbol propSymbol ||
                propSymbol.SetMethod == null) continue;

            if (ParseHelper.TryParseProperty(propSymbol, attribute.Value, out var rValue))
            {
                code.AppendLine($"{indent}{targetVariable}.{propertyName} = {rValue};");
            }
        }

        return code.ToString();
    }

    /// <summary>
    /// 递归生成 XML 元素对应的对象初始化代码，并返回生成的实例变量名和初始化逻辑。
    /// </summary>
    /// <param name="typeSymbol">目标类型的符号信息，用于创建实例。</param>
    /// <param name="element">XML 元素，其结构和属性将用于初始化对象。</param>
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
    private static (string code, string variableName) GenerateElementInitialization(INamedTypeSymbol typeSymbol, XElement element, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        var uniqueVariableName = $"element{++_variableCounter}";

        code.AppendLine($"{indent}var {uniqueVariableName} = new global::{typeSymbol}();");

        // 属性赋值
        code.Append(GeneratePropertyAssignments(typeSymbol, element.Attributes(), uniqueVariableName, indentLevel));

        if (element.HasElements)
        {
            foreach (var item in element.Elements())
            {
                if (!AliasToTypeSymbolMapping.TryGetValue(item.Name.LocalName, out var itemTypeSymbol)) continue;

                var (childCode, childVariableName) = GenerateElementInitialization(itemTypeSymbol, item, indentLevel);
                code.Append(childCode);

                if (item.Attribute("Name") is { } nameAttr && ValidMemberName.Contains(nameAttr.Value))
                {
                    code.AppendLine($"{indent}{nameAttr.Value} = {childVariableName};");
                }
                code.AppendLine($"{indent}{uniqueVariableName}.Add({childVariableName});");
            }
        }

        return (code.ToString(), uniqueVariableName);
    }
}