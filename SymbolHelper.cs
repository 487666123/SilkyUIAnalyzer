using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

internal static class SymbolHelper
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
}
