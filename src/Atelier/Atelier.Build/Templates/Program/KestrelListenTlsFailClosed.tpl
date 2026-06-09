else
{
    throw new System.InvalidOperationException("TLS endpoint '{{ endpointName }}' is declared secure but no usable certificate was found at startup; refusing to listen in plaintext.");
}