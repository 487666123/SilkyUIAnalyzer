using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

internal static class SymbolExtensions
{
    /// <summary>
    /// 获取 指定类型符号 的 所有成员（包括继承的成员，唯一：子类重写优先）
    /// </summary>
    public static List<ISymbol> GetOnlyMembers(this INamedTypeSymbol typeSymbol, string name)
    {
        var members = new List<ISymbol>();

        while (typeSymbol != null)
        {
            foreach (var item in typeSymbol.GetMembers(name))
            {
                if (members.Any(s => s.Name == item.Name)) continue;
                members.Add(item);
            }

            typeSymbol = typeSymbol.BaseType;
        }

        return members;
    }

    /// <summary>
    /// 遍历命名空间下的所有 TypeSymbol
    /// </summary>
    public static void ForEachTypeSymbol(this INamespaceSymbol ns, Action<INamedTypeSymbol> action)
    {
        if (action == null) return;

        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNS)
            {
                ForEachTypeSymbol(childNS, action);
            }
            else if (member is INamedTypeSymbol typeSymbol)
            {
                action(typeSymbol);
            }
        }
    }

    /// <summary>
    /// 遍历类型的 BaseType 链<br/>
    /// 如果找到与指定全名匹配的类型，则返回 true<br/>
    /// 如果到达最顶层 Object 类型仍未匹配，则返回 false
    /// </summary>
    public static bool InheritsFrom(this INamedTypeSymbol typeSymbol, string baseTypeFullName)
    {
        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.ToDisplayString() == baseTypeFullName)
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

}
