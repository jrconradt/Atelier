if (ret is null)
            {
                throw new InvalidOperationException("Operation declared Outcome-shaped returned null on default input — expected an Outcome");
            }
            var rt = ret.GetType();
            if (!IsAtelierOutcome(rt))
            {
                throw new InvalidOperationException($"Return value '{rt.FullName}' is not an Atelier Outcome on default input");
            }
            var isSuccess = rt.GetProperty("IsSuccess")!.GetValue(ret) as bool?;
            if (isSuccess is null)
            {
                throw new InvalidOperationException("Outcome returned on default input has a null IsSuccess — Outcome state is indeterminate");
            }