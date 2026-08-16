using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Atelier.Framework.Outcomes;

namespace {{ namespaceName }};

public static class {{ endpointsName }}
{
    public static void Map{{ endpointsName }}(this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
    {
        {{ mappings }}
    }
}
