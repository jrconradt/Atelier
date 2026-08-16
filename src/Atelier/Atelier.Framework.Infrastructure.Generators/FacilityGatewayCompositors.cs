using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Templar.Rendering;
using Atelier.Framework.Generators.Requisition;

namespace Atelier.Framework.Infrastructure.Generators.Compositors.Facility;

public sealed class Client : Compositor
{
    public string NamespaceName { get; }
    public string InterfaceName { get; }
    public string ClientName { get; }
    public IEnumerable<ClientMethod> Methods { get; }
    public IEnumerable<ClientDto> Dtos { get; }

    public Client(INamedTypeSymbol interfaceSymbol, string facilityName)
    {
        var className = interfaceSymbol.Name.StartsWith("I") ? interfaceSymbol.Name.Substring(1) : interfaceSymbol.Name;
        NamespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
        InterfaceName = FacilityGatewaySourceGenerator.DisplayName(interfaceSymbol);
        ClientName = $"{className}HttpClient";

        var methods = interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var lowerFacility = facilityName.ToLowerInvariant();
        Methods = methods.Select(m => new ClientMethod(m, lowerFacility)).ToList();

        var dtosList = new List<ClientDto>();
        foreach (var m in methods)
        {
            var normalParams = m.Parameters.Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken").ToList();
            if (normalParams.Count > 1)
            {
                dtosList.Add(new ClientDto(m));
            }
        }
        Dtos = dtosList;
    }
}

public sealed class ClientMethod : Compositor
{
    public string ReturnType { get; }
    public string MethodName { get; }
    public string Parameters { get; }
    public string LowerFacility { get; }
    public string LowerMethodName { get; }
    public string RequestContentCode { get; }
    public string CtParam { get; }
    public string UnwrappedReturnType { get; }

    public ClientMethod(IMethodSymbol method, string lowerFacility)
    {
        var className = method.ContainingType.Name.StartsWith("I") ? method.ContainingType.Name.Substring(1) : method.ContainingType.Name;
        ReturnType = FacilityGatewaySourceGenerator.DisplayName(method.ReturnType);
        MethodName = method.Name;
        Parameters = string.Join(", ", method.Parameters.Select(p => $"{FacilityGatewaySourceGenerator.DisplayName(p.Type)} {p.Name}"));
        LowerFacility = lowerFacility;
        LowerMethodName = method.Name.ToLowerInvariant();

        var normalParams = method.Parameters.Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken").ToList();
        if (normalParams.Count == 0)
        {
            RequestContentCode = "request.Content = new StringContent(string.Empty);";
        }
        else if (normalParams.Count == 1)
        {
            RequestContentCode = $"request.Content = JsonContent.Create({normalParams[0].Name});";
        }
        else
        {
            var dtoClassName = $"{className}{method.Name}Request";
            var dtoJsonProps = string.Join(", ", normalParams.Select(p => $"{char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)} = {p.Name}"));
            RequestContentCode = $"request.Content = JsonContent.Create(new {dtoClassName} {{ {dtoJsonProps} }});";
        }

        CtParam = method.Parameters.FirstOrDefault(p => p.Type.ToDisplayString() == "System.Threading.CancellationToken")?.Name ?? "default";
        UnwrappedReturnType = FacilityGatewaySourceGenerator.DisplayName(FacilityGatewaySourceGenerator.UnwrapTask(method.ReturnType));
    }
}

public sealed class ClientDto : Compositor
{
    public string DtoClassName { get; }
    public string DtoProps { get; }

    public ClientDto(IMethodSymbol method)
    {
        var className = method.ContainingType.Name.StartsWith("I") ? method.ContainingType.Name.Substring(1) : method.ContainingType.Name;
        DtoClassName = $"{className}{method.Name}Request";

        var normalParams = method.Parameters.Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken").ToList();
        DtoProps = string.Join("\n", normalParams.Select(p => $"    public {FacilityGatewaySourceGenerator.DisplayName(p.Type)} {char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)} {{ get; set; }} = null!;"));
    }
}

public sealed class Endpoints : Compositor
{
    public string NamespaceName { get; }
    public string EndpointsName { get; }
    public IEnumerable<EndpointMap> Mappings { get; }

    public Endpoints(INamedTypeSymbol interfaceSymbol, string facilityName)
    {
        var className = interfaceSymbol.Name.StartsWith("I") ? interfaceSymbol.Name.Substring(1) : interfaceSymbol.Name;
        NamespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
        EndpointsName = $"{className}Endpoints";

        var methods = interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var lowerFacility = facilityName.ToLowerInvariant();
        Mappings = methods.Select(m => new EndpointMap(m, lowerFacility)).ToList();
    }
}

public sealed class EndpointMap : Compositor
{
    public string LowerFacility { get; }
    public string LowerMethodName { get; }
    public string ParameterString { get; }
    public string ClassName { get; }
    public string ValidationCheck { get; }
    public string MethodName { get; }
    public string ServiceArgs { get; }

    public EndpointMap(IMethodSymbol method, string lowerFacility)
    {
        var className = method.ContainingType.Name.StartsWith("I") ? method.ContainingType.Name.Substring(1) : method.ContainingType.Name;
        var unwrappedReturnType = FacilityGatewaySourceGenerator.DisplayName(FacilityGatewaySourceGenerator.UnwrapTask(method.ReturnType));
        
        LowerFacility = lowerFacility;
        LowerMethodName = method.Name.ToLowerInvariant();
        ClassName = className;
        MethodName = method.Name;

        var normalParams = method.Parameters.Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken").ToList();
        var ctParam = method.Parameters.FirstOrDefault(p => p.Type.ToDisplayString() == "System.Threading.CancellationToken")?.Name;

        if (normalParams.Count == 0)
        {
            ParameterString = "";
            var ctArg = ctParam != null ? "cancellationToken" : "";
            ServiceArgs = ctArg;
            ValidationCheck = "";
        }
        else if (normalParams.Count == 1)
        {
            var paramType = FacilityGatewaySourceGenerator.DisplayName(normalParams[0].Type);
            ParameterString = $"{paramType} request,";

            var ctArg = ctParam != null ? ", cancellationToken" : string.Empty;
            ServiceArgs = $"request{ctArg}";

            var isContract = normalParams[0].Type.GetAttributes().Any(a => a.AttributeClass?.Name == "ContractAttribute");
            ValidationCheck = isContract 
                ? $"if (request == null || !global::{normalParams[0].Type.ContainingNamespace.ToDisplayString()}.{normalParams[0].Type.Name}ContractValidationExtensions.IsValid(request)) {{ return Results.BadRequest({unwrappedReturnType}.Failure()); }}"
                : "if (request == null) { return Results.BadRequest(); }";
        }
        else
        {
            var dtoClassName = $"{className}{method.Name}Request";
            ParameterString = $"{dtoClassName} request,";

            var ctArg = ctParam != null ? ", cancellationToken" : string.Empty;
            var args = string.Join(", ", normalParams.Select(p => $"request.{char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)}"));
            ServiceArgs = $"{args}{ctArg}";

            var validationChecks = new List<string> { "request == null" };
            foreach (var p in normalParams)
            {
                validationChecks.Add($"request.{char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)} == null");
                if (p.Type.GetAttributes().Any(a => a.AttributeClass?.Name == "ContractAttribute"))
                {
                    validationChecks.Add($"!global::{p.Type.ContainingNamespace.ToDisplayString()}.{p.Type.Name}ContractValidationExtensions.IsValid(request.{char.ToUpperInvariant(p.Name[0]) + p.Name.Substring(1)})");
                }
            }
            ValidationCheck = $"if ({string.Join(" || ", validationChecks)}) {{ return Results.BadRequest({unwrappedReturnType}.Failure()); }}";
        }
    }
}

public sealed class Gateway : Compositor
{
    public string NamespaceName { get; }
    public string InterfaceName { get; }
    public string ClassName { get; }
    public string? TokenValidatorField { get; }
    public GatewayAuth? AuthorizeAsyncMethod { get; }
    public IEnumerable<GatewayMethod> Methods { get; }

    public Gateway(
        INamedTypeSymbol interfaceSymbol,
        bool requiresAuth,
        string[] requiredClaims,
        string[] requiredScopes,
        List<Diagnostic> diagnostics,
        DiagnosticDescriptor nonOutcomeAuthenticatedMethodRule)
    {
        NamespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
        InterfaceName = FacilityGatewaySourceGenerator.DisplayName(interfaceSymbol);
        ClassName = SymbolNaming.ImplName(interfaceSymbol.Name);

        TokenValidatorField = requiresAuth
            ? "[Requisite] private readonly global::Atelier.Framework.Identity.Interfaces.IJwtTokenValidator _tokenValidator = null!;"
            : null;

        if (requiresAuth)
        {
            var claimChecks = requiredClaims.Select(c => new GatewayAuthClaim { Claim = c }).ToList();
            
            GatewayAuthScope? scopeChecks = null;
            if (requiredScopes.Length > 0)
            {
                var scopeAssertions = requiredScopes.Select(s => new GatewayAuthScopeAssert { Scope = s }).ToList();
                scopeChecks = new GatewayAuthScope { ScopeAssertions = scopeAssertions };
            }

            AuthorizeAsyncMethod = new GatewayAuth
            {
                ClaimChecks = claimChecks,
                ScopeChecks = scopeChecks
            };
        }

        var ordinaryMethods = interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var methodCompositors = new List<GatewayMethod>();
        foreach (var method in ordinaryMethods)
        {
            if (requiresAuth
                && !FacilityGatewaySourceGenerator.IsOutcomeOfT(method.ReturnType)
                && !FacilityGatewaySourceGenerator.IsBareOutcome(method.ReturnType))
            {
                diagnostics.Add(Diagnostic.Create(
                    nonOutcomeAuthenticatedMethodRule,
                    method.Locations.FirstOrDefault() ?? interfaceSymbol.Locations.FirstOrDefault() ?? Location.None,
                    method.Name,
                    interfaceSymbol.Name));
                continue;
            }

            methodCompositors.Add(new GatewayMethod(method));
        }

        Methods = methodCompositors;
    }
}

public sealed class GatewayAuth : Compositor
{
    public IEnumerable<GatewayAuthClaim> ClaimChecks { get; init; } = Enumerable.Empty<GatewayAuthClaim>();
    public GatewayAuthScope? ScopeChecks { get; init; }
}

public sealed class GatewayAuthClaim : Compositor
{
    public string Claim { get; init; } = "";
}

public sealed class GatewayAuthScope : Compositor
{
    public IEnumerable<GatewayAuthScopeAssert> ScopeAssertions { get; init; } = Enumerable.Empty<GatewayAuthScopeAssert>();
}

public sealed class GatewayAuthScopeAssert : Compositor
{
    public string Scope { get; init; } = "";
}

public sealed class GatewayMethod : Compositor
{
    public bool IsAsync { get; }
    public string ReturnType { get; }
    public string MethodName { get; }
    public string Parameters { get; }
    public string Arguments { get; }
    public bool IsOutcomeOfT { get; }
    public bool IsBareOutcome { get; }
    public bool HasContractValidator { get; }
    public string ContractNamespace { get; }
    public string ContractName { get; }

    public GatewayMethod(IMethodSymbol method)
    {
        ReturnType = FacilityGatewaySourceGenerator.DisplayName(method.ReturnType);
        MethodName = method.Name;
        Parameters = string.Join(", ", method.Parameters.Select(p => $"{FacilityGatewaySourceGenerator.DisplayName(p.Type)} {p.Name}"));
        Arguments = string.Join(", ", method.Parameters.Select(p => p.Name));

        IsOutcomeOfT = FacilityGatewaySourceGenerator.IsOutcomeOfT(method.ReturnType);
        IsBareOutcome = FacilityGatewaySourceGenerator.IsBareOutcome(method.ReturnType);
        IsAsync = IsOutcomeOfT || IsBareOutcome;

        ContractNamespace = "";
        ContractName = "";

        if (IsOutcomeOfT && FacilityGatewaySourceGenerator.GetOutcomeInnerType(method.ReturnType) is INamedTypeSymbol inner
            && inner.GetAttributes().Any(a => a.AttributeClass?.Name == "ContractAttribute"))
        {
            HasContractValidator = true;
            ContractNamespace = inner.ContainingNamespace.ToDisplayString();
            ContractName = inner.Name;
        }
    }
}
