var instance = _pool.Rent();

    if (specification != null)
    {
        MapFromSpecification(instance, specification);
    }
