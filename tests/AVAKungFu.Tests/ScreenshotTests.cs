using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAKungFu.Engine;
using AVAKungFu.Views;

namespace AVAKungFu.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set KUNGFU_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avakungfu-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("KUNGFU_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAKungFu-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer fight until something worth a picture is on screen.</summary>
    private static bool FightUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void At(GameEngine g, int floor)
    {
        g.Go(floor);
        g.SetLives(30);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 91250, 8);
        g.HighScores.Insert("AVA", 62800, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Up_the_temple()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(30);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "floor-start");
        Assert.True(FightUntil(g, c, g => g.Hero.Act == Act.Kick && g.Hero.ActTime > 0.12 && g.Enemies.Count(e => !e.Dead) >= 2));
        Shot(w, c, "kick");
        Assert.True(FightUntil(g, c, g => g.Hero.Act == Act.Held && g.Enemies.Count(e => e.Holding) >= 1 && g.Hero.Shake >= 2));
        Shot(w, c, "held");
        Assert.True(FightUntil(g, c, g => g.Missiles.Any(m => Math.Abs(m.X - g.Hero.X) < 120) && g.Hero.Crouching));
        Shot(w, c, "knife");
        At(g, 1);
        g.ToBoss(150);
        Assert.True(FightUntil(g, c, g => g.Boss.Move == "windup" && g.Boss.MoveTime > 0.2 && Math.Abs(g.Boss.X - g.Hero.X) < 150));
        Shot(w, c, "staff");
        At(g, 2);
        Assert.True(FightUntil(g, c, g => g.Enemies.Any(e => e.Kind == EnemyKind.Jar && e.Y is > 80 and < 260 && Math.Abs(e.X - g.Hero.X) < 400) && g.Enemies.Count(e => !e.Dead) >= 2));
        Shot(w, c, "jars-and-snakes");
        At(g, 3);
        g.ToBoss(140);
        Assert.True(FightUntil(g, c, g => g.Boss.Move == "strike" || (g.Hero.Act == Act.Kick && g.Hero.ActTime > 0.12)));
        Shot(w, c, "giant");
        At(g, 4);
        g.ToBoss(160);
        bool fire = FightUntil(g, c, g => g.Missiles.Any(m => m.Fire && Math.Abs(m.X - g.Hero.X) < 160));
        Assert.True(fire, $"phase {g.Phase} floor {g.Floor} boss {g.Boss.Kind} hp {g.Boss.Hp} hidden {g.Boss.Hidden} mt {g.Boss.MoveTime:0.00} struck {g.Boss.Struck} bx {g.Boss.X:0} hx {g.Hero.X:0} fight {g.BossFight} doing {g.Pilot.Doing}");
        Shot(w, c, "sorcerer");
        At(g, 5);
        g.ToBoss(160);
        Assert.True(FightUntil(g, c, g => g.Boss.Beaten || (g.Boss.Hp < g.Boss.MaxHp * 0.6 && g.Boss.Move == "windup")));
        Shot(w, c, "master");
        Assert.True(FightUntil(g, c, g => g.Phase == Phase.Rescue && g.PhaseTime > 0.5, 400));
        Shot(w, c, "rescue");
    }

    [AvaloniaFact]
    public void Down()
    {
        var (w, c, g) = Open(4);
        g.Go(1);
        g.SetEnergy(0);
        g.Idle(20);
        c.Advance(0.3);
        Shot(w, c, "down");
    }
}
