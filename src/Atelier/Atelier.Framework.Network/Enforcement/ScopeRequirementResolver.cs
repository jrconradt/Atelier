using System.Reflection;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Network.Enforcement;

public static class ScopeRequirementResolver
{
    public static ScopeRequirement ResolveRequiredScopes(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var scopes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in method.GetCustomAttributes<RequiresScopeAttribute>(inherit: true))
        {
            scopes.Add(attribute.Scope);
        }

        var declaringType = method.DeclaringType;
        if (declaringType != null)
        {
            foreach (var attribute in declaringType.GetCustomAttributes<RequiresScopeAttribute>(inherit: true))
            {
                scopes.Add(attribute.Scope);
            }

            foreach (var attribute in declaringType.GetCustomAttributes<RequiresScopeContractAttribute>(inherit: true))
            {
                scopes.Add(attribute.Scope);
            }
        }

        var failClosed = false;
        var scopePairType = ResolveScopePairType(method);
        if (scopePairType != null)
        {
            var tierScope = ResolveTierScope(scopePairType, method);
            if (string.IsNullOrEmpty(tierScope))
            {
                failClosed = true;
            }
            else
            {
                scopes.Add(tierScope);
            }
        }

        return new ScopeRequirement(scopes, failClosed);
    }

    private static string? ResolveTierScope(Type scopePairType,
                                            MethodInfo method)
    {
        var effect = ResolveDeclaredEffect(method);
        if (effect == null)
        {
            return null;
        }

        var fieldName = effect.Value == EffectKind.Write ? "WRITE" : "READ";
        var field = scopePairType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        if (field == null)
        {
            return null;
        }

        return field.GetRawConstantValue() as string;
    }

    private static EffectKind? ResolveDeclaredEffect(MethodInfo method)
    {
        var methodAttribute = method.GetCustomAttribute<OperationEffectAttribute>(inherit: true);
        return methodAttribute?.Effect;
    }

    private static Type? ResolveScopePairType(MethodInfo method)
    {
        var declaringType = method.DeclaringType;
        if (declaringType == null)
        {
            return null;
        }

        var declared = declaringType.GetCustomAttribute<ScopeResourceAttribute>(inherit: true);
        if (declared != null)
        {
            return declared.ScopePairType;
        }

        foreach (var contract in declaringType.GetInterfaces())
        {
            var contractAttribute = contract.GetCustomAttribute<ScopeResourceAttribute>(inherit: true);
            if (contractAttribute != null)
            {
                return contractAttribute.ScopePairType;
            }
        }

        return null;
    }

    public static bool TryResolveAllowSelf(MethodInfo method,
                                           out string identityParameterName)
    {
        ArgumentNullException.ThrowIfNull(method);

        var methodAttribute = method.GetCustomAttribute<AllowSelfAttribute>(inherit: true);
        if (methodAttribute != null)
        {
            identityParameterName = methodAttribute.IdentityParameterName;
            return true;
        }

        var declaringType = method.DeclaringType;
        if (declaringType != null)
        {
            var contractAttribute = declaringType.GetCustomAttribute<AllowSelfContractAttribute>(inherit: true);
            if (contractAttribute != null)
            {
                identityParameterName = contractAttribute.IdentityPropertyName;
                return true;
            }
        }

        identityParameterName = string.Empty;
        return false;
    }

    public static string? ReadIdentityArgument(MethodInfo method,
                                                object?[] arguments,
                                                string identityParameterName)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(identityParameterName);

        var parameters = method.GetParameters();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (string.Equals(parameters[index].Name, identityParameterName, StringComparison.Ordinal)
                && index < arguments.Length)
            {
                return arguments[index]?.ToString();
            }
        }

        return null;
    }
}
