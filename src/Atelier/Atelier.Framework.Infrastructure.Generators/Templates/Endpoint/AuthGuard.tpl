        var auditAtelier = context.RequestServices.GetRequiredService<global::Atelier.Framework.Observability.IAtelier>();
        if (!(context.User?.Identity?.IsAuthenticated ?? false))
        {
            auditAtelier.Observe(
                global::Atelier.Framework.Observability.LogLevel.Warning,
                values:
                [
                    ("Event", "AuthorizationDenied"),
                    ("Decision", "Unauthorized"),
                    ("Subject", context.User?.Identity?.Name ?? "anonymous"),
                    ("Path", context.Request.Path.ToString()),
                    ("RequiredClaims", "{{ requiredClaimsList }}")
                ]);
            return Results.Unauthorized();
        }

{{? claimChecks }}
        if ({{ claimChecks }})
        {
            auditAtelier.Observe(
                global::Atelier.Framework.Observability.LogLevel.Warning,
                values:
                [
                    ("Event", "AuthorizationDenied"),
                    ("Decision", "Forbidden"),
                    ("Subject", context.User?.Identity?.Name ?? "anonymous"),
                    ("Path", context.Request.Path.ToString()),
                    ("RequiredClaims", "{{ requiredClaimsList }}")
                ]);
            return Results.StatusCode(403);
        }
{{? }}
