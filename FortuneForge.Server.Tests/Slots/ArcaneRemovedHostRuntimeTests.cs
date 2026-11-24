using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Slots.Models;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcaneRemovedHostRuntimeTests
{
    [Fact]
    public async Task RetiredHostEntryPointsRejectArcaneBeforeOpeningADatabaseTransaction()
    {
        var store = new FirestoreAccountStore(null!);
        const string gameId = ArcaneArchivesEngine.GameId;
        var admission = await Assert.ThrowsAsync<ArgumentException>(() => store.BeginSlotSpinAsync(
            "arcane-native-only", gameId, 12, 25, false, false, 0,
            DateTime.UtcNow, TimeSpan.Zero, default));
        Assert.Equal("gameId", admission.ParamName);
        var state = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetSlotStateAsync("arcane-native-only", gameId, 25, default));
        Assert.Equal("gameId", state.ParamName);
        var outcome = new SpinResult(Guid.NewGuid(), gameId, "unused", "unused", "unused",
            12, 25, [], [], new(0, []), 0, false, 0, 0, 0, false);
        var settlement = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RecordSlotSpinAsync("arcane-native-only", outcome, null!, DateTime.UtcNow, default));
        Assert.Equal("gameId", settlement.ParamName);
    }
}
