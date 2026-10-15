using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAKungFu.Engine;
using Effect = AVAKungFu.Engine.Effect;

namespace AVAKungFu.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAKungFu/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Skin = 0xFFF0C090;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private double _cam;
    private double SX(double worldX) => worldX - _cam;
    private static double SY(double height) => Field.FloorY - height;

    // ───────────────────────────── poses ─────────────────────────────

    /// <summary>
    /// A body's angles, each measured from straight down, positive forward: the thighs and shins, the upper
    /// arms and forearms, and how far the trunk leans. Lift raises the hips off the floor (in the air).
    /// </summary>
    private record struct Pose(double FThigh, double FShin, double BThigh, double BShin, double FUp, double FFore, double BUp, double BFore, double Lean, double Lift = 0);

    private static readonly Pose Guard = new(0.2, 0.02, -0.16, -0.05, 0.5, 2.4, 0.2, 2.0, 0.05);
    private static readonly Pose CrouchPose = new(1.25, -0.25, 0.9, -0.6, 0.8, 2.2, 0.4, 1.9, 0.25);

    private static double Ease(double t, double over) => Math.Clamp(t / over, 0, 1);

    private static Pose PoseFor(Act act, double t, double walk) => act switch
    {
        Act.Walk => WalkPose(walk),
        Act.Crouch => CrouchPose,
        Act.Punch => Guard with { FThigh = 0.35, BThigh = -0.3, FUp = 0.5 + 1.07 * Ease(t, 0.06), FFore = 2.4 - 0.83 * Ease(t, 0.06), Lean = 0.15 },
        Act.Kick => new Pose(0.3 + 1.35 * Ease(t, 0.09), 0.3 + 1.3 * Ease(t, 0.09), -0.12, 0, 0.9, 1.8, -0.6, -0.3, -0.35),
        Act.CrouchPunch => CrouchPose with { FUp = 0.8 + 0.65 * Ease(t, 0.05), FFore = 2.2 - 0.75 * Ease(t, 0.05) },
        Act.CrouchKick => new Pose(1.0 + 0.55 * Ease(t, 0.09), 0.3 + 1.25 * Ease(t, 0.09), 1.3, -0.5, 0.9, 2.2, 0.3, 1.6, 0.35),
        Act.Jump => new Pose(1.6, 0.2, 1.0, -0.4, 2.2, 2.6, 1.8, 2.4, 0.05),
        Act.JumpKick => new Pose(1.48, 1.45, 1.2, -0.3, 0.6, 1.3, -0.9, -0.4, -0.2),
        Act.Hurt => Guard with { Lean = -0.45, FUp = 2.4, FFore = 2.9, BUp = 2.0, BFore = 2.6 },
        Act.Held => Guard with { FUp = 2.4 + Math.Sin(t * 30) * 0.4, FFore = 2.9, BUp = 2.1 - Math.Sin(t * 30) * 0.4, BFore = 2.7, Lean = Math.Sin(t * 30) * 0.12 },
        Act.Down => new Pose(1.5, 1.5, 1.4, 1.5, 2.6, 2.8, 2.4, 2.6, -1.45, 10),
        _ => Guard,
    };

    private static Pose WalkPose(double walk)
    {
        double p = walk * 0.045, s = Math.Sin(p), c = Math.Cos(p);
        return Guard with
        {
            FThigh = 0.45 * s, FShin = 0.45 * s - 0.35 * (1 + c) / 2,
            BThigh = -0.45 * s, BShin = -0.45 * s - 0.35 * (1 - c) / 2,
            FUp = 0.5 - 0.15 * s, BUp = 0.2 + 0.15 * s,
        };
    }

    /// <summary>How a fighter looks: clothes, skin and hair, and what it carries.</summary>
    private record struct Look(uint Top, uint Legs, uint Skin, uint Hair, uint Belt, uint Band = 0, string Gear = "", double HeadScale = 1);

    private static readonly Look HeroLook = new(0xFFF6F6F0, 0xFFF0F0E8, Skin, 0xFF201810, 0xFF141414, Band: 0xFFE02020);

    /// <summary>A fighter, feet at (x, floor − height), facing ±1.</summary>
    private void DrawFighter(DrawingContext dc, double x, double height, int facing, double s, Pose p, Look look, double alpha = 1)
    {
        const double thigh = 38, shin = 36, trunk = 55, upper = 28, fore = 26;
        double f = facing;
        Point Seg(Point from, double angle, double len) => new(from.X + Math.Sin(angle) * len * f * s, from.Y + Math.Cos(angle) * len * s);
        // Stand on whichever leg reaches further down, unless in the air.
        double reach = Math.Max(Math.Cos(p.FThigh) * thigh + Math.Cos(p.FShin) * shin, Math.Cos(p.BThigh) * thigh + Math.Cos(p.BShin) * shin);
        double hipUp = p.Lift > 0 ? p.Lift : height > 0 ? Math.Max(reach, 40) : reach;
        var hip = new Point(x, SY(height) - hipUp * s);
        var shoulder = new Point(hip.X + Math.Sin(p.Lean) * trunk * f * s, hip.Y - Math.Cos(p.Lean) * trunk * s);
        var head = new Point(shoulder.X + Math.Sin(p.Lean) * 18 * f * s, shoulder.Y - Math.Cos(p.Lean) * 18 * s);
        uint Fade(uint c) => alpha >= 1 ? c : (uint)(((c >> 24) * alpha)) << 24 | (c & 0x00FFFFFF);

        void Limb(Point a, Point b, Point c2, uint colour, uint end, double w)
        {
            dc.DrawLine(Pen(Fade(colour), w * s), a, b);
            dc.DrawLine(Pen(Fade(colour), w * s), b, c2);
            dc.DrawEllipse(Brush(Fade(end)), null, c2, w * 0.55 * s, w * 0.5 * s);
        }
        // The far arm and leg first, a shade darker.
        var bKnee = Seg(hip, p.BThigh, thigh);
        Limb(hip, bKnee, Seg(bKnee, p.BShin, shin), Darker(look.Legs), 0xFF2A1A10, 13);
        var bElbow = Seg(shoulder, p.BUp, upper);
        Limb(shoulder, bElbow, Seg(bElbow, p.BFore, fore), Darker(look.Top), Darker(look.Skin), 10);
        // The trunk, the belt, the near leg.
        dc.DrawLine(Pen(Fade(look.Top), 24 * s), hip, shoulder);
        // A belt: a band across the trunk just above the hips.
        double ux = Math.Sin(p.Lean) * f, uy = -Math.Cos(p.Lean), nx = Math.Cos(p.Lean), ny = Math.Sin(p.Lean) * f;
        Point B(double along, double across) => new(hip.X + (ux * along + nx * across) * s, hip.Y + (uy * along + ny * across) * s);
        Poly(dc, Fade(look.Belt), null, 0, B(5, -13), B(5, 13), B(11, 13), B(11, -13));
        var fKnee = Seg(hip, p.FThigh, thigh);
        Limb(hip, fKnee, Seg(fKnee, p.FShin, shin), look.Legs, 0xFF2A1A10, 13);
        // The head.
        double hr = 13 * s * look.HeadScale;
        dc.DrawEllipse(Brush(Fade(look.Skin)), Pen(Fade(0xFF6A4020), 1), head, hr, hr * 1.08);
        dc.DrawEllipse(Brush(Fade(look.Hair)), null, new Point(head.X - f * 3 * s, head.Y - hr * 0.45), hr * 0.92, hr * 0.62);
        dc.DrawEllipse(Brush(Fade(0xFF101010)), null, new Point(head.X + f * hr * 0.5, head.Y - hr * 0.05), 1.8 * s, 2.2 * s);
        if (look.Band != 0)
        {
            dc.DrawLine(Pen(Fade(look.Band), 4 * s), new Point(head.X - hr, head.Y - hr * 0.35), new Point(head.X + hr, head.Y - hr * 0.35));
            dc.DrawLine(Pen(Fade(look.Band), 3 * s), new Point(head.X - f * hr, head.Y - hr * 0.35), new Point(head.X - f * hr * 2.1, head.Y + hr * 0.2));
        }
        if (look.Gear == "beard")
            Poly(dc, Fade(0xFFF0F0F0), null, 0, new Point(head.X - hr * 0.3 * f, head.Y + hr * 0.5), new Point(head.X + hr * 0.9 * f, head.Y + hr * 0.4), new Point(head.X + hr * 0.4 * f, head.Y + hr * 2.4));
        if (look.Gear == "hood")
            Poly(dc, Fade(look.Top), null, 0, new Point(head.X - hr * 1.3, head.Y + hr * 0.4), new Point(head.X + hr * 1.3, head.Y + hr * 0.4), new Point(head.X - f * hr * 0.8, head.Y - hr * 2.2));
        // The near arm last, over everything.
        var fElbow = Seg(shoulder, p.FUp, upper);
        var hand = Seg(fElbow, p.FFore, fore);
        Limb(shoulder, fElbow, hand, look.Top, look.Skin, 10);
        switch (look.Gear)
        {
            case "staff":
            {
                // Held across the hand, longer forward than back.
                double a = p.FFore;
                var tip = new Point(hand.X + Math.Sin(a) * 80 * f * s, hand.Y + Math.Cos(a) * 80 * s);
                var butt = new Point(hand.X - Math.Sin(a) * 40 * f * s, hand.Y - Math.Cos(a) * 40 * s);
                dc.DrawLine(Pen(Fade(0xFF8A5A2A), 6 * s), butt, tip);
                break;
            }
            case "knife":
                dc.DrawLine(Pen(Fade(0xFFD8D8E0), 3 * s), hand, new Point(hand.X + f * 16 * s, hand.Y - 6 * s));
                break;
            case "glow":
                dc.DrawEllipse(Brush(Fade(0x90B060FF)), null, hand, 12 * s, 12 * s);
                break;
        }
    }

    private static uint Darker(uint c)
    {
        uint r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
        return (c & 0xFF000000) | (r * 3 / 4) << 16 | (g * 3 / 4) << 8 | (b * 3 / 4);
    }

    // ───────────────────────────── the screen ─────────────────────────────

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        _cam = g.Camera;
        DrawTemple(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused || g.State == GameState.GameOver;
        if (inGame)
        {
            DrawBoss(dc);
            foreach (var e in g.Enemies.Where(e => !e.Holding)) DrawEnemy(dc, e);
            DrawHero(dc);
            if (g.Phase == Phase.Rescue)
            {
                // Mei, free at the top of the stairs.
                double mx = SX(g.Stairs) - g.Way * 40;
                DrawFighter(dc, mx, 0, -g.Way, 0.92, Guard with { FUp = 2.6, FFore = 2.9, BUp = 0.3, BFore = 0.4, FThigh = 0.08, BThigh = -0.08 },
                            new Look(0xFFFF7AB0, 0xFFE05A90, Skin, 0xFF101010, 0xFFFFE14A, Band: 0xFFFFE14A));
                double beat = 1 + 0.15 * Math.Sin(_clock * 8);
                var heart = new Point((mx + SX(g.Hero.X)) / 2, SY(200));
                dc.DrawEllipse(Brush(0xFFFF4A6A), null, new Point(heart.X - 7 * beat, heart.Y), 8 * beat, 8 * beat);
                dc.DrawEllipse(Brush(0xFFFF4A6A), null, new Point(heart.X + 7 * beat, heart.Y), 8 * beat, 8 * beat);
                Poly(dc, 0xFFFF4A6A, null, 0, new Point(heart.X - 15 * beat, heart.Y + 2), new Point(heart.X + 15 * beat, heart.Y + 2), new Point(heart.X, heart.Y + 20 * beat));
            }
            foreach (var e in g.Enemies.Where(e => e.Holding)) DrawEnemy(dc, e);
            foreach (var m in g.Missiles) DrawMissile(dc, m);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 22), 26, Yellow, center: true);
    }

    /// <summary>Each floor's walls and boards.</summary>
    private static readonly (uint Wall, uint Panel, uint Floor, uint Plank)[] Floors =
    [
        (0xFF5A2E2A, 0xFF7A3E34, 0xFF8A5A30, 0xFF6A4220),
        (0xFF2A3E5A, 0xFF34507A, 0xFF7A6A40, 0xFF5A4A2A),
        (0xFF2E4A2E, 0xFF3E6A3A, 0xFF8A6A3A, 0xFF6A4A26),
        (0xFF4A2E5A, 0xFF64407A, 0xFF7A5A3A, 0xFF5A3E26),
        (0xFF5A4A1E, 0xFF7A6428, 0xFF8A6A3A, 0xFF6A4A26),
    ];

    private void DrawTemple(DrawingContext dc)
    {
        var g = Engine;
        var (wall, panel, floor, plank) = Floors[(g.Floor - 1) % Floors.Length];
        dc.FillRectangle(Brush(0xFF120A08), new Rect(0, 0, Field.Width, Field.Height));
        dc.FillRectangle(Brush(wall), new Rect(0, 150, Field.Width, Field.FloorY - 150));
        // Panels and lanterns between pillars, every 300 along the floor.
        double first = Math.Floor(_cam / 300) * 300;
        for (double wx = first - 300; wx < _cam + Field.Width + 300; wx += 300)
        {
            double x = SX(wx);
            dc.FillRectangle(Brush(panel), new Rect(x + 60, 230, 180, 250));
            dc.DrawRectangle(null, Pen(0xFFB08A3A, 3), new Rect(x + 60, 230, 180, 250));
            for (int k = 1; k < 4; k++) dc.DrawLine(Pen(0x40000000, 2), new Point(x + 60 + k * 45, 232), new Point(x + 60 + k * 45, 478));
            // A lantern on a cord.
            dc.DrawLine(Pen(0xFF3A2A1A, 2), new Point(x + 150, 150), new Point(x + 150, 190));
            dc.DrawEllipse(Brush(0xFFE04A2A), Pen(0xFF6A1A0A, 1.5), new Point(x + 150, 210), 16, 22);
            dc.DrawEllipse(Brush(0x60FFE080), null, new Point(x + 150, 210), 26, 30);
            dc.FillRectangle(Brush(0xFF2A1A0A), new Rect(x + 140, 186, 20, 5));
            // The pillar.
            dc.FillRectangle(Brush(0xFFB02A20), new Rect(x - 18, 150, 36, Field.FloorY - 150));
            dc.FillRectangle(Brush(0xFF7A1A14), new Rect(x + 8, 150, 10, Field.FloorY - 150));
            dc.FillRectangle(Brush(0xFFE0B040), new Rect(x - 22, 170, 44, 10));
            dc.FillRectangle(Brush(0xFFE0B040), new Rect(x - 22, Field.FloorY - 30, 44, 10));
        }
        // The beams overhead and the boards underfoot.
        dc.FillRectangle(Brush(0xFF3A2216), new Rect(0, 120, Field.Width, 34));
        dc.FillRectangle(Brush(0xFFE0B040), new Rect(0, 150, Field.Width, 4));
        dc.FillRectangle(Brush(floor), new Rect(0, Field.FloorY, Field.Width, Field.Height - Field.FloorY));
        dc.FillRectangle(Brush(0xFFE0B040), new Rect(0, Field.FloorY, Field.Width, 4));
        for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(plank), new Rect(0, Field.FloorY + 26 + k * 28, Field.Width, 3));
        for (double wx = Math.Floor(_cam / 120) * 120; wx < _cam + Field.Width + 120; wx += 120)
            dc.FillRectangle(Brush(plank), new Rect(SX(wx), Field.FloorY + 4, 3, Field.Height - Field.FloorY));
        // The stairs up at the far end.
        double sx = SX(g.Stairs);
        const double w = 26;
        for (int k = 0; k < 7; k++)
        {
            var step = new Rect(g.Way < 0 ? sx - 70 - k * w : sx + 44 + k * w, Field.FloorY - (k + 1) * 26, w + 2, (k + 1) * 26);
            dc.FillRectangle(Brush(0xFF6A4220), step);
            dc.FillRectangle(Brush(0xFFE0B040), new Rect(step.X, step.Y, step.Width, 3));
        }
    }

    private void DrawHero(DrawingContext dc)
    {
        var g = Engine;
        var h = g.Hero;
        bool blink = h.Guard > 0 && (int)(_clock * 20) % 2 == 0 && h.Act != Act.Down;
        if (blink) return;
        var pose = PoseFor(h.Act, h.ActTime, h.Walk);
        DrawFighter(dc, SX(h.X), h.Y, h.Facing, 1, pose, HeroLook);
    }

    private void DrawEnemy(DrawingContext dc, Enemy e)
    {
        double x = SX(e.X);
        if (x < -100 || x > Field.Width + 100) return;
        double fade = e.Dead ? Math.Clamp(e.Falling / 0.6, 0, 1) : 1;
        switch (e.Kind)
        {
            case EnemyKind.Clinger:
            {
                var pose = e.Dead ? PoseFor(Act.Hurt, 0, 0) : e.Holding ? Guard with { FUp = 1.5, FFore = 1.4, BUp = 1.3, BFore = 1.2, Lean = 0.2 } : WalkPose(e.Age * 105) with { FUp = 1.2, FFore = 1.3 };
                DrawFighter(dc, x, e.Y, e.Facing, 0.95, pose, new Look(0xFF7A3AA0, 0xFF4A2068, Skin, 0xFF201810, 0xFF201010), fade);
                break;
            }
            case EnemyKind.BladeMan:
            {
                bool throwing = !e.Dead && e.Timer < 0.3;
                var pose = e.Dead ? PoseFor(Act.Hurt, 0, 0) : throwing ? Guard with { FUp = 2.6, FFore = 2.9, Lean = -0.1 } : Guard with { FUp = 1.0, FFore = 1.9 };
                DrawFighter(dc, x, e.Y, e.Facing, 1, pose, new Look(0xFF8A5A2A, 0xFF3A2A1A, Skin, 0xFF100808, 0xFFC08030, Gear: "knife"), fade);
                break;
            }
            case EnemyKind.Imp:
            {
                var pose = e.Leaping || e.Holding ? new Pose(1.3, 0.4, 1.0, -0.2, 2.2, 2.5, 1.9, 2.4, 0.1) : WalkPose(e.Age * 240);
                double y = e.Holding ? e.Y + 95 : e.Y;
                if (e.Holding) y = 112;
                DrawFighter(dc, x, y, e.Facing, 0.58, pose, new Look(0xFF3AA04A, 0xFF2A6A2A, Skin, 0xFF3A2010, 0xFF101010, HeadScale: 1.35), fade);
                break;
            }
            case EnemyKind.Jar:
            {
                var p = new Point(x, SY(e.Y + 22));
                Poly(dc, 0xFFB0703A, 0xFF5A3010, 2, new Point(p.X - 10, p.Y - 26), new Point(p.X + 10, p.Y - 26), new Point(p.X + 8, p.Y - 18),
                     new Point(p.X + 20, p.Y), new Point(p.X + 14, p.Y + 22), new Point(p.X - 14, p.Y + 22), new Point(p.X - 20, p.Y), new Point(p.X - 8, p.Y - 18));
                dc.DrawLine(Pen(0xFF3A6AB0, 3), new Point(p.X - 18, p.Y + 2), new Point(p.X + 18, p.Y + 2));
                break;
            }
            case EnemyKind.Snake:
            {
                var pts = Enumerable.Range(0, 9).Select(i => new Point(x - e.Facing * i * 6, SY(6 + Math.Sin(e.Age * 14 + i) * 4))).ToArray();
                for (int i = 0; i + 1 < pts.Length; i++) dc.DrawLine(Pen(0xFF3AB04A, 9 - i * 0.6), pts[i], pts[i + 1]);
                dc.DrawEllipse(Brush(0xFF2A8A3A), null, new Point(x + e.Facing * 4, SY(8)), 8, 6);
                dc.DrawLine(Pen(Red, 1.5), new Point(x + e.Facing * 10, SY(8)), new Point(x + e.Facing * 17, SY(6 + Math.Sin(_clock * 30) * 3)));
                break;
            }
        }
    }

    private void DrawBoss(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Boss;
        double x = SX(b.X);
        if (x < -200 || x > Field.Width + 200) return;
        if (b.Hidden)
        {
            // A shimmer where the sorcerer has gone.
            dc.DrawEllipse(Brush((uint)(60 + 40 * Math.Sin(_clock * 20)) << 24 | 0x00B060FF), null, new Point(x, SY(90)), 30, 90);
            return;
        }
        Pose pose = b.Beaten ? PoseFor(Act.Down, 0, 0)
            : b.Stagger > 0 ? PoseFor(Act.Hurt, 0, 0)
            : b.Move == "windup" ? (b.Aim == Level.Low ? CrouchPose with { FUp = -0.4, FFore = 0.4, Lean = 0.1 } : Guard with { FUp = -0.6, FFore = 2.6, Lean = -0.2 })
            : b.Move == "strike" ? (b.Aim == Level.Low ? new Pose(1.0, 1.5, 1.3, -0.5, 1.5, 1.5, 0.3, 1.6, 0.35) : Guard with { FUp = 1.57, FFore = 1.57, Lean = 0.2, FThigh = 0.4, BThigh = -0.3 })
            : Math.Abs(b.MoveTime) < 0.01 ? Guard : WalkPose(b.Age * 70);
        var look = b.Kind switch
        {
            BossKind.Staff => new Look(0xFF2A5AB0, 0xFF1A3A70, Skin, 0xFF101010, 0xFFE0B040, Gear: "staff"),
            BossKind.Thrower => new Look(0xFFE07A2A, 0xFF6A3A1A, Skin, 0xFF201008, 0xFF101010, Band: 0xFF2A2A2A),
            BossKind.Giant => new Look(0xFFB02A2A, 0xFF5A1A1A, 0xFFE0A070, 0xFF101010, 0xFF101010, Gear: "beard"),
            BossKind.Sorcerer => new Look(0xFF3A1A5A, 0xFF2A1040, 0xFFC0B0D0, 0xFF101010, 0xFFB060FF, Gear: "glow"),
            _ => new Look(0xFFE0D0A0, 0xFFB09A60, Skin, 0xFFF0F0F0, 0xFF3AA04A, Gear: "beard"),
        };
        if (b.Kind == BossKind.Sorcerer) look = look with { Gear = "hood" };
        DrawFighter(dc, x, 0, b.Facing, b.Size, pose, look);
        if (b.Kind == BossKind.Sorcerer && !b.Beaten)
            dc.DrawEllipse(Brush(0x70B060FF), null, new Point(x + b.Facing * 30, SY(95)), 12, 12);
        // Wind-up warning: where the blow will come.
        if (b.Move == "windup" && !b.Beaten && b.Kind != BossKind.Sorcerer)
        {
            double y = b.Aim == Level.High ? 130 : 30;
            dc.DrawEllipse(Brush((uint)(120 + 100 * Math.Sin(_clock * 30)) << 24 | 0x00FFE14A), null, new Point(x + b.Facing * 40 * b.Size, SY(y * b.Size)), 6, 6);
        }
    }

    private void DrawMissile(DrawingContext dc, Missile m)
    {
        double x = SX(m.X), y = SY(m.Level == Level.High ? 130 : 26);
        int dir = Math.Sign(m.Vx);
        if (m.Fire)
        {
            for (int i = 1; i <= 4; i++) dc.DrawEllipse(Brush((uint)(160 - i * 34) << 24 | 0x00FF8030), null, new Point(x - dir * i * 9, y), 10 - i * 1.5, 8 - i);
            dc.DrawEllipse(Brush(0xFFFFE070), Pen(0xFFFF6020, 2), new Point(x, y), 9, 8);
        }
        else if (m.Returns)
        {
            double a = m.Age * 18;
            var c = new Point(x, y);
            Point P(double ang, double r) => new(c.X + Math.Cos(ang) * r, c.Y + Math.Sin(ang) * r);
            dc.DrawLine(Pen(0xFFE0B040, 5), c, P(a, 16));
            dc.DrawLine(Pen(0xFFE0B040, 5), c, P(a + 1.6, 16));
        }
        else
        {
            dc.DrawLine(Pen(0xFFE0E0F0, 3), new Point(x - dir * 4, y), new Point(x + dir * 16, y));
            dc.DrawLine(Pen(0xFF6A3A1A, 5), new Point(x - dir * 14, y), new Point(x - dir * 4, y));
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var p = new Point(SX(e.X), SY(e.Y));
        switch (e.Kind)
        {
            case EffectKind.Spark:
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4, r0 = 4 + 18 * t, r1 = r0 + 10 * (1 - t);
                    dc.DrawLine(Pen((uint)(255 * (1 - t)) << 24 | 0x00FFF0A0, 3), new Point(p.X + Math.Cos(a) * r0, p.Y + Math.Sin(a) * r0), new Point(p.X + Math.Cos(a) * r1, p.Y + Math.Sin(a) * r1));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(p.X, p.Y - t * 30), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(p.X, p.Y - t * 20), 80, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0A0606), new Rect(0, 0, Field.Width, Field.Top));
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, White);
        Text(dc, "HI", new Point(170, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(170, 44), 28, Dim);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (!inGame) return;
        Text(dc, "TIME", new Point(330, 16), 22, Dim);
        bool low = g.TimeLeft < 400;
        if (!low || (int)(_clock * 4) % 2 == 0) Text(dc, ((int)g.TimeLeft).ToString("D4"), new Point(330, 44), 34, low ? Red : Yellow);
        // The floors, the current one lit, the stairs climbing.
        for (int i = 0; i < Field.Floors; i++)
        {
            bool now = i + 1 == g.Floor, done = i + 1 < g.Floor;
            var r = new Rect(470 + i * 52, 16, 46, 34);
            dc.DrawRectangle(Brush(now ? 0xFFB02A20 : done ? 0xFF4A2A20 : 0xFF241818), Pen(now ? Yellow : 0xFF5A3A2A, 2), r, 4, 4);
            Text(dc, $"{i + 1}F", r.Center, 26, now ? White : 0xFF8A6A5A, center: true, shadow: false);
        }
        if (g.Lap > 1) Text(dc, $"LAP {g.Lap}", new Point(740, 33), 24, Yellow);
        Text(dc, "LIVES", new Point(830, 16), 22, Dim);
        for (int i = 0; i < Math.Min(5, g.Lives); i++)
            dc.DrawEllipse(Brush(Skin), Pen(0xFFE02020, 2), new Point(842 + i * 26, 44), 9, 9);
        // The energy bars: the hero's, and the keeper's once it is met.
        dc.FillRectangle(Brush(0xC00A0606), new Rect(0, Field.Top, Field.Width, 42));
        Text(dc, "PLAYER", new Point(16, Field.Top + 21), 22, Yellow);
        Bar(dc, 96, g.Hero.Energy / 100, 0xFFFFE14A);
        Text(dc, "ENEMY", new Point(560, Field.Top + 21), 22, 0xFFFF7A6A);
        bool met = g.BossFight || g.Boss.Beaten || g.Boss.Hp < g.Boss.MaxHp;
        Bar(dc, 636, met ? Math.Max(0, g.Boss.Hp) / g.Boss.MaxHp : 1, met ? 0xFFFF4A3A : 0xFF5A2A2A);
    }

    private static void Bar(DrawingContext dc, double x, double f, uint colour)
    {
        var r = new Rect(x, Field.Top + 12, 360, 18);
        dc.DrawRectangle(Brush(0xFF241818), Pen(0xFF6A4A3A, 1.5), r);
        dc.FillRectangle(Brush(colour), new Rect(x + 2, r.Y + 2, 356 * Math.Clamp(f, 0, 1), 14));
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xB0000000), new Rect(cx - 360, 200, 720, g.SubMessage != null ? 110 : 70));
        Text(dc, msg, new Point(cx, 232), 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, 282), 30, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0120A08), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA KUNG-FU", new Point(cx, 140), 120, 0xFFFFE14A, center: true);
        Text(dc, "AFTER THE 1984 ARCADE CLASSIC", new Point(cx, 208), 28, Dim, center: true);
        string[] lines =
        [
            "FIGHT UP FIVE FLOORS OF THE TEMPLE TO FREE MEI",
            "SHAKE OFF THE CLINGERS - DUCK HIGH KNIVES, JUMP LOW ONES",
            "CROUCH-KICK THE IMPS AND SNAKES",
            "BEAT THE KEEPER OF EACH FLOOR'S STAIRS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 270 + i * 34), 27, White, center: true);
        // The hero, kicking.
        double t = _clock % 1.2;
        var pose = t < 0.4 ? PoseFor(Act.Kick, t, 0) : t < 0.8 ? PoseFor(Act.Punch, t - 0.4, 0) : Guard;
        _cam = 0;
        DrawFighter(dc, cx - 120, 50, 1, 0.9, pose, HeroLook);
        DrawFighter(dc, cx + 70, 50, -1, 0.85, t < 0.4 ? PoseFor(Act.Hurt, 0, 0) : Guard with { FUp = 1.4, FFore = 1.4 }, new Look(0xFF7A3AA0, 0xFF4A2068, Skin, 0xFF201810, 0xFF201010));
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 640), 46, Yellow, center: true);
        Text(dc, "ARROWS: WALK, UP JUMPS, DOWN CROUCHES    Z: PUNCH    X: KICK", new Point(cx, 700), 25, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8120A08), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP FIGHTERS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   FLOOR", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8120A08), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
