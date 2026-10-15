using AVAKungFu.Engine;
using Engine = AVAKungFu.Engine;

namespace AVAKungFu.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avakungfu-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play on a floor, with nobody coming unless a test puts them there.</summary>
    private static GameEngine Game(int floor = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(floor);
        return g;
    }

    private static readonly Engine.Controls Nothing = default;
    private static Engine.Controls Walk(int dx) => new(dx);
    private static readonly Engine.Controls Punch = new(Punch: true), Kick = new(Kick: true), Duck = new(0, 1), Jump = new(0, -1);
    private static readonly Engine.Controls LowKick = new(0, 1, Kick: true), LowPunch = new(0, 1, Punch: true);

    // ───────────────────────────── moving ─────────────────────────────

    [Fact]
    public void Floor_one_goes_left_floor_two_right_and_the_hero_starts_at_the_near_end()
    {
        var g = Game(1);
        Assert.Equal(-1, g.Way);
        Assert.True(g.Hero.X > Field.Length - 300);
        double x = g.Hero.X;
        g.Run(30, Walk(-1));
        Assert.Equal(x - GameEngine.WalkSpeed / 2, g.Hero.X, 0);
        g = Game(2);
        Assert.Equal(1, g.Way);
        Assert.True(g.Hero.X < 300);
    }

    [Fact]
    public void Up_jumps_and_he_comes_down_again_and_down_crouches()
    {
        var g = Game();
        g.Run(1, Jump);
        g.Idle(10);
        Assert.True(g.Hero.Airborne);
        double peak = 0;
        for (int i = 0; i < 60; i++) { g.Idle(1); peak = Math.Max(peak, g.Hero.Y); }
        Assert.True(peak > 80);
        Assert.False(g.Hero.Airborne);
        g.Run(5, Duck);
        Assert.True(g.Hero.Crouching);
    }

    [Fact]
    public void A_walk_the_other_way_turns_him_round()
    {
        var g = Game();
        Assert.Equal(-1, g.Hero.Facing);
        g.Run(3, Walk(1));
        Assert.Equal(1, g.Hero.Facing);
    }

    // ───────────────────────────── fighting the crowd ─────────────────────────────

    [Fact]
    public void A_kick_or_a_punch_drops_a_clinger_in_reach()
    {
        var g = Game();
        var a = g.Put(EnemyKind.Clinger, 60);
        g.Run(2, Kick);
        g.Idle(20);
        Assert.True(a.Dead);
        Assert.Equal(100, g.Score);
        var b = g.Put(EnemyKind.Clinger, 50);
        g.Run(2, Punch);
        g.Idle(20);
        Assert.True(b.Dead);
        // Out of reach, the kick misses.
        var c = g.Put(EnemyKind.Clinger, 200);
        g.Run(2, Kick);
        g.Idle(5);
        Assert.False(c.Dead);
    }

    [Fact]
    public void A_clinger_that_gets_hold_drains_energy_until_shaken_off()
    {
        var g = Game();
        g.Put(EnemyKind.Clinger, 20);
        g.Idle(10);
        Assert.Equal(Act.Held, g.Hero.Act);
        double e = g.Hero.Energy;
        g.Idle(60);
        Assert.Equal(e - GameEngine.HoldDrain, g.Hero.Energy, 0);
        // Wriggle.
        for (int i = 0; i < GameEngine.ShakesToFree + 2; i++) { g.Run(1, Walk(1)); g.Run(1, Walk(-1)); }
        Assert.NotEqual(Act.Held, g.Hero.Act);
        Assert.DoesNotContain(g.Enemies, en => en.Holding);
    }

    [Fact]
    public void A_blade_man_takes_two_hits()
    {
        var g = Game();
        var b = g.Put(EnemyKind.BladeMan, 70);
        g.Run(2, Kick);
        g.Idle(25);
        Assert.False(b.Dead);
        Assert.Equal(1, b.Hp);
        b.X = g.Hero.X + g.Hero.Facing * 70;
        g.Run(2, Kick);
        g.Idle(25);
        Assert.True(b.Dead);
        Assert.Equal(500, g.Score);
    }

    [Fact]
    public void High_knives_pass_over_a_crouch_and_low_ones_under_a_jump()
    {
        var g = Game();
        g.PutMissile(200, Level.High);
        g.Run(40, Duck);
        Assert.Equal(100, g.Hero.Energy);
        g.PutMissile(160, Level.Low);
        g.Run(1, Jump);
        g.Idle(40);
        Assert.Equal(100, g.Hero.Energy);
        // Standing still, a knife hurts.
        g.PutMissile(200, Level.High);
        g.Idle(40);
        Assert.Equal(100 - GameEngine.KnifeDamage, g.Hero.Energy);
    }

    [Fact]
    public void An_imp_on_the_ground_takes_only_a_low_blow()
    {
        var g = Game();
        var imp = g.Put(EnemyKind.Imp, 60);
        imp.Timer = 99;
        g.Run(2, Punch);
        g.Idle(3);
        Assert.False(imp.Dead);
        // (Once the punch is over.)
        g.Idle(15);
        imp.X = g.Hero.X + g.Hero.Facing * 60;
        g.Run(2, LowKick);
        g.Idle(20);
        Assert.True(imp.Dead);
    }

    [Fact]
    public void An_imp_that_leaps_onto_the_hero_hangs_on_like_a_clinger()
    {
        var g = Game();
        var imp = g.Put(EnemyKind.Imp, 140);
        imp.Timer = 0;
        for (int i = 0; i < 120 && !imp.Holding; i++) g.Idle(1);
        Assert.True(imp.Holding);
        g.Idle(1);
        Assert.Equal(Act.Held, g.Hero.Act);
    }

    [Fact]
    public void A_jar_breaks_on_the_floor_into_a_snake_that_only_a_low_blow_stops()
    {
        var g = Game(2);
        var jar = g.Put(EnemyKind.Jar, 140, 400);
        for (int i = 0; i < 120 && jar.Kind == EnemyKind.Jar; i++) g.Idle(1);
        Assert.Equal(EnemyKind.Snake, jar.Kind);
        for (int i = 0; i < 60 && Math.Abs(jar.X - g.Hero.X) > 70; i++) g.Run(1, Duck);
        g.Run(2, LowKick);
        g.Idle(20);
        Assert.True(jar.Dead);
    }

    // ───────────────────────────── the keepers ─────────────────────────────

    [Fact]
    public void The_keeper_bars_the_way_until_beaten_then_the_hero_climbs_on()
    {
        var g = Game(1);
        g.ToBoss(60);
        g.Run(60, Walk(g.Way));
        Assert.True(Math.Abs(g.Hero.X - g.Boss.X) >= 45);
        Assert.True(g.BossFight);
        // Beat it with kicks, minding nothing (it fights back, but it falls first).
        g.SetEnergy(100);
        for (int i = 0; i < 60 * 30 && !g.Boss.Beaten; i++)
        {
            g.Hero.Energy = 100;
            g.Hero.X = g.Boss.X - g.Way * 70;
            g.Hero.Facing = g.Way;
            g.Tick(i % 2 == 0 ? Kick : Nothing);
        }
        Assert.True(g.Boss.Beaten);
        Assert.Equal(Phase.ToStairs, g.Phase);
        g.Idle(60 * 4);
        Assert.Equal(Phase.FloorClear, g.Phase);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Floor);
    }

    [Fact]
    public void A_keepers_blow_can_be_ducked_or_jumped_by_its_height()
    {
        var g = Game(1);
        g.ToBoss(90);
        int hurt = 0, dodged = 0;
        double before = g.Hero.Energy;
        for (int i = 0; i < 60 * 12; i++)
        {
            var b = g.Boss;
            var c = Nothing;
            if (b.Move == "windup") c = b.Aim == Level.High ? Duck : b.MoveTime > g.WindUp - 0.2 ? Jump : Nothing;
            g.Tick(c);
            if (b.Move == "strike" && b.MoveTime < 0.02)
            {
                if (g.Hero.Energy < before) hurt++; else dodged++;
                before = g.Hero.Energy;
            }
        }
        Assert.True(dodged >= 3);
        Assert.Equal(0, hurt);
    }

    [Fact]
    public void The_thrower_keeps_back_and_his_boomerang_comes_home()
    {
        var g = Game(2);
        Assert.Equal(BossKind.Thrower, g.Boss.Kind);
        g.ToBoss(300);
        bool thrown = false, home = false;
        for (int i = 0; i < 60 * 8; i++)
        {
            g.Hero.Energy = 100;
            g.Run(1, Duck);
            if (g.Missiles.Any(m => m.Returns)) thrown = true;
            if (g.Missiles.Any(m => m.Returning)) home = true;
        }
        Assert.True(thrown && home);
    }

    [Fact]
    public void The_sorcerer_vanishes_and_comes_back_and_cant_be_hit_unseen()
    {
        var g = Game(4);
        Assert.Equal(BossKind.Sorcerer, g.Boss.Kind);
        g.ToBoss(150);
        bool hid = false;
        for (int i = 0; i < 60 * 6 && !hid; i++) { g.Hero.Energy = 100; g.Idle(1); hid = g.Boss.Hidden; }
        Assert.True(hid);
        double hp = g.Boss.Hp;
        g.Hero.X = g.Boss.X - g.Way * 60;
        g.Run(2, Kick);
        g.Idle(10);
        Assert.Equal(hp, g.Boss.Hp);
        for (int i = 0; i < 60 && g.Boss.Hidden; i++) g.Idle(1);
        Assert.False(g.Boss.Hidden);
    }

    // ───────────────────────────── time, lives, laps ─────────────────────────────

    [Fact]
    public void Out_of_time_or_energy_costs_a_life_and_the_floor_again_and_the_last_ends_the_game()
    {
        var g = Game();
        g.SetTime(5);
        g.Idle(30);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("TIME UP", g.Cause);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(1, g.Floor);
        Assert.True(g.TimeLeft > GameEngine.TimeStart - 100);
        g.SetLives(1);
        g.Idle((int)(60 * GameEngine.StartSeconds) + 5);
        g.SetEnergy(0);
        g.Idle(2);
        Assert.Equal("OUT OF ENERGY", g.Cause);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 1)));
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Each_floor_has_its_own_keeper_and_after_the_fifth_comes_the_rescue_and_a_harder_lap()
    {
        var kinds = new List<BossKind>();
        for (int f = 1; f <= Field.Floors; f++) kinds.Add(Game(f).Boss.Kind);
        Assert.Equal([BossKind.Staff, BossKind.Thrower, BossKind.Giant, BossKind.Sorcerer, BossKind.Master], kinds);
        var g = Game(5);
        double hp = g.Boss.MaxHp;
        g.ToBoss(60);
        for (int i = 0; i < 60 * 40 && !g.Boss.Beaten; i++)
        {
            g.Hero.Energy = 100;
            g.Hero.X = g.Boss.X - g.Way * 70;
            g.Hero.Facing = g.Way;
            g.Tick(i % 2 == 0 ? Kick : Nothing);
        }
        Assert.True(g.Boss.Beaten);
        for (int i = 0; i < 60 * 20 && g.Phase != Phase.Rescue; i++) g.Idle(1);
        Assert.Equal(Phase.Rescue, g.Phase);
        g.Idle((int)(60 * GameEngine.RescueSeconds) + 5);
        Assert.Equal(2, g.Lap);
        Assert.Equal(1, g.Floor);
        Assert.Equal(6, g.Stage);
        Assert.True(g.Boss.MaxHp > 60);
        _ = hp;
    }

    [Fact]
    public void The_floor_bonus_pays_for_time_and_energy_left()
    {
        var g = Game(1);
        g.ToBoss(60);
        for (int i = 0; i < 60 * 30 && !g.Boss.Beaten; i++)
        {
            g.Hero.Energy = 100;
            g.Hero.X = g.Boss.X - g.Way * 70;
            g.Hero.Facing = g.Way;
            g.Tick(i % 2 == 0 ? Kick : Nothing);
        }
        int before = g.Score;
        for (int i = 0; i < 60 * 5 && g.Phase != Phase.FloorClear; i++) g.Idle(1);
        Assert.True(g.Score - before >= 100 * 20);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_fighter_climbs_the_first_floor()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 200 && g.Floor == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.Floor);
        Assert.True(g.Score > 2000);
    }

    [Fact]
    public void Left_alone_the_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Hero.X:0.000} {g.Hero.Energy:0.000} {g.Floor} {g.Lives} {g.Enemies.Count}";
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        g.SetEnergy(0);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "MEI") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("MEI", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
