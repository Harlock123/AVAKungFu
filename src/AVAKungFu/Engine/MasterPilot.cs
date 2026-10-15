namespace AVAKungFu.Engine;

/// <summary>
/// The computer at the controls, for the demonstration (and the tests). It wriggles free when held,
/// ducks high knives and jumps low ones, crouch-kicks imps and snakes, kicks whatever closes in from
/// either side, reads a keeper's wind-up to dodge it and hits back in the gap, and otherwise walks on to
/// the stairs.
/// </summary>
public sealed class MasterPilot
{
    public string Doing { get; private set; } = "";

    private int _tick;
    private bool _pressed;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var h = g.Hero;
        if (g.Phase != Phase.Play) return default;
        // Held: wriggle.
        if (h.Act == Act.Held) { Doing = "wriggle"; return new Controls((_tick / 3) % 2 == 0 ? 1 : -1); }
        if (h.Act == Act.Hurt) return default;

        // Things flying this way.
        foreach (var m in g.Missiles)
        {
            double d = m.X - h.X;
            if (Math.Sign(d) == Math.Sign(m.Vx) || Math.Abs(d) > 150) continue;
            Doing = "dodge";
            return m.Level == Level.High ? new Controls(0, 1) : new Controls(0, -1);
        }

        var b = g.Boss;
        if (g.BossFight && !b.Beaten)
        {
            double dist = Math.Abs(b.X - h.X);
            if (b.Move == "windup" && dist < b.Reach + 40 && b.Kind != BossKind.Sorcerer)
            {
                Doing = "read";
                // Duck a high blow, hop a low one, as it lands.
                if (b.Aim == Level.High) return new Controls(0, 1);
                return b.MoveTime > g.WindUp - 0.25 ? new Controls(0, -1) : default;
            }
            if (b.Hidden) { Doing = "wait"; return default; }
            if (dist < 80) return Attack(new Controls(0, 0), kick: true, "hit back");
            Doing = "close in";
            return new Controls(g.Way);
        }

        // The nearest threat on either side.
        Enemy? near = null;
        double nd = double.MaxValue;
        foreach (var e in g.Enemies)
        {
            if (e.Dead || e.Kind == EnemyKind.Jar) continue;
            double d = Math.Abs(e.X - h.X);
            if (d < nd) { nd = d; near = e; }
        }
        if (near != null)
        {
            int side = Math.Sign(near.X - h.X) == 0 ? h.Facing : Math.Sign(near.X - h.X);
            bool low = near.Kind == EnemyKind.Snake || (near.Kind == EnemyKind.Imp && !near.Leaping);
            double reach = low ? 80 : near.Kind == EnemyKind.Imp ? 60 : 78;
            if (nd < reach + 10)
            {
                // Turn to face it first.
                if (side != h.Facing) { Doing = "turn"; _pressed = false; return new Controls(side); }
                if (near.Kind == EnemyKind.Imp && near.Leaping) return Attack(default, kick: false, "punch the imp");
                return Attack(low ? new Controls(0, 1) : default, kick: true, low ? "low kick" : "kick");
            }
            // Wait for a blade man's knife rather than walk into it: step in when he's close.
            if (near.Kind == EnemyKind.BladeMan && side == g.Way && nd < 300) { Doing = "close on knife"; return new Controls(g.Way); }
        }
        // A jar coming down just ahead: wait for it.
        if (g.Enemies.Any(e => e.Kind == EnemyKind.Jar && !e.Dead && Math.Abs(e.X - h.X) < 60 && e.Y > 0)) { Doing = "wait for the jar"; return new Controls(-g.Way); }
        Doing = "walk";
        return new Controls(g.Way);
    }

    /// <summary>A press of an attack button, let go on the next step so the press after counts again.</summary>
    private Controls Attack(Controls stance, bool kick, string doing)
    {
        Doing = doing;
        _pressed = !_pressed;
        return _pressed ? stance with { Kick = kick, Punch = !kick } : stance;
    }
}
