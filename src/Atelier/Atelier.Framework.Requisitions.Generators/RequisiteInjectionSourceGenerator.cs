using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Templar.Rendering;
using Templar.Presets;
using I = Atelier.Framework.Requisitions.Generators.Compositors.Injection;
using A = Atelier.Framework.Requisitions.Generators.Compositors.Injection.Assignments;

namespace Atelier.Framework.Generators.Requisition;

[Generator]
public sealed class RequisiteInjectionSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var injections = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            injections,
            static (spc, result) =>
                spc.AddSource(result.HintName,
                              SourceText.From(result.Source, Encoding.UTF8)));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDeclaration)
        {
            return false;
        }

        var hasRequisiteAttribute = classDeclaration.Members
            .OfType<MemberDeclarationSyntax>()
            .SelectMany(m => m.AttributeLists)
            .SelectMany(al => al.Attributes)
            .Any(attr => attr.Name.ToString() == "Requisite" || attr.Name.ToString() == "Runtime");

        var partialWithBase = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))
            && classDeclaration.BaseList is not null
            && classDeclaration.BaseList.Types.Count > 0;

        return hasRequisiteAttribute || partialWithBase;
    }

    private static RequisiteInjectionResult? Transform(GeneratorSyntaxContext ctx)
    {
        var classDeclaration = (ClassDeclarationSyntax)ctx.Node;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol is null)
        {
            return null;
        }

        foreach (var reference in classSymbol.DeclaringSyntaxReferences)
        {
            var referenceSyntax = reference.GetSyntax();
            if (!IsCandidate(referenceSyntax))
            {
                continue;
            }
            if (!ReferenceEquals(referenceSyntax, classDeclaration))
            {
                return null;
            }
            break;
        }

        var loggerType = ctx.SemanticModel.Compilation.GetTypeByMetadataName("Atelier.Framework.Observability.ILogger");
        var contextAccessorType = ctx.SemanticModel.Compilation.GetTypeByMetadataName("Atelier.Framework.Context.IContextAccessor");

        return EmitFor(classSymbol, loggerType, contextAccessorType);
    }

    private static RequisiteInjectionResult? EmitFor(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol? loggerType,
        INamedTypeSymbol? contextAccessorType)
    {
        var requisiteMembers = GetRequisiteMembers(classSymbol);

        var implementsIAtelier = ImplementsIAtelier(classSymbol);

        var emitLoggerField = implementsIAtelier
            && loggerType is not null
            && !LoggerWillExistInChain(classSymbol);

        var existingAccessorMember = contextAccessorType is not null
            ? FindExistingContextAccessorMember(classSymbol, contextAccessorType)
            : null;

        var contextAccessorPropertyExistsInChain = ContextAccessorPropertyExistsInChain(classSymbol);

        var emitContextAccessorField = implementsIAtelier
            && contextAccessorType is not null
            && existingAccessorMember is null
            && !contextAccessorPropertyExistsInChain;

        var emitContextAccessorProperty = implementsIAtelier
            && contextAccessorType is not null
            && existingAccessorMember is not null
            && !contextAccessorPropertyExistsInChain;

        var needsLoggerCtorParam = implementsIAtelier && loggerType is not null;
        var needsContextAccessorCtorParam = emitContextAccessorField;
        var needsObserve = implementsIAtelier
            && !ObserveWillExistInChain(classSymbol);

        if (needsLoggerCtorParam)
        {
            requisiteMembers.Add(new RequisiteMember
            {
                Name = "Logger",
                Type = loggerType!,
                IsField = true,
                IsRequired = false,
                IsRuntime = false,
                IsLogger = true,
            });
        }

        if (needsContextAccessorCtorParam)
        {
            requisiteMembers.Add(new RequisiteMember
            {
                Name = "ContextAccessor",
                Type = contextAccessorType!,
                IsField = true,
                IsRequired = false,
                IsRuntime = false,
                IsContextAccessor = true,
            });
        }

        if (requisiteMembers.Count == 0 && !needsObserve)
        {
            return null;
        }

        if (!IsPartialClass(classSymbol))
        {
            return null;
        }

        var injectionCode = GenerateInjectionCodeFromTemplate(
            classSymbol,
            requisiteMembers,
            emitLoggerField: emitLoggerField,
            emitContextAccessorField: emitContextAccessorField,
            emitContextAccessorProperty: emitContextAccessorProperty,
            existingAccessorMemberName: existingAccessorMember,
            emitObserveMethod: needsObserve);

        if (string.IsNullOrEmpty(injectionCode))
        {
            return null;
        }

        var namespacePart = classSymbol.ContainingNamespace.ToDisplayString().Replace(".", "_");
        var fileName = $"{namespacePart}_{classSymbol.Name}_RequisiteInjection.g.cs";
        return new RequisiteInjectionResult(fileName, injectionCode);
    }

    private static bool LoggerWillExistInChain(INamedTypeSymbol classSymbol)
    {
        var current = classSymbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            if (HasSourceDeclaredLogger(current))
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
                    && IsContextAccessorType(field.Type, contextAccessorType)
                    && (isDeclaringType || IsAccessibleFromDerived(field)))
                {
                    return field.Name;
                }
                if (member is IPropertySymbol property
                    && IsContextAccessorType(property.Type, contextAccessorType)
                    && (isDeclaringType || IsAccessibleFromDerived(property)))
                {
                    return property.Name;
                }
            }
            current = current.BaseType;
        }
        return null;
    }

    private static bool IsContextAccessorType(
        ITypeSymbol candidate,
        INamedTypeSymbol contextAccessorType)
    {
        return SymbolEqualityComparer.Default.Equals(candidate, contextAccessorType);
    }

    private static bool IsAccessibleFromDerived(ISymbol member)
    {
        return member.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal
            or Accessibility.Internal;
    }

    private static bool ObserveWillExistInChain(INamedTypeSymbol classSymbol)
    {
        var current = classSymbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            if (HasSourceDeclaredObserve(current))
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

    private static bool HasSourceDeclaredLogger(INamedTypeSymbol classSymbol)
    {
        foreach (var member in classSymbol.GetMembers("Logger"))
        {
            if (member is IFieldSymbol || member is IPropertySymbol)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasSourceDeclaredObserve(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetMembers("Observe").OfType<IMethodSymbol>().Any();
    }

    private static string GenerateInjectionCodeFromTemplate(
        INamedTypeSymbol classSymbol,
        List<RequisiteMember> requisiteMembers,
        bool emitLoggerField,
        bool emitContextAccessorField,
        bool emitContextAccessorProperty,
        string? existingAccessorMemberName,
        bool emitObserveMethod)
    {
        var className = classSymbol.Name;
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

        var runtimeMembers = requisiteMembers.Where(m => m.IsRuntime).ToList();
        var diMembers = requisiteMembers.Where(m => !m.IsRuntime).ToList();
        var orderedMembers = runtimeMembers.Concat(diMembers).ToList();

        var emitConstructor = orderedMembers.Count > 0 && !HasExistingConstructor(classSymbol);

        if (!emitConstructor
            && !emitLoggerField
            && !emitContextAccessorField
            && !emitContextAccessorProperty
            && !emitObserveMethod)
        {
            return string.Empty;
        }

        var typeParameters = BuildTypeParameters(classSymbol);

        var sectionItems = new List<Compositor>();

        if (emitContextAccessorField)
        {
            sectionItems.Add(new I.ContextAccessorField());
        }

        if (emitContextAccessorProperty)
        {
            sectionItems.Add(new I.ContextAccessorProperty
            {
                SourceMember = existingAccessorMemberName!,
            });
        }

        if (emitLoggerField)
        {
            sectionItems.Add(new I.LoggerField());
        }

        if (emitObserveMethod)
        {
            sectionItems.Add(new I.ObserveMethod());
        }

        if (emitConstructor)
        {
            sectionItems.Add(BuildConstructor(classSymbol, className, orderedMembers));
        }

        var sections = Sequence.BlankLines(sectionItems);

        var body = new I.InjectionFile
        {
            ClassName = className,
            TypeParameters = typeParameters,
            Sections = sections,
        };

        return new RequisiteFile
        {
            Namespace = namespaceName,
            Pragmas = "#pragma warning disable CS8618",
            Usings = new[]
            {
                "global::System",
                "global::System.Collections.Generic",
                "global::System.Reflection",
            },
            Body = body.Render(),
        }.Render();
    }

    private static Compositor BuildConstructor(
        INamedTypeSymbol classSymbol,
        string className,
        List<RequisiteMember> orderedMembers)
    {
        var parameters = Sequence.CommaList(orderedMembers.Select(m => (Compositor)BuildParameter(m)));

        var assignments = Sequence.Lines(orderedMembers.Select(m => (Compositor)BuildAssignment(classSymbol, m)));

        return new I.Constructor
        {
            ClassName = className,
            Parameters = parameters,
            Assignments = assignments,
        };
    }

    private static Compositor BuildParameter(RequisiteMember member)
    {
        var paramName = GeneratorNaming.ToCamelCase(member.Name);
        var typeDisplay = member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!member.IsRequired && !member.Type.IsValueType && !typeDisplay.EndsWith("?"))
        {
            typeDisplay += "?";
        }
        return new I.ParameterDecl
        {
            ParamType = typeDisplay,
            ParamName = paramName,
            DefaultClause = member.IsContextAccessor ? " = null" : string.Empty,
        };
    }

    private static Compositor BuildAssignment(INamedTypeSymbol classSymbol, RequisiteMember member)
    {
        var paramName = GeneratorNaming.ToCamelCase(member.Name);
        var useNullCheck = member.IsRequired && !member.Type.IsValueType;
        var declaringType = member.DeclaringType ?? classSymbol;
        var declaringTypeName = declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return useNullCheck
            ? new A.NullCheckedAssignment
            {
                DeclaringTypeName = declaringTypeName,
                MemberName = member.Name,
                ParamName = paramName,
            }
            : new A.PlainAssignment
            {
                DeclaringTypeName = declaringTypeName,
                MemberName = member.Name,
                ParamName = paramName,
            };
    }

    private sealed class RequisiteFile : Templar.Presets.CSharpFile { }

    private static List<RequisiteMember> GetRequisiteMembers(INamedTypeSymbol classSymbol)
    {
        var members = new List<RequisiteMember>();
        var memberSet = new HashSet<string>();

        var directMembers = classSymbol.GetMembers().ToList();

        foreach (var member in directMembers)
        {
            ProcessRequisiteMember(
                member,
                members,
                memberSet);
        }

        foreach (var interfaceSymbol in classSymbol.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceSymbol.GetMembers())
            {
                if (HasRequisiteAttribute(interfaceMember))
                {
                    ProcessRequisiteMember(
                        interfaceMember,
                        members,
                        memberSet);
                }
            }
        }

        var baseType = classSymbol.BaseType;
        while (baseType != null && ImplementsIAtelier(baseType))
        {
            foreach (var baseMember in baseType.GetMembers())
            {
                if (HasRequisiteAttribute(baseMember))
                {
                    ProcessRequisiteMember(
                        baseMember,
                        members,
                        memberSet);
                }
            }
            baseType = baseType.BaseType;
        }

        return members;
    }

    private static bool ImplementsIAtelier(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current != null)
        {
            if (current.AllInterfaces.Any(i => i.Name == "IAtelier"))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static bool HasRequisiteAttribute(ISymbol member)
    {
        return member.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "RequisiteAttribute" ||
            a.AttributeClass?.Name == "RuntimeAttribute");
    }

    private static void ProcessRequisiteMember(
        ISymbol member,
        List<RequisiteMember> members,
        HashSet<string> memberSet,
        ISymbol? implementation = null)
    {
        var attributes = member.GetAttributes();
        var requisiteAttr = attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name == "RequisiteAttribute");
        var runtimeAttr = attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name == "RuntimeAttribute");

        if (requisiteAttr == null && runtimeAttr == null)
        {
            return;
        }

        var isRequired = true;
        var isRuntime = runtimeAttr != null;
        var targetAttr = requisiteAttr ?? runtimeAttr;

        foreach (var namedArg in targetAttr!.NamedArguments)
        {
            if (namedArg.Key == "Required" && namedArg.Value.Value is bool req)
            {
                isRequired = req;
            }
        }

        var targetMember = implementation ?? member;

        var memberKey = $"{targetMember.Name}:{targetMember.GetType().Name}:{targetMember.ContainingType?.Name ?? "Unknown"}";

        if (memberSet.Contains(memberKey))
        {
            return;
        }

        memberSet.Add(memberKey);

        switch (targetMember)
        {
            case IFieldSymbol field:
            {
                members.Add(new RequisiteMember
                {
                    Name = field.Name,
                    Type = field.Type,
                    DeclaringType = field.ContainingType as INamedTypeSymbol,
                    IsField = true,
                    IsRequired = isRequired,
                    IsRuntime = isRuntime,
                });
                break;
            }

            case IPropertySymbol property:
            {
                members.Add(new RequisiteMember
                {
                    Name = property.Name,
                    Type = property.Type,
                    DeclaringType = property.ContainingType as INamedTypeSymbol,
                    IsField = false,
                    IsRequired = isRequired,
                    IsRuntime = isRuntime,
                });
                break;
            }
        }
    }

    private static bool IsPartialClass(INamedTypeSymbol classSymbol)
    {
        return classSymbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
    }

    private static bool HasExistingConstructor(INamedTypeSymbol classSymbol)
    {
        return classSymbol.Constructors.Any(c =>
            !c.IsImplicitlyDeclared &&
            c.DeclaredAccessibility == Accessibility.Public &&
            c.Parameters.Length > 0);
    }

    private static string BuildTypeParameters(INamedTypeSymbol classSymbol)
    {
        if (!classSymbol.IsGenericType)
        {
            return string.Empty;
        }

        var typeParamList = Sequence.CommaList(classSymbol.TypeParameters.Select(tp => (Compositor)new I.IdentFragment { Text = tp.Name })).Render();

        return "<" + typeParamList + ">";
    }

    private class RequisiteMember
    {
        public string Name { get; set; } = string.Empty;
        public ITypeSymbol Type { get; set; } = null!;
        public INamedTypeSymbol? DeclaringType { get; set; }
        public bool IsField { get; set; }
        public bool IsRequired { get; set; }
        public bool IsRuntime { get; set; }
        public bool IsLogger { get; set; }
        public bool IsContextAccessor { get; set; }
    }
}

internal sealed record RequisiteInjectionResult(string HintName, string Source);
