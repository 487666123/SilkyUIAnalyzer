using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SilkyUIAnalyzer;

/// <summary>统一解析标签别名和 CLR 命名空间，缓存同一节点的结果和诊断。</summary>
internal sealed class XmlTypeResolver(Compilation compilation, INamedTypeSymbol rootType,
    ImmutableDictionary<string, INamedTypeSymbol> aliases, XmlDiagnosticReporter diagnostics)
{
    private static readonly DiagnosticDescriptor InvalidNamespace = new(
        "SUI003", "CLR 命名空间声明无效", "CLR 命名空间“{0}”格式无效，应为 clr-namespace:Namespace，不支持程序集参数",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor AmbiguousType = new(
        "SUI004", "CLR 类型不明确", "CLR 类型“global::{0}”存在歧义，请调整类型命名空间或项目引用",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor TypeUnavailable = new(
        "SUI005", "CLR 类型不可用", "无法在当前编译中通过 global:: 解析 CLR 类型“{0}”，请检查类型名称、可访问性及项目引用",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor TypeNotConstructible = new(
        "SUI006", "CLR 类型无法创建", "CLR 类型“{0}”不能在组件“{1}”中创建：{2}",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor UnsupportedNamespace = new(
        "SUI007", "XML 元素命名空间不受支持", "XML 元素“{0}”使用了不受支持的命名空间“{1}”",
        "SilkyUI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private readonly Dictionary<XElement, INamedTypeSymbol> _resolved = [];
    private readonly Dictionary<string, ClrXmlNamespace> _namespaces = [];

    public void ValidateDeclarations(XElement root)
    {
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(attribute =>
                     attribute.IsNamespaceDeclaration && ClrXmlNamespace.IsClrNamespace(attribute.Value)))
            ResolveNamespace(attribute.Value, attribute);
    }

    public bool TryResolve(XElement element, out INamedTypeSymbol type)
    {
        if (_resolved.TryGetValue(element, out type)) return type != null;
        type = Resolve(element);
        _resolved.Add(element, type);
        return type != null;
    }

    private INamedTypeSymbol Resolve(XElement element)
    {
        var xmlNamespace = element.Name.NamespaceName;
        if (xmlNamespace.Length == 0)
            return aliases.TryGetValue(element.Name.LocalName, out var alias) ? alias : null;

        if (!ClrXmlNamespace.IsClrNamespace(xmlNamespace))
        {
            diagnostics.Report(UnsupportedNamespace, element, element.Name.LocalName, xmlNamespace);
            return null;
        }

        var declaration = ResolveNamespace(xmlNamespace, element);
        if (declaration == null) return null;
        var fullName = declaration.NamespaceName.Length == 0 ? element.Name.LocalName :
            declaration.NamespaceName + "." + element.Name.LocalName;
        var symbolInfo = BindGlobalType(fullName);
        if (symbolInfo.CandidateReason == CandidateReason.Ambiguous)
        {
            diagnostics.Report(AmbiguousType, element, fullName);
            return null;
        }
        if (symbolInfo.Symbol is not INamedTypeSymbol type || type.TypeKind == TypeKind.Error)
        {
            diagnostics.Report(TypeUnavailable, element, fullName);
            return null;
        }

        var reason = GetConstructionError(type);
        if (reason == null) return type;
        diagnostics.Report(TypeNotConstructible, element, fullName, rootType.ToDisplayString(), reason);
        return null;
    }

    private ClrXmlNamespace ResolveNamespace(string value, XObject node)
    {
        if (_namespaces.TryGetValue(value, out var cached)) return cached;
        if (!ClrXmlNamespace.TryParse(value, out var declaration))
            diagnostics.Report(InvalidNamespace, node, value);
        return _namespaces[value] = declaration;
    }

    private SymbolInfo BindGlobalType(string fullName)
    {
        // 让 C# 绑定器处理源码优先、引用别名、类型转发和歧义，保持与生成的 global:: 名称一致。
        // CLR 名称不带 @，绑定时逐段转义，兼容使用 C# 关键字的命名空间和类型。
        var typeName = SyntaxFactory.ParseTypeName("global::" + string.Join(".", fullName.Split('.').Select(part => "@" + part)));
        if (typeName.ContainsDiagnostics) return default;
        var declaration = rootType.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>().FirstOrDefault(syntax => !syntax.OpenBraceToken.IsMissing &&
                syntax.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken));
        if (declaration == null) return default;
        var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
        return semanticModel.GetSpeculativeSymbolInfo(declaration.OpenBraceToken.Span.End, typeName,
            SpeculativeBindingOption.BindAsTypeOrNamespace);
    }

    private string GetConstructionError(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.ContainingType != null || type.Arity != 0 || type.IsStatic || type.IsAbstract)
            return "只支持非抽象、非静态、非泛型的顶层类";
        if (type.IsFileLocal || !compilation.IsSymbolAccessibleWithin(type, rootType))
            return "类型在生成代码的位置不可访问";

        var constructor = type.InstanceConstructors.FirstOrDefault(ctor => ctor.Parameters.Length == 0 &&
            IsConstructorAccessible(ctor, type));
        if (constructor == null) return "缺少可访问的零参数构造函数";

        if (HasRequiredMembers(type) && !constructor.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
            return "包含 required 成员，零参数构造函数必须声明 SetsRequiredMembers";
        return null;
    }

    private bool IsConstructorAccessible(IMethodSymbol constructor, INamedTypeSymbol type)
    {
        if (!compilation.IsSymbolAccessibleWithin(constructor, rootType)) return false;
        // 派生类可以调用 base()，但不能仅凭 protected 权限调用 new Base()。
        if (constructor.DeclaredAccessibility is not (Accessibility.Protected or Accessibility.ProtectedAndInternal or
            Accessibility.ProtectedOrInternal)) return true;

        for (var context = rootType; context != null; context = context.ContainingType)
            if (SymbolEqualityComparer.Default.Equals(context, type)) return true;

        return constructor.DeclaredAccessibility == Accessibility.ProtectedOrInternal &&
            (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, rootType.ContainingAssembly) ||
             type.ContainingAssembly.GivesAccessTo(rootType.ContainingAssembly));
    }

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (current.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
                return true;
        return false;
    }
}
