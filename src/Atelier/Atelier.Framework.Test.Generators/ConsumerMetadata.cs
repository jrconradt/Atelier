using Microsoft.CodeAnalysis;

namespace Atelier.Framework.Test.Generators;

internal sealed class RequisiteField
{
    public string Name { get; init; } = string.Empty;
    public string FullyQualifiedTypeName { get; init; } = string.Empty;
    public bool TypeIsInterface { get; init; }
    public bool TypeHasParameterlessCtor { get; init; }
}

internal sealed class OperationMethod
{
    public string Name { get; init; } = string.Empty;
    public string FullyQualifiedReturnTypeName { get; init; } = string.Empty;
    public bool ReturnsOutcomeShape { get; init; }
    public bool IsAsync { get; init; }
    public List<OperationParameter> Parameters { get; init; } = new();
    public string OperationName { get; init; } = string.Empty;
}

internal sealed class OperationParameter
{
    public string Name { get; init; } = string.Empty;
    public string FullyQualifiedTypeName { get; init; } = string.Empty;
    public bool IsCancellationToken { get; init; }
    public bool IsNonNullableReference { get; init; }
    public bool IsString { get; init; }
}

internal sealed class ConsumerMetadata
{
    public required INamedTypeSymbol Symbol { get; init; }
    public required string ClassName { get; init; }
    public required string Namespace { get; init; }
    public required string FullyQualifiedName { get; init; }
    public required bool ImplementsIAtelier { get; init; }
    public required bool IsPartial { get; init; }
    public required IReadOnlyList<RequisiteField> RequisiteFields { get; init; }
    public required IReadOnlyList<OperationMethod> Operations { get; init; }

        public required bool GeneratorAddsLogger { get; init; }

        public required bool GeneratorAddsContextAccessor { get; init; }

        public required bool HasUserDeclaredConstructor { get; init; }

        public required bool IsProduct { get; init; }

        public int ExpectedCtorArity =>
            RequisiteFields.Count
            + (GeneratorAddsLogger ? 1 : 0)
            + (GeneratorAddsContextAccessor ? 1 : 0);

        public bool GeneratorEmitsConstructor =>
        ExpectedCtorArity > 0 && !HasUserDeclaredConstructor;

    public bool HasAnyTestableSurface =>
        RequisiteFields.Count > 0 || Operations.Count > 0 || ImplementsIAtelier;
}

internal static class ConsumerAnalyzer
{
    private static readonly SymbolDisplayFormat FqnFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public static ConsumerMetadata? Analyze(
        INamedTypeSymbol classSymbol,
        Compilation compilation)
    {
        if (classSymbol.TypeKind != TypeKind.Class)
        {
            return null;
        }
        if (classSymbol.IsImplicitlyDeclared)
        {
            return null;
        }
        if (classSymbol.IsStatic)
        {
            return null;
        }

        if (classSymbol.IsAbstract)
        {
            return null;
        }
        if (classSymbol.IsGenericType)
        {
            return null;
        }

        var requisites = CollectRequisiteFields(classSymbol);
        var operations = CollectOperations(classSymbol);
        var implementsIAtelier = ImplementsIAtelier(classSymbol);
        var isProduct = DerivesFromProductBase(classSymbol);

        if (requisites.Count == 0 && operations.Count == 0
            && !implementsIAtelier)
        {
            return null;
        }




        var generatorAddsLogger = implementsIAtelier;

        var contextAccessorType = compilation.GetTypeByMetadataName("Atelier.Framework.Context.IContextAccessor");
        var generatorAddsContextAccessor = implementsIAtelier
            && contextAccessorType is not null
            && FindExistingContextAccessorMember(classSymbol, contextAccessorType) is null
            && !ContextAccessorPropertyExistsInChain(classSymbol);

        var hasUserCtor = classSymbol.Constructors.Any(c =>
            !c.IsImplicitlyDeclared &&
            c.DeclaredAccessibility == Accessibility.Public &&
            c.Parameters.Length > 0);

        return new ConsumerMetadata
        {
            Symbol = classSymbol,
            ClassName = classSymbol.Name,
            Namespace = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : classSymbol.ContainingNamespace.ToDisplayString(),
            FullyQualifiedName = classSymbol.ToDisplayString(FqnFormat),
            ImplementsIAtelier = implementsIAtelier,
            IsPartial = IsPartialClass(classSymbol),
            RequisiteFields = requisites,
            Operations = operations,
            GeneratorAddsLogger = generatorAddsLogger,
            GeneratorAddsContextAccessor = generatorAddsContextAccessor,
            HasUserDeclaredConstructor = hasUserCtor,
            IsProduct = isProduct,
        };
    }

    private const string ProductBaseFullyQualifiedName = "global::Atelier.Framework.Offering.Product.ProductBase";

    public static bool DerivesFromProductBase(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString(FqnFormat) == ProductBaseFullyQualifiedName)
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private const string IAtelierFullyQualifiedName = "global::Atelier.Framework.Observability.IAtelier";

    public static bool ImplementsIAtelier(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.AllInterfaces.Any(i => i.ToDisplayString(FqnFormat) == IAtelierFullyQualifiedName))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static bool ContextAccessorPropertyExistsInChain(INamedTypeSymbol classSymbol)
    {
        var current = classSymbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            if (HasSourceDeclaredContextAccessor(current))
            {
                return true;
            }

            if (!ReferenceEquals(current, classSymbol)
                && ImplementsIAtelier(current)
                && IsPartialClass(current))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static bool HasSourceDeclaredContextAccessor(INamedTypeSymbol classSymbol)
    {
        foreach (var member in classSymbol.GetMembers("ContextAccessor"))
        {
            if (member is IFieldSymbol || member is IPropertySymbol)
            {
                return true;
            }
        }
        return false;
    }

    private static string? FindExistingContextAccessorMember(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol contextAccessorType)
    {
        var current = classSymbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            var isDeclaringType = ReferenceEquals(current, classSymbol);
            foreach (var member in current.GetMembers())
            {
                if (member.Name == "ContextAccessor")
                {
                    continue;
                }
                if (member is IFieldSymbol field
                    && SymbolEqualityComparer.Default.Equals(field.Type, contextAccessorType)
                    && (isDeclaringType || IsAccessibleFromDerived(field)))
                {
                    return field.Name;
                }
                if (member is IPropertySymbol property
                    && SymbolEqualityComparer.Default.Equals(property.Type, contextAccessorType)
                    && (isDeclaringType || IsAccessibleFromDerived(property)))
                {
                    return property.Name;
                }
            }
            current = current.BaseType;
        }
        return null;
    }

    private static bool IsAccessibleFromDerived(ISymbol member)
    {
        return member.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal
            or Accessibility.Internal;
    }

    private static bool IsPartialClass(INamedTypeSymbol classSymbol)
    {
        foreach (var reference in classSymbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax decl
                && decl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }
        return false;
    }

    private static List<RequisiteField> CollectRequisiteFields(INamedTypeSymbol classSymbol)
    {
        var result = new List<RequisiteField>();
        var seen = new HashSet<string>();

        var current = classSymbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (!HasRequisiteAttribute(member))
                {
                    continue;
                }
                if (!seen.Add(member.Name))
                {
                    continue;
                }

                var (name, type) = member switch
                {
                    IFieldSymbol f => (f.Name, f.Type),
                    IPropertySymbol p => (p.Name, p.Type),
                    _ => (string.Empty, (ITypeSymbol?)null),
                };
                if (type is null)
                {
                    continue;
                }

                result.Add(new RequisiteField
                {
                    Name = name,
                    FullyQualifiedTypeName = type.ToDisplayString(FqnFormat),
                    TypeIsInterface = type.TypeKind == TypeKind.Interface,
                    TypeHasParameterlessCtor = HasParameterlessCtor(type),
                });
            }

            if (!ImplementsIAtelier(current))
            {
                break;
            }
            current = current.BaseType;
        }

        return result;
    }

    private static bool HasParameterlessCtor(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return type.IsValueType;
        }
        if (type.IsValueType)
        {
            return true;
        }
        return named.Constructors.Any(c =>
            c.Parameters.Length == 0 &&
            c.DeclaredAccessibility == Accessibility.Public);
    }

    private static bool HasRequisiteAttribute(ISymbol member)
        => member.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "RequisiteAttribute" or "RuntimeAttribute");

    private static List<OperationMethod> CollectOperations(INamedTypeSymbol classSymbol)
    {




        var candidates = new List<IMethodSymbol>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }
            if (method.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }
            if (method.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }
            if (method.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "OperationAttribute" or "Operation"))
            {
                candidates.Add(method);
            }
        }


        var genericShadows = new HashSet<(string Name, int Arity)>();
        foreach (var c in candidates)
        {
            if (c.IsGenericMethod || c.Parameters.Any(p => ContainsOpenGenericParameter(p.Type)))
            {
                genericShadows.Add((c.Name, c.Parameters.Length));
            }
        }

        var result = new List<OperationMethod>();
        foreach (var method in candidates)
        {



            if (method.IsGenericMethod)
            {
                continue;
            }

            if (method.Parameters.Any(p => ContainsOpenGenericParameter(p.Type)))
            {
                continue;
            }

            if (genericShadows.Contains((method.Name, method.Parameters.Length)))
            {
                continue;
            }

            var operationAttr = method.GetAttributes().First(a =>
                a.AttributeClass?.Name is "OperationAttribute" or "Operation");

            var operationName = method.Name;
            var nameArg = operationAttr.ConstructorArguments.FirstOrDefault();
            if (nameArg.Value is string s && !string.IsNullOrEmpty(s))
            {
                operationName = s;
            }

            var (isAsync, returnInner) = UnwrapAsync(method.ReturnType);
            var returnsOutcome = IsOutcomeShape(method.ReturnType);

            var parameters = new List<OperationParameter>();
            foreach (var p in method.Parameters)
            {
                var pType = p.Type;
                var isCt = pType.ToDisplayString() == "System.Threading.CancellationToken";
                var nonNullRef =
                    !isCt &&
                    !p.IsOptional &&
                    !pType.IsValueType &&
                    pType.NullableAnnotation == NullableAnnotation.NotAnnotated;

                parameters.Add(new OperationParameter
                {
                    Name = p.Name,
                    FullyQualifiedTypeName = pType.ToDisplayString(FqnFormat),
                    IsCancellationToken = isCt,
                    IsNonNullableReference = nonNullRef,
                    IsString = pType.SpecialType == SpecialType.System_String && nonNullRef,
                });
            }

            result.Add(new OperationMethod
            {
                Name = method.Name,
                FullyQualifiedReturnTypeName = method.ReturnType.ToDisplayString(FqnFormat),
                ReturnsOutcomeShape = returnsOutcome,
                IsAsync = isAsync,
                Parameters = parameters,
                OperationName = operationName,
            });
        }
        return result;
    }

    private static bool ContainsOpenGenericParameter(ITypeSymbol type)
    {
        var stack = new Stack<ITypeSymbol>();
        stack.Push(type);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current is ITypeParameterSymbol)
            {
                return true;
            }

            if (current is IArrayTypeSymbol arr)
            {
                stack.Push(arr.ElementType);
                continue;
            }

            if (current is IPointerTypeSymbol ptr)
            {
                stack.Push(ptr.PointedAtType);
                continue;
            }

            if (current is INamedTypeSymbol named)
            {
                foreach (var arg in named.TypeArguments)
                {
                    stack.Push(arg);
                }
            }
        }

        return false;
    }

    private static (bool isAsync, ITypeSymbol inner) UnwrapAsync(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol named && named.IsGenericType)
        {
            var def = named.ConstructedFrom.ToDisplayString();
            if (def == "System.Threading.Tasks.Task<TResult>" ||
                def == "System.Threading.Tasks.ValueTask<TResult>")
            {
                return (true, named.TypeArguments[0]);
            }
        }
        return (false, returnType);
    }

    public static bool IsOutcomeShape(ITypeSymbol type)
    {
        var current = type;

        while (true)
        {
            if (current.ToDisplayString() == "Atelier.Framework.Outcomes.Outcome")
            {
                return true;
            }

            if (current is not INamedTypeSymbol named || !named.IsGenericType)
            {
                return false;
            }

            var def = named.ConstructedFrom.ToDisplayString();
            if (def == "Atelier.Framework.Outcomes.Outcome<T>")
            {
                return true;
            }

            if (def != "System.Threading.Tasks.Task<TResult>"
                && def != "System.Threading.Tasks.ValueTask<TResult>")
            {
                return false;
            }

            current = named.TypeArguments[0];
        }
    }
}
