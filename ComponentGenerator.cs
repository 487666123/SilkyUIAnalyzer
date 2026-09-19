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
    /// <summary>
    /// SilkyUI 程序集名
    /// </summary>
    private const string AssemblyName = "SilkyUIFramework";
    /// <summary>
    /// Xml 映射 [CLR 元数据名称]
    /// </summary>
    private const string XmlMappingName = $"{AssemblyName}.Attributes.XmlElementMappingAttribute";

    /// <summary>
    /// 元素容器接口 [CLR 元数据名称]
    /// </summary>
    private const string ContainerName = $"{AssemblyName}.Interfaces.IContainer`1";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 获取别名映射字典，返回 null 表示有重复别名冲突
        var mapping = context.CompilationProvider.Select((compilation, _) =>
        {
            // 查找特性的 Symbol
            var types = compilation.GetTypesByMetadataName(XmlMappingName);
            if (types.IsEmpty) return null;
            // 确保来自程序集: SilkyUIFramework
            var symbol = types.FirstOrDefault(t => t.ContainingAssembly.Name == AssemblyName);
            if (symbol is null) return null;

            // Xml 映射表
            var map = new Dictionary<string, INamedTypeSymbol>();

            try
            {
                compilation.GlobalNamespace.ForEachTypeSymbol((typeSymbol) =>
                {
                    var aliases = typeSymbol.GetAttributes()
                                          .Where(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, symbol))
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

            // 返回不可变映射表
            return map.ToImmutableDictionary();
        });

        // 保留源路径和文本，以便为 XML 解析和类型错误报告准确位置。
        var xmlProvider = context.AdditionalTextsProvider
            .Where(file => Path.GetFileName(file.Path).EndsWith(".sui.xml", StringComparison.OrdinalIgnoreCase))
            .Select((file, cancellationToken) => new { file.Path, Text = file.GetText(cancellationToken) })
            .Where(file => file.Text != null);

        var classSyntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, _) =>
                    context.SemanticModel.GetDeclaredSymbol(context.Node) as INamedTypeSymbol)
            .Where(symbol => symbol != null).Collect();

        var source = xmlProvider.Combine(classSyntaxProvider).Combine(mapping).Combine(context.CompilationProvider);
        context.RegisterSourceOutput(source, (spc, input) =>
        {
            var (((xml, typeSymbols), mappings), compilation) = input;
            var diagnostics = new XmlDiagnosticReporter(xml.Path, xml.Text, spc.ReportDiagnostic);
            XDocument document = null;
            try
            {
                document = XDocument.Parse(xml.Text.ToString(), LoadOptions.SetLineInfo);
                var root = document.Root;
                if (root == null || !root.TryGetSuiAttribute("Class", out var classAttribute)) return;
                var typeSymbol = typeSymbols.FirstOrDefault(symbol => symbol.ToDisplayString() == classAttribute.Value);
                var containerType = compilation.GetTypesByMetadataName(ContainerName)
                    .FirstOrDefault(type => type.ContainingAssembly.Name == AssemblyName);

                // 保持原有根类约束；CLR 导入只扩展子元素的类型解析。
                if (mappings == null || !typeSymbol.GetConstructedInterfaces(containerType).Any()) return;

                var resolver = new XmlTypeResolver(compilation, typeSymbol, mappings, diagnostics);
                resolver.ValidateDeclarations(root);
                var logic = new ComponentGeneratorLogic(resolver, compilation, containerType, diagnostics);
                var code = logic.GenerateComponentCode(root, typeSymbol);
                spc.AddSource($"{typeSymbol.ToDisplayString()}.g.cs", SourceText.From(code, System.Text.Encoding.UTF8));
            }
            catch (XmlException exception)
            {
                diagnostics.ReportXmlException(exception);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                diagnostics.Report(XmlDiagnosticReporter.GenerationFailed, document?.Root, exception.Message);
            }
        });
    }
}
