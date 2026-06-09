using Atelier.Framework.Identity.Authorization;
using Xunit;

namespace Atelier.Framework.Infrastructure.Generators.Tests;

public sealed class OperationEffectClassifierHeavyTests
{
    [Theory]
    [InlineData("Get")]
    [InlineData("Fetch")]
    [InlineData("Retrieve")]
    [InlineData("Discover")]
    [InlineData("Find")]
    [InlineData("List")]
    [InlineData("Query")]
    [InlineData("Search")]
    [InlineData("GetBoutique")]
    [InlineData("FetchItem")]
    [InlineData("RetrieveRecord")]
    [InlineData("DiscoverPeers")]
    [InlineData("FindByName")]
    [InlineData("ListAll")]
    [InlineData("QueryState")]
    [InlineData("SearchCatalog")]
    public void EveryReaderPrefixClassifiesAsReader(string methodName)
    {
        Assert.False(OperationEffectClassifier.IsMutatingOperation(methodName));
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Add")]
    [InlineData("Insert")]
    [InlineData("Register")]
    [InlineData("Publish")]
    [InlineData("Submit")]
    [InlineData("Send")]
    [InlineData("Post")]
    [InlineData("Start")]
    [InlineData("Begin")]
    [InlineData("Invoke")]
    [InlineData("Execute")]
    [InlineData("Handle")]
    [InlineData("Update")]
    [InlineData("Modify")]
    [InlineData("Edit")]
    [InlineData("Replace")]
    [InlineData("Set")]
    [InlineData("Delete")]
    [InlineData("Remove")]
    [InlineData("Unregister")]
    [InlineData("Release")]
    [InlineData("Revoke")]
    [InlineData("Stop")]
    [InlineData("Cancel")]
    [InlineData("Patch")]
    [InlineData("CreateBoutique")]
    [InlineData("DeleteBoutique")]
    [InlineData("PublishEvent")]
    [InlineData("RevokeGrant")]
    public void MutatorPrefixesClassifyAsMutator(string methodName)
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation(methodName));
    }

    [Theory]
    [InlineData("Frobnicate")]
    [InlineData("Reticulate")]
    [InlineData("Xyzzy")]
    [InlineData("Process")]
    [InlineData("Reconcile")]
    [InlineData("Sync")]
    public void UnknownNameClassifiesAsMutatorFailClosed(string methodName)
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation(methodName));
    }

    [Fact]
    public void EmptyNameClassifiesAsMutatorFailClosed()
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation(string.Empty));
    }

    [Fact]
    public void NullNameClassifiesAsMutatorFailClosed()
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation(null!));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void WhitespaceNameClassifiesAsMutatorFailClosed(string methodName)
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation(methodName));
    }

    [Theory]
    [InlineData("GetBoutiqueAsync")]
    [InlineData("FetchItemAsync")]
    [InlineData("RetrieveRecordAsync")]
    [InlineData("ListAllAsync")]
    [InlineData("QueryStateAsync")]
    public void ReaderWithAsyncSuffixStillClassifiesAsReader(string methodName)
    {
        Assert.False(OperationEffectClassifier.IsMutatingOperation(methodName));
    }

    [Theory]
    [InlineData("CreateBoutiqueAsync")]
    [InlineData("DeleteBoutiqueAsync")]
    [InlineData("UpdateStateAsync")]
    [InlineData("PublishEventAsync")]
    [InlineData("FrobnicateAsync")]
    public void MutatorWithAsyncSuffixStillClassifiesAsMutator(string methodName)
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation(methodName));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("GET")]
    [InlineData("gEtBoutique")]
    [InlineData("FETCHItem")]
    [InlineData("ListAll")]
    [InlineData("LISTALL")]
    public void ReaderPrefixMatchIsCaseInsensitive(string methodName)
    {
        Assert.False(OperationEffectClassifier.IsMutatingOperation(methodName));
    }

    [Fact]
    public void OnlyAsyncStripsToEmptyAndFailsClosed()
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation("Async"));
    }

    [Theory]
    [InlineData("GetOrCreate")]
    [InlineData("GetOrCreateBoutique")]
    [InlineData("FindOrCreate")]
    [InlineData("FindOrCreateBoutique")]
    [InlineData("ListAndPurge")]
    [InlineData("SearchAndReplace")]
    [InlineData("RetrieveAndDelete")]
    [InlineData("FetchAndUpdate")]
    [InlineData("QueryAndModify")]
    [InlineData("GetOrCreateAsync")]
    public void ReaderPrefixedMutationMustRequireWriteClassification(string methodName)
    {
        Assert.True(OperationEffectClassifier.IsMutatingOperation(methodName));
    }
}
