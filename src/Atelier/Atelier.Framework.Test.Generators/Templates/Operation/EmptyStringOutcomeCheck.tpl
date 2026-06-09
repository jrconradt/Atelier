if (ret is null)
            {
                throw new InvalidOperationException("Operation declared Outcome-shaped returned null on empty-string '{{ paramName }}' — expected an Outcome");
            }
            var rt = ret.GetType();
            if (!IsAtelierOutcome(rt))
            {
                throw new InvalidOperationException($"Return value '{rt.FullName}' is not an Atelier Outcome on empty-string '{{ paramName }}'");
            }
            var isSuccess = rt.GetProperty("IsSuccess")!.GetValue(ret) as bool?;
            if (isSuccess is null)
            {
                throw new InvalidOperationException("Outcome returned on empty-string '{{ paramName }}' has a null IsSuccess — Outcome state is indeterminate");
            }