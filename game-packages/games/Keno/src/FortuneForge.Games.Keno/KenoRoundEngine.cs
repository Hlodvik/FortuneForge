namespace FortuneForge.Games.Keno;

public static class KenoRoundEngine
{
    public static KenoDraw Draw(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var numbers = Enumerable.Range(1, KenoTicket.MaximumNumber).ToArray();
        for (var index = 0; index < KenoDraw.DrawCount; index++)
        {
            var selectedIndex = random.Next(index, numbers.Length);
            (numbers[index], numbers[selectedIndex]) = (numbers[selectedIndex], numbers[index]);
        }

        return new KenoDraw(numbers.Take(KenoDraw.DrawCount));
    }

    public static KenoRound Play(KenoTicket ticket, KenoDraw draw, IKenoPaytable? paytable = null)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(draw);

        var hitCount = ticket.Numbers.Intersect(draw.Numbers).Count();
        var outcome = paytable?.Evaluate(ticket, hitCount);
        return new KenoRound(new KenoRoundResult(ticket, draw, hitCount, outcome));
    }
}
