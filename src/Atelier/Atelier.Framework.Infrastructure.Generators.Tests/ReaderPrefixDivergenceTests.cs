using Atelier.Framework.Identity.Authorization;
using Xunit;

namespace Atelier.Framework.Infrastructure.Generators.Tests;

public sealed class ReaderPrefixDivergenceTests
{
    [Fact]
    public void GeneratorAndRuntimeReaderPrefixListsAreIdentical()
    {
        Assert.Equal(OperationEffectClassifier.READER_PREFIXES,
                     NamingConventions.READER_PREFIXES);
    }

    [Theory]
    [InlineData("GetBoutique")]
    [InlineData("FetchBoutiqueAsync")]
    [InlineData("RetrieveItem")]
    [InlineData("DiscoverBoutiquesAsync")]
    [InlineData("FindByName")]
    [InlineData("ListAll")]
    [InlineData("QueryState")]
    [InlineData("SearchCatalog")]
    [InlineData("CreateBoutique")]
    [InlineData("AddItemAsync")]
    [InlineData("UpdateState")]
    [InlineData("DeleteBoutique")]
    [InlineData("PatchRecord")]
    [InlineData("PublishEvent")]
    [InlineData("Frobnicate")]
    [InlineData("GetOrCreate")]
    [InlineData("GetOrCreateBoutique")]
    [InlineData("FindOrCreate")]
    [InlineData("ListAndPurge")]
    [InlineData("SearchAndReplace")]
    [InlineData("RetrieveAndDelete")]
    [InlineData("FetchAndUpdate")]
    [InlineData("QueryAndModify")]
    [InlineData("QueryThenPurge")]
    [InlineData("DiscoverOrProvision")]
    [InlineData("GetOrCreateAsync")]
    public void GeneratorReaderDetectionMatchesRuntimeClassifier(string methodName)
    {
        Assert.Equal(NamingConventions.IsReaderMethod(methodName),
                     !OperationEffectClassifier.IsMutatingOperation(methodName));
    }
}
