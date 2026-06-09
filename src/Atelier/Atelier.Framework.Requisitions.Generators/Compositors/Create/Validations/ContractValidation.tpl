var validationResult = _validator.Validate(instance);
    if (!validationResult.IsValid)
    {
        throw new ValidationException(validationResult.Errors);
    }

    return instance;
