using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.Dice.SicBo;
using Xunit;

namespace FortuneForge.Server.Tests.Practice;

public sealed class WalletPracticeStoreTests
{
    [Fact]
    public async Task VideoPoker_UsesAnIsolatedPracticeBalanceAcrossDealAndDraw()
    {
        var service = new VideoPokerService(new PracticeVideoPokerStore(), TimeProvider.System);
        var deal = await service.StartAsync("player", new CreateVideoPokerRoundRequest(5), "practice-video-poker-deal-0001", CancellationToken.None);
        var draw = await service.DrawAsync("player", deal.RoundId, new DrawVideoPokerRoundRequest([]), "practice-video-poker-draw-0001", CancellationToken.None);

        Assert.Equal(9_995m, deal.Balance);
        Assert.True(draw.Balance >= 9_995m);
        Assert.Equal("completed", draw.Phase);
    }

    [Fact]
    public async Task Baccarat_UsesAnIsolatedPracticeBalance()
    {
        var service = new BaccaratService(new PracticeBaccaratStore(), TimeProvider.System);
        var round = await service.StartAsync("player", new CreateBaccaratRoundRequest("player", 10m), "practice-baccarat-deal-0001", CancellationToken.None);

        Assert.InRange(round.Balance, 9_990m, 10_010m);
        Assert.Equal("settled", round.Phase);
    }

    [Fact]
    public async Task CasinoWar_UsesAnIsolatedPracticeBalance()
    {
        var service = new CasinoWarService(new PracticeCasinoWarStore(), TimeProvider.System);
        var round = await service.StartAsync("player", new CreateCasinoWarRoundRequest(10m, 0m), "practice-casino-war-deal-0001", CancellationToken.None);

        Assert.InRange(round.Balance, 9_990m, 10_010m);
        Assert.True(round.Phase is "completed" or "awaiting-tie-decision");
    }

    [Fact]
    public async Task SicBo_UsesAnIsolatedPracticeBalance()
    {
        var service = new SicBoService(new PracticeSicBoStore(), TimeProvider.System);
        var round = await service.StartAsync("player", new CreateSicBoRoundRequest([
            new SicBoBetRequest("small", 10m, null, null, null, null),
        ]), "practice-sic-bo-roll-0001", CancellationToken.None);

        Assert.InRange(round.Balance, 9_990m, 10_010m);
        Assert.Equal("settled", round.Phase);
    }
}
