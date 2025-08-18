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

    public static string AttributeName => "SilkyUIFramework.Attributes.XmlElementMappingAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var allSymbol = context.CompilationProvider.Select((c, _) =>
        {
            var result = ImmutableArray.CreateBuilder<(string Alias, INamedTypeSymbol TypeSymbol)>();

            var targetAttributeSymbol = c.GetTypeByMetadataName(AttributeName);
            if (targetAttributeSymbol == null) return [];

            void VisitSymbol(INamespaceSymbol ns, ImmutableArray<(string, INamedTypeSymbol)>.Builder result)
            {
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol childNS)
                    {
                        VisitSymbol(childNS, result);
                    }
                    else if (member is INamedTypeSymbol typeSymbol)
                    {
                        foreach (var attr in typeSymbol.GetAttributes())
                        {
                            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, targetAttributeSymbol))
                            {
                                var alias = attr.ConstructorArguments[0].Value as string;
                                result.Add((alias, typeSymbol));
                                continue;
                            }
                        }
                    }
                }
            }

            VisitSymbol(c.GlobalNamespace, result);
            return result.ToImmutable();
        });


        // 监听 XmlElementMappingAttribute 特性
        var xmlMappedTypes = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeName,
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, cancellationToken) =>
                {
                    var alias = context.Attributes[0].ConstructorArguments[0].Value as string;
                    var typeSymbol = context.TargetSymbol as INamedTypeSymbol;

                    var attributeSyntax = context.Attributes[0].ApplicationSyntaxReference?.GetSyntax(cancellationToken);
                    var location = attributeSyntax?.GetLocation() ?? Location.None;

                    return (Alias: alias, TypeSymbol: typeSymbol, Location: location);
                }).Collect();

        // 筛选 .xml 后缀的文件
        var xmlDocuments = context.AdditionalTextsProvider
            .Where(f => Path.GetExtension(f.Path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            .Select((file, _) =>
            {
                try
                {
                    var document = XDocument.Parse(file.GetText()?.ToString());

                    if (string.IsNullOrEmpty(document.Root.Attribute("Class").Value))
                        return null;

                    return document;
                }
                catch { }

                return null;
            }).Where(doc => doc != null);

        // 所有类语法
        var classSyntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
            transform: static (context, _) => context.SemanticModel.GetDeclaredSymbol(context.Node) as INamedTypeSymbol).Collect();

        // 找到 XML 绑定的 Class 的 TypeSymbol
        var provider = xmlDocuments
            .Combine(classSyntaxProvider).Combine(allSymbol)
            .Select((pair, _) =>
            {
                var ((document, symbols), mapping) = pair;

                var className = document.Root.Attribute("Class").Value;
                var typeSymbol = symbols.FirstOrDefault(symbols => symbols.ToDisplayString().Equals(className));

                return (document, typeSymbol, mapping);
            }).Where((args) => args.typeSymbol != null);

        // 注册源输出
        context.RegisterSourceOutput(provider, (spc, data) =>
        {
            var (document, typeSymbol, mappings) = data;

            var duplicates = mappings.GroupBy(x => x.Alias).Where(g => g.Count() > 1).ToArray();

            // 如果有重复的不会生成代码
            if (duplicates.Length > 0)
            {
                //foreach (var group in duplicates)
                //{
                //    foreach (var item in group)
                //    {
                //        // 报告重复别名错误
                //        var diagnostic = Diagnostic.Create(DuplicateElementNameRule, item.Location, item.Alias);
                //        spc.ReportDiagnostic(diagnostic);
                //    }
                //}

                return;
            }

            // 获取映射表
            var eMappingTable = mappings.ToDictionary(a => a.Alias, b => b.TypeSymbol);

            try
            {
                if (document.Root is not { } root) return;

                var fullName = root.Attribute("Class").Value;

                ComponentGeneratorLogic.AliasToTypeSymbolMapping = eMappingTable;
                var code = ComponentGeneratorLogic.GenCode(root, typeSymbol);

                spc.AddSource($"{string.Join(".", fullName)}.g.cs", SourceText.From(code, System.Text.Encoding.UTF8));
            }
            catch { }
        });
    }
}
