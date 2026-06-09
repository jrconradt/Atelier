new ProductManifest
{
    ProductTypeName = typeof({{ productType }}).AssemblyQualifiedName!,
    ProductType = typeof({{ productType }}),
    AutoStart = {{ autoStart }},
    {{ configBlock }}
},
