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

    public {{ clientName }}(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    private void ApplyAuthorization(HttpRequestMessage request)
    {
        var context = AmbientContext.Current;
        var contextHeader = global::Atelier.Framework.Network.WireContextCodec.Encode(context);
        if (!string.IsNullOrEmpty(contextHeader))
        {
            request.Headers.TryAddWithoutValidation(
                global::Atelier.Framework.Network.WireContextCodec.CANONICAL_HEADER_NAME,
                contextHeader);
        }

        if (context.TryGetValue("Authorization", out var authHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        }
    }

    {{ methods }}
}

{{ dtos }}
