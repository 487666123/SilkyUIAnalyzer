using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SilkyUIAnalyzer;

[Generator]
internal partial class ComponentGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor DuplicateElementNameRule = new(
        id: "XMLMAP 001",
        title: "重复的 XML 元素名映射",
        messageFormat: "XML 元素名 '{0}' 被多个类型使用，请确保唯一。",
        category: "XmlMappingGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "标记不同类型的 XML 元素名不能重复.");

    private const string AttributeName = "SilkyUIFramework.Attributes.XmlElementMappingAttribute";
    private const string ElementGroupClassName = "SilkyUIFramework.Elements.UIElementGroup";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var allSymbol = context.CompilationProvider.Select((c, _) =>
        {
            // 获取特性的 TypeSymbol
            if (c.GetTypeByMetadataName(AttributeName) is not { } targetAttributeSymbol) return [];

            var result = ImmutableArray.CreateBuilder<(string Alias, INamedTypeSymbol TypeSymbol)>();

            c.GlobalNamespace.ForEachTypeSymbol((typeSymbol) =>
            {
                // 所有 别名(alias) and TypeSymbol
                foreach (var alias in from attr in typeSymbol.GetAttributes()
                                      where SymbolEqualityComparer.Default.Equals(attr.AttributeClass, targetAttributeSymbol)
                                      select attr.ConstructorArguments[0].Value as string
                         into alias
                                      where !string.IsNullOrWhiteSpace(alias)
                                      select alias)
                {
                    result.Add((alias, typeSymbol));
                }
            });

            return result.ToImmutable();
        });

        // 筛选 .xml 后缀的文件, 并转换为 document
        var xmlDocuments = context.AdditionalTextsProvider
            .Where(f => Path.GetExtension(f.Path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            .Select((file, _) =>
            {
                try
                {
                    var document = XDocument.Parse(file.GetText()!.ToString());
                    return string.IsNullOrWhiteSpace(document.Root?.Attribute("Class")?.Value) ? null : document;
                }
                catch { return null; }
            }).Where(doc => doc != null);

        // 所有类语法
        var classSyntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, _) =>
                    context.SemanticModel.GetDeclaredSymbol(context.Node) as INamedTypeSymbol)
            .Where(symbol => symbol.InheritsFrom(ElementGroupClassName)).Collect();

        // 找到 XML 绑定的 Class 的 TypeSymbol, 并筛选掉类型映射失败的组
        var provider = xmlDocuments
            .Combine(classSyntaxProvider).Combine(allSymbol)
            .Select((pair, _) =>
            {
                var ((doc, groupTypeSymbols), mapping) = pair;

                var className = doc.Root.Attribute("Class").Value;
                var typeSymbol =
                    groupTypeSymbols.FirstOrDefault(symbols => symbols.ToDisplayString().Equals(className));

                return (doc, typeSymbol, mapping);
            }).Where((args) => args.typeSymbol != null);

        // 注册源输出
        context.RegisterSourceOutput(provider, (spc, data) =>
        {
            var (doc, typeSymbol, mappings) = data;

            var duplicates = mappings.GroupBy(x => x.Alias).Where(g => g.Count() > 1).ToArray();
            if (duplicates.Length > 0) return;

            var mappingTable = mappings.ToDictionary(a => a.Alias, b => b.TypeSymbol);

            try
            {
                ComponentGeneratorLogic.AliasToTypeSymbolMapping = mappingTable;
                var code = ComponentGeneratorLogic.GenerateComponentCode(doc.Root, typeSymbol);

                spc.AddSource($"{typeSymbol.ToDisplayString()}.g.cs", SourceText.From(code, System.Text.Encoding.UTF8));
            }
            catch { }
        });
    }
}