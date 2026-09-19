using Microsoft.CodeAnalysis;

namespace SilkyUIAnalyzer;

internal static class ITypeSymbolExtensions
{
    extension(INamedTypeSymbol typeSymbol)
    {
        /// <summary>
        /// 获取 指定类型符号 的 所有成员（包括继承的成员，唯一：子类重写优先）
        /// </summary>
        public ISymbol GetFirstMembers(string name)
        {
            while (typeSymbol != null)
            {
                foreach (var item in typeSymbol.GetMembers(name))
                {
                    return item;
                }

                typeSymbol = typeSymbol.BaseType;
            }

            return null;
        }

        /// <summary>
        /// 获取类型实现的指定泛型接口，包括继承的接口和接口类型自身。
        /// </summary>
        public IEnumerable<INamedTypeSymbol> GetConstructedInterfaces(INamedTypeSymbol interfaceDefinition)
        {
            if (typeSymbol == null || interfaceDefinition == null) yield break;

            if (typeSymbol.TypeKind == TypeKind.Interface &&
                SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, interfaceDefinition))
                yield return typeSymbol;

            foreach (var interfaceType in typeSymbol.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, interfaceDefinition))
                    yield return interfaceType;
            }
        }
    }

    extension(INamespaceSymbol namespaceSymbol)
    {
        /// <summary>
        /// 遍历命名空间下的所有 TypeSymbol
        /// </summary>
        public void ForEachTypeSymbol(Action<INamedTypeSymbol> action)
        {
            if (action == null) return;

            foreach (var member in namespaceSymbol.GetMembers())
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
    }
}