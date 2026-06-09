var manifest = new BoutiqueManifest
{
    BoutiqueId = "{{ boutiqueId }}",
    Name = "{{ name }}",
    Description = "{{ description }}",
    Version = "{{ version }}",
    Capabilities = new BoutiqueCapabilities
    {
        SupportsRest = {{ supportsRest }},
        SupportsGrpc = {{ supportsGrpc }},
        SupportsWebSocket = {{ supportsWebSocket }},
        SupportsGraphQL = {{ supportsGraphQL }}
    },
    {{ productsBlock }}
};
