using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Atelier.Framework.Context;
using Atelier.Framework.Outcomes;

namespace {{ namespaceName }};

public class {{ clientName }} : {{ interfaceName }}
{
    private readonly HttpClient _httpClient;
    private readonly IContextAccessor _contextAccessor;

    public {{ clientName }}(HttpClient httpClient, IContextAccessor contextAccessor)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
    }

    private void ApplyAuthorization(HttpRequestMessage request)
    {
        if (_contextAccessor.Current != null)
        {
            var contextHeader = global::Atelier.Framework.Network.WireContextCodec.Encode(_contextAccessor.Current);
            if (!string.IsNullOrEmpty(contextHeader))
            {
                request.Headers.TryAddWithoutValidation(
                    global::Atelier.Framework.Network.WireContextCodec.CANONICAL_HEADER_NAME,
                    contextHeader);
            }

            if (_contextAccessor.Current.TryGetValue("Authorization", out var authHeader))
            {
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            }
        }
    }

    {{ methods }}
}

{{ dtos }}
