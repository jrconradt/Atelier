        return Results.Ok(new
        {
            success = result.IsSuccess,
            values = result.IsSuccess
                ? (global::System.Collections.IEnumerable?)(object?)result.Data ?? global::System.Array.Empty<object?>()
                : global::System.Array.Empty<object?>()
        });
