        return Results.Ok(new
        {
            success = result.IsSuccess,
            values = result.IsSuccess
                ? new object?[] { result.Data }
                : global::System.Array.Empty<object?>()
        });
