using TenMillionBlocks.Progression;

static void Expect(double seconds, int expected)
{
    int actual = CompletionScore.CalculatePercent(seconds);
    if (actual != expected) throw new InvalidOperationException($"{seconds}s => {actual}% (expected {expected}%).");
}

Expect(0, 100);
Expect(299.999, 100);
Expect(300, 90);
Expect(599.999, 90);
Expect(600, 80);
Expect(899.999, 80);
Expect(900, 70);
Expect(1199.999, 70);
Expect(1200, 60);
Expect(1499.999, 60);
Expect(1500, 50);
Expect(1799.999, 50);
Expect(1800, 40);
Expect(2099.999, 40);
Expect(2100, 30);
Expect(2399.999, 30);
Expect(2400, 20);
Expect(99999, 20);

if (CompletionScore.CalculateBonus(10_000, 70) != 7_000)
    throw new InvalidOperationException("10,000 blocks at 70% must award 7,000.");
if (CompletionScore.CalculateBonus(6_824, 20) != 1_365)
    throw new InvalidOperationException("6,824 blocks at 20% must round to 1,365.");

Console.WriteLine("Completion score contract passed.");
