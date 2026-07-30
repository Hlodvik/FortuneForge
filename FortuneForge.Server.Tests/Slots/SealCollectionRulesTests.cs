using FortuneForge.Server.Slots.Bonuses;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SealCollectionRulesTests
{
    [Fact]
    public void CompletionTarget_IsFortySeals() =>
        Assert.Equal(40, SealCollectionRules.CompletionTarget);
}
