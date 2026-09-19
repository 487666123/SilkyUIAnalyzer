using Microsoft.CodeAnalysis.CSharp;

namespace SilkyUIAnalyzer;

internal sealed class ClrXmlNamespace(string namespaceName)
{
    public const string Prefix = "clr-namespace:";
    public string NamespaceName { get; } = namespaceName;

    public static bool IsClrNamespace(string value) => value.StartsWith("clr-namespace", StringComparison.Ordinal);

    public static bool TryParse(string value, out ClrXmlNamespace result)
    {
        result = null;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) || value.Any(char.IsWhiteSpace)) return false;

        var namespaceName = value.Substring(Prefix.Length);
        if (namespaceName.Length > 0 && !namespaceName.Split('.').All(IsIdentifier)) return false;

        result = new ClrXmlNamespace(namespaceName);
        return true;
    }

    private static bool IsIdentifier(string value) => value.Length > 0 &&
        SyntaxFacts.IsIdentifierStartCharacter(value[0]) && value.Skip(1).All(SyntaxFacts.IsIdentifierPartCharacter);
}
