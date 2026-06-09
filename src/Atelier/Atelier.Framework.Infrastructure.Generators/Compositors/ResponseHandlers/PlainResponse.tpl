        return Results.Ok(new
        {
            success = result is not null,
            values = result is not null ? new object?[] { result } : global::System.Array.Empty<object?>()
        });
