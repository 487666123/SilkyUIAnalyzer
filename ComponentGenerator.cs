using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SilkyUIAnalyzer;

[Generator]
internal partial class ComponentGenerator : IIncrementalGenerator
{
    //private static readonly DiagnosticDescriptor DuplicateElementNameRule = new(
    //    id: "XMLMAP 001",
    //    title: "重复的 XML 元素名映射",
    //    messageFormat: "XML 元素名 '{0}' 被多个类型使用，请确保唯一。",
    //    category: "XmlMappingGenerator",
    //    DiagnosticSeverity.Error,
    //    isEnabledByDefault: true,
    //    description: "标记不同类型的 XML 元素名不能重复.");

    private const string AttributeName = "SilkyUIFramework.Attributes.XmlElementMappingAttribute";
    private const string ElementGroupClassName = "SilkyUIFramework.Elements.UIElementGroup";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 获取别名映射字典，返回null表示有重复别名冲突
        var allSymbol = context.CompilationProvider.Select((compilation, _) =>
        {
            // 获取特性的 TypeSymbol
            if (compilation.GetTypeByMetadataName(AttributeName) is not { } targetAttributeSymbol) return null;

            var map = new Dictionary<string, INamedTypeSymbol>();

            try
            {
                compilation.GlobalNamespace.ForEachTypeSymbol((typeSymbol) =>
                {
                    var aliases = typeSymbol.GetAttributes()
                                          .Where(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, targetAttributeSymbol))
                                          .Select(attr => attr.ConstructorArguments[0].Value as string)
                                          .Where(alias => !string.IsNullOrWhiteSpace(alias));
                    // 收集该类型的所有别名
                    foreach (var alias in aliases)
                    {
                        map.Add(alias, typeSymbol);
                    }
                });
            }
            catch { return null; }

            return map.ToImmutableDictionary();
        });

        // 筛选 .xml 后缀的文件, 并转换为 document
        var xmlDocuments = context.AdditionalTextsProvider
            .Where(f => Path.GetExtension(f.Path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            .Select((file, _) =>
            {
                try
                {
                    var str = file.GetText()!.ToString();
                    //[?] Xml 必有开头哪一行，所以可以设置一个 Length 最小检测
                    if (string.IsNullOrWhiteSpace(str)) return null;

                    // 检查 Class 属性
                    using var reader = XmlReader.Create(new StringReader(str));
                    reader.MoveToContent();
                    if (reader.NodeType != XmlNodeType.Element) return null;

                    // 检查 Class 属性
                    var classValue = reader.GetAttribute("Class");
                    if (string.IsNullOrWhiteSpace(classValue)) return null;

                    return XDocument.Parse(str);
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
                var ((doc, groupTypeSymbols), mappings) = pair;

                var className = doc.Root.Attribute("Class").Value;
                var typeSymbol =
                    groupTypeSymbols.FirstOrDefault(symbols => symbols.ToDisplayString().Equals(className));

                return (doc, typeSymbol, mappings);
            }).Where((args) => args.typeSymbol != null);

        // 注册源输出
        context.RegisterSourceOutput(provider, (spc, data) =>
        {
            var (doc, typeSymbol, mappings) = data;

            // 如果有重复别名冲突，mappings为null，跳过生成
            if (mappings == null) return;

            try
            {
                var logic = new ComponentGeneratorLogic(mappings);
                var code = logic.GenerateComponentCode(doc.Root, typeSymbol);

                spc.AddSource($"{typeSymbol.ToDisplayString()}.g.cs", SourceText.From(code, System.Text.Encoding.UTF8));
            }
            catch { }
        });
    }
}