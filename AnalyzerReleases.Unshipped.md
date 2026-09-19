; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SUI001 | SilkyUI | Error | Parent does not implement an IContainer<T> that accepts the child type
SUI002 | SilkyUI | Error | Multiple compatible IContainer<T> interfaces have no unique most specific match
SUI003 | SilkyUI | Error | CLR XML namespace declaration is malformed
SUI004 | SilkyUI | Error | CLR XML element type is ambiguous under C# global alias lookup
SUI005 | SilkyUI | Error | CLR XML element type cannot be resolved through the C# global alias
SUI006 | SilkyUI | Error | CLR XML element type cannot be constructed from the bound root class
SUI007 | SilkyUI | Error | XML element namespace is unsupported
SUI008 | SilkyUI | Error | XML document cannot be parsed
SUI009 | SilkyUI | Error | XML component generation failed
