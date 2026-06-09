builder.Services.AddSingleton<Atelier.Framework.Offering.IOfferingProvider>(sp =>
    new NullOfferingProvider());
