builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
                origin == "null" ||
                origin.StartsWith("http:") ||
                origin.StartsWith("https:"))
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
