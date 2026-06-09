{{ portVars }}
{{ enableHttpsVar }}
builder.WebHost.ConfigureKestrel(options =>
{
    {{ listenBlocks }}
});
