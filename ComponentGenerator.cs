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
    private const string XmlMappingName = "SilkyUIFramework.Attributes.XmlElementMappingAttribute";

    /// <summary>
    /// UI 元素组 [CLR 元数据名称]
    /// </summary>
    private const string UIElementGroupName = "SilkyUIFramework.Elements.UIElementGroup";

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

        // 筛选 .xml 后缀的文件
        // 转换为 Xml Document
        var xmlProvider = context.AdditionalTextsProvider
            .Where(f => Path.GetExtension(f.Path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            .Select((file, _) =>
            {
                try
                {
                    var str = file.GetText().ToString();
                    //[?] Xml 必有开头哪一行，所以可以设置一个 Length 最小检测
                    if (string.IsNullOrWhiteSpace(str)) return null;

                    // 检查 Class 属性
                    using var reader = XmlReader.Create(new StringReader(str));
                    reader.MoveToContent();
                    if (reader.NodeType != XmlNodeType.Element) return null;

                    // 检查 Class 属性
                    var className = reader.GetAttribute("Class");
                    if (string.IsNullOrWhiteSpace(className)) return null;

                    return new { str, className };
                }
                catch { return null; }
            }).Where(doc => doc != null);

        // 所有类语法
        var classSyntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, _) =>
                    context.SemanticModel.GetDeclaredSymbol(context.Node) as INamedTypeSymbol)
            .Where(symbol => symbol.InheritsFrom(UIElementGroupName)).Collect();

        // 找到 XML 绑定的 Class 的 TypeSymbol, 并筛选掉类型映射失败的组
        var source = xmlProvider.Combine(classSyntaxProvider).Combine(mapping)
            .Select((pair, _) =>
            {
                var ((xml, typeSymbols), mappings) = pair;
                var typeSymbol = typeSymbols.FirstOrDefault(symbols => symbols.ToDisplayString().Equals(xml.className));

                if (typeSymbol == null) return null;

                return new { xml.str, typeSymbol, mappings };
            }).Where(input => input != null);

        // 注册源输出
        context.RegisterSourceOutput(source, (spc, sourceInput) =>
        {
            try
            {
                // 解析 Xml
                var document = XDocument.Parse(sourceInput.str);

                var logic = new ComponentGeneratorLogic(sourceInput.mappings);
                var code = logic.GenerateComponentCode(document.Root, sourceInput.typeSymbol);

                spc.AddSource($"{sourceInput.typeSymbol.ToDisplayString()}.g.cs", SourceText.From(code, System.Text.Encoding.UTF8));
            }
            catch { }
        });
    }
}
