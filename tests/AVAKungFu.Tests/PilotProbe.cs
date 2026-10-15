using AVAKungFu.Engine;

namespace AVAKungFu.Tests;

/// <summary>How the computer fighter gets on, floor by floor (set KUNGFU_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_fights()
    {
        if (Environment.GetEnvironmentVariable("KUNGFU_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "kungfu-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var floors = new List<string>();
            var last = g.Phase;
            int stage = 1, t = 0;
            double start = 0, minEnergy = 100;
            for (; t < 60 * 900 && g.Stage <= 5; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Play) minEnergy = Math.Min(minEnergy, g.Hero.Energy);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"F{g.Floor} {g.Cause}{(g.BossFight ? " (boss)" : "")}"] = deaths.GetValueOrDefault($"F{g.Floor} {g.Cause}{(g.BossFight ? " (boss)" : "")}") + 1;
                if (g.Stage != stage) { floors.Add($"F{stage}:{(t - start) / 60:0}s"); stage = g.Stage; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: stage {g.Stage} {g.Phase} after {t / 60:0}s score {g.Score} min energy {minEnergy:0} | {string.Join(" ", floors)} | at x {g.Hero.X:0} boss hp {g.Boss.Hp:0} doing {g.Pilot.Doing}");
            output.WriteLine("   " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}
