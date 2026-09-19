using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

/// <summary>
/// 组件生成器逻辑类，负责将 XML 元素映射为 C# 代码
/// </summary>
/// <param name="typeResolver">标签别名及 CLR 类型的统一解析器</param>
/// <param name="compilation">用于检查子元素到容器参数类型的隐式转换</param>
/// <param name="containerDefinition">IContainer&lt;T&gt; 的泛型定义</param>
/// <param name="diagnostics">带 XML 位置的诊断报告器</param>
internal class ComponentGeneratorLogic(XmlTypeResolver typeResolver,
    Compilation compilation, INamedTypeSymbol containerDefinition, XmlDiagnosticReporter diagnostics)
{
    private static readonly DiagnosticDescriptor UnsupportedChild = new(
        "SUI001", "容器无法接收子元素",
        "类型“{0}”未实现可接收子元素类型“{1}”的 IContainer<T> 接口",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AmbiguousContainer = new(
        "SUI002", "容器接口匹配不明确",
        "类型“{0}”实现了多个可接收子元素类型“{1}”的 IContainer<T> 接口，无法确定唯一的最具体接口",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private int _variableCounter = 0;

    private readonly HashSet<string> ValidMemberName = [];

    public Dictionary<string, XAttribute[]> StaticStyles { get; } = [];

    /// <summary>
    /// 生成完整的UI组件代码，包括命名空间、类定义和初始化方法
    /// </summary>
    public string GenerateComponentCode(XElement root, INamedTypeSymbol typeSymbol)
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
                ParseHelper.IsValidMemberName(nameAttr.Value) && !element.Name.IsPropertyElement() &&
                typeResolver.TryResolve(element, out var typeSymbol) && ValidMemberName.Add(nameAttr.Value))
            {
                var typeGlobalName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var accessibility = ClrXmlNamespace.IsClrNamespace(element.Name.NamespaceName) &&
                    typeSymbol.DeclaredAccessibility != Accessibility.Public ? "internal" : "public";
                code.AppendLine($$"""{{indent}}{{accessibility}} {{typeGlobalName}} {{nameAttr.Value}} { get; private set; }""");
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
            if (item.Name.IsPropertyElement()) continue;
            if (!typeResolver.TryResolve(item, out var itemTypeSymbol)) continue;
            var containerInterface = GetContainerInterface(typeSymbol, itemTypeSymbol, item);
            if (containerInterface == null) continue;

            var itemVariableName = $"element{++_variableCounter}";

            code.AppendLine($"{indent}var {itemVariableName} = new {itemTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}();");
            code.Append(GenerateElementInitialization(itemTypeSymbol, item, itemVariableName, indentLevel));

            // 如果子元素有 sui:Name 属性且名称有效，则生成属性赋值代码 (将对象映射到属性)
            if (item.TryGetSuiAttribute("Name", out var nameAttr) && ValidMemberName.Contains(nameAttr.Value))
            {
                code.AppendLine($"{indent}{nameAttr.Value} = {itemVariableName};");
            }

            var containerTypeName = containerInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            code.AppendLine($"{indent}(({containerTypeName}){variableName}).Add({itemVariableName});");
        }

        return code.ToString();
    }

    // 根据子元素类型选择容器接口，避免依赖公开 Add 方法或接口枚举顺序。
    private INamedTypeSymbol GetContainerInterface(INamedTypeSymbol parentType, INamedTypeSymbol childType, XElement element)
    {
        var candidates = parentType.GetConstructedInterfaces(containerDefinition)
            .Where(type => compilation.ClassifyCommonConversion(childType, type.TypeArguments[0]).IsImplicit)
            .ToArray();

        if (candidates.Length == 0)
        {
            diagnostics.Report(UnsupportedChild, element, parentType.ToDisplayString(), childType.ToDisplayString());
            return null;
        }

        var exactMatch = candidates.FirstOrDefault(type =>
            SymbolEqualityComparer.Default.Equals(type.TypeArguments[0], childType));
        if (exactMatch != null) return exactMatch;

        // 例如同时实现 IContainer<object> 和 IContainer<UIView> 时，优先选择 UIView。
        var bestMatches = candidates.Where(candidate => candidates.All(other =>
            SymbolEqualityComparer.Default.Equals(candidate, other) ||
            (compilation.ClassifyCommonConversion(candidate.TypeArguments[0], other.TypeArguments[0]).IsImplicit &&
             !compilation.ClassifyCommonConversion(other.TypeArguments[0], candidate.TypeArguments[0]).IsImplicit)))
            .ToArray();

        if (bestMatches.Length == 1) return bestMatches[0];

        diagnostics.Report(AmbiguousContainer, element, parentType.ToDisplayString(), childType.ToDisplayString());
        return null;
    }

    // 生成 XML 属性到 C# 对象属性的赋值代码
    private string GeneratePropertyAssignments(INamedTypeSymbol typeSymbol,
        XElement element, string variableName, int indentLevel)
    {
        var indent = new string(' ', indentLevel);
        var code = new StringBuilder();

        variableName ??= "this";

        // Properties 命名空间元素展开父对象已有的属性，递归配置该属性对象。
        foreach (var item in element.Elements().Where(e => e.Name.IsPropertyElement()))
        {
            var memberName = item.Name.LocalName;

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
