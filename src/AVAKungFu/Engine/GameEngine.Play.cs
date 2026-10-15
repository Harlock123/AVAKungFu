namespace AVAKungFu.Engine;

public sealed partial class GameEngine
{
    public const double WalkSpeed = 220, JumpSpeed = 620, Gravity = 1700, AirSpeed = 170;
    public const double StartSeconds = 2.4, DyingSeconds = 2.6, ClearSeconds = 3.4, RescueSeconds = 5;
    public const double TimeStart = 2000, TimePerSecond = 20;
    public const double HoldDrain = 7, KnifeDamage = 12, SnakeDamage = 8, JarDamage = 10;
    public const int ShakesToFree = 8;
    public const int FirstExtraLife = 30000, ExtraLifeEvery = 50000;

    /// <summary>How long each move lasts, and when in it the blow lands.</summary>
    public static (double Length, double From, double To) Timing(Act a) => a switch
    {
        Act.Punch or Act.CrouchPunch => (0.22, 0.05, 0.14),
        Act.Kick or Act.CrouchKick => (0.30, 0.09, 0.20),
        Act.JumpKick => (9, 0.06, 0.45),
        _ => (0, 0, 0),
    };

    /// <summary>How far a blow reaches, at what height, and what it does to a keeper.</summary>
    public static (double Reach, Level Level, double Damage) Blow(Act a) => a switch
    {
        Act.Punch => (64, Level.High, 6),
        Act.Kick => (84, Level.Mid, 8),
        Act.CrouchPunch => (60, Level.Low, 6),
        Act.CrouchKick => (88, Level.Low, 8),
        Act.JumpKick => (78, Level.High, 11),
        _ => (0, Level.Mid, 0),
    };

    public int Floor { get; private set; } = 1;
    /// <summary>Times round the temple: each lap is harder.</summary>
    public int Lap { get; private set; } = 1;
    public int Stage => (Lap - 1) * Field.Floors + Floor;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public double TimeLeft { get; private set; } = TimeStart;
    public Hero Hero { get; } = new();
    public List<Enemy> Enemies { get; } = [];
    public List<Missile> Missiles { get; } = [];
    public Boss Boss { get; private set; } = new();
    public MasterPilot Pilot { get; } = new();
    /// <summary>What ended the last life.</summary>
    public string Cause { get; private set; } = "";
    /// <summary>For tests: no enemies come unless a test puts them there.</summary>
    internal bool Calm { get; set; }

    /// <summary>Which way this floor goes: odd floors to the left, even to the right.</summary>
    public int Way => Floor % 2 == 1 ? -1 : 1;
    /// <summary>Where the stairs are, at the far end.</summary>
    public double Stairs => Way < 0 ? 70 : Field.Length - 70;
    /// <summary>Whether the hero has reached the keeper's arena.</summary>
    public bool BossFight => Math.Abs(Hero.X - Stairs) < Field.Arena && !Boss.Beaten;
    public double Pace => 1 + 0.12 * (Lap - 1) + 0.03 * (Floor - 1);
    public double Camera => Math.Clamp(Hero.X - Field.Width / 2, 0, Field.Length - Field.Width);

    private int _nextExtra, _lastDx;
    private bool _punchPrev, _kickPrev;
    private double _spawnIn, _jarIn;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Lap = 1;
        _nextExtra = FirstExtraLife;
        Floor = 1;
        LayOut();
        SetPhase(Phase.FloorStart);
    }

    private void BeginFloor(int floor)
    {
        Floor = floor;
        LayOut();
        SetPhase(Phase.FloorStart);
        Say($"FLOOR {floor}" + (Lap > 1 ? $" - LAP {Lap}" : ""), $"{Boss.Name} GUARDS THE STAIRS", StartSeconds);
        Play(Sfx.FloorStart);
    }

    /// <summary>The hero at the near end of the floor, its keeper by the stairs, nobody else about.</summary>
    private void LayOut()
    {
        Enemies.Clear();
        Missiles.Clear();
        Effects.Clear();
        var h = Hero;
        h.X = Way < 0 ? Field.Length - 160 : 160;
        h.Y = h.Vy = 0;
        h.Facing = Way;
        h.Act = Act.Stand;
        h.Energy = 100;
        h.Guard = 0;
        h.Shake = 0;
        TimeLeft = TimeStart;
        var kind = (BossKind)(Floor - 1);
        double hp = (kind switch { BossKind.Giant => 100, BossKind.Master => 110, BossKind.Staff => 60, _ => 55 }) * (1 + 0.25 * (Lap - 1));
        Boss = new Boss { Kind = kind, X = Stairs - Way * 170, Hp = hp, MaxHp = hp, Facing = -Way };
        _spawnIn = 1.5;
        _jarIn = 3;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var h = Hero;
        switch (Phase)
        {
            case Phase.FloorStart:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                TimeLeft = Math.Max(0, TimeLeft - TimePerSecond * dt);
                MoveHero(c, dt);
                Arrivals(dt);
                MoveEnemies(dt);
                MoveMissiles(dt);
                if (BossFight || Boss.Hp < Boss.MaxHp) MoveBoss(dt);
                if (Phase != Phase.Play) break;
                if (h.Energy <= 0) Lose("OUT OF ENERGY");
                else if (TimeLeft <= 0) Lose("TIME UP");
                break;
            case Phase.ToStairs:
                // Up to the stairs on his own.
                h.Act = Act.Walk;
                h.Facing = Way;
                h.X += Way * WalkSpeed * dt;
                h.Walk += WalkSpeed * dt;
                MoveEnemies(dt);
                if (Way < 0 ? h.X <= Stairs : h.X >= Stairs) FloorDone();
                break;
            case Phase.FloorClear:
                if (PhaseTime > ClearSeconds)
                {
                    if (Floor < Field.Floors) BeginFloor(Floor + 1);
                    else
                    {
                        SetPhase(Phase.Rescue);
                        int bonus = 10000 * Lap;
                        AddScore(bonus, h.X, 160, 0xFFFFE14A);
                        Say("MEI IS FREE!", $"THE TEMPLE IS YOURS - BONUS {bonus}", RescueSeconds);
                        Play(Sfx.Rescue);
                    }
                }
                break;
            case Phase.Rescue:
                if (PhaseTime > RescueSeconds)
                {
                    Lap++;
                    BeginFloor(1);
                }
                break;
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    BeginFloor(Floor);
                }
                break;
        }
        Enemies.RemoveAll(e => e.Dead && e.Falling <= 0);
        Missiles.RemoveAll(m => m.Dead);
    }

    // ───────────────────────────── the hero ─────────────────────────────

    private void MoveHero(Controls c, double dt)
    {
        var h = Hero;
        bool punch = c.Punch && !_punchPrev, kick = c.Kick && !_kickPrev;
        _punchPrev = c.Punch;
        _kickPrev = c.Kick;
        h.Guard = Math.Max(0, h.Guard - dt);
        h.ActTime += dt;

        // Held: wriggle left and right to break free.
        if (Enemies.Any(e => e.Holding && !e.Dead))
        {
            h.Act = Act.Held;
            if (c.Dx != 0 && c.Dx != _lastDx)
            {
                h.Shake++;
                Play(Sfx.Shake);
            }
            if (c.Dx != 0) _lastDx = c.Dx;
            if (h.Shake >= ShakesToFree)
            {
                h.Shake = 0;
                foreach (var e in Enemies.Where(e => e.Holding))
                {
                    e.Holding = false;
                    e.Stagger = 0.9;
                    e.X += Math.Sign(e.X - h.X == 0 ? 1 : e.X - h.X) * 70;
                    e.Leaping = false;
                    e.Y = 0;
                }
                h.Act = Act.Stand;
            }
            return;
        }
        _lastDx = c.Dx;
        if (h.Act == Act.Hurt)
        {
            if (h.ActTime > 0.3) h.Act = Act.Stand;
            else { h.X += -h.Facing * 120 * dt; Fall(h, dt); Bounds(); return; }
        }

        if (h.Airborne || h.Act is Act.Jump or Act.JumpKick)
        {
            if (kick && h.Act == Act.Jump) Begin(Act.JumpKick);
            h.X += c.Dx * AirSpeed * dt;
            Fall(h, dt);
            if (!h.Airborne) h.Act = Act.Stand;
        }
        else if (h.Attacking)
        {
            if (h.ActTime >= Timing(h.Act).Length) h.Act = c.Dy > 0 ? Act.Crouch : Act.Stand;
        }
        else
        {
            if (c.Dx != 0 && c.Dx != h.Facing && c.Dy == 0) h.Facing = c.Dx;
            if (kick) Begin(c.Dy > 0 ? Act.CrouchKick : Act.Kick);
            else if (punch) Begin(c.Dy > 0 ? Act.CrouchPunch : Act.Punch);
            else if (c.Dy < 0)
            {
                h.Act = Act.Jump;
                h.ActTime = 0;
                h.Vy = JumpSpeed;
                h.Y = 0.6;
                Play(Sfx.Swish);
            }
            else if (c.Dy > 0) h.Act = Act.Crouch;
            else if (c.Dx != 0)
            {
                h.Act = Act.Walk;
                h.Facing = c.Dx;
                h.X += c.Dx * WalkSpeed * dt;
                h.Walk += WalkSpeed * dt;
            }
            else h.Act = Act.Stand;
        }
        Bounds();
        Strike();
    }

    private void Begin(Act a)
    {
        var h = Hero;
        h.Act = a;
        h.ActTime = 0;
        h.Struck = false;
        Play(a is Act.Punch or Act.CrouchPunch ? Sfx.Punch : Sfx.Kick);
    }

    private static void Fall(Hero h, double dt)
    {
        if (!h.Airborne && h.Vy <= 0) return;
        h.Vy -= Gravity * dt;
        h.Y += h.Vy * dt;
        if (h.Y <= 0) { h.Y = 0; h.Vy = 0; }
    }

    /// <summary>The ends of the floor, and the keeper standing in the way.</summary>
    private void Bounds()
    {
        var h = Hero;
        h.X = Math.Clamp(h.X, 40, Field.Length - 40);
        // (Even unseen, the sorcerer bars the way.)
        if (!Boss.Beaten && Math.Abs(h.X - Stairs) < Field.Arena + 200)
        {
            if (Way < 0) h.X = Math.Max(h.X, Boss.X + 46);
            else h.X = Math.Min(h.X, Boss.X - 46);
        }
    }

    /// <summary>A blow landing during its active moment: the first thing in reach at the right height takes it.</summary>
    private void Strike()
    {
        var h = Hero;
        if (!h.Attacking || h.Struck) return;
        var (_, from, to) = Timing(h.Act);
        if (h.ActTime < from || h.ActTime > to) return;
        var (reach, level, damage) = Blow(h.Act);
        bool InReach(double x) { double d = (x - h.X) * h.Facing; return d > -10 && d < reach; }
        // The keeper first, if it is in reach.
        if (!Boss.Beaten && !Boss.Hidden && InReach(Boss.X) && (BossFight || Boss.Hp < Boss.MaxHp))
        {
            h.Struck = true;
            Boss.Hp -= damage;
            Boss.X += h.Facing * 14;
            if (Boss.Kind == BossKind.Sorcerer)
            {
                // Hit, the sorcerer vanishes at once, to come back somewhere else.
                Boss.Hidden = true;
                Boss.MoveTime = 0;
            }
            else
            {
                Boss.Stagger = 0.3;
                Boss.Move = "idle";
                Boss.MoveTime = 0;
            }
            Effects.Add(new Effect { Kind = EffectKind.Spark, X = Boss.X - h.Facing * 20, Y = level == Level.Low ? 30 : level == Level.Mid ? 80 : 130 });
            Play(Sfx.BossHit);
            if (Boss.Hp <= 0) Beaten();
            return;
        }
        foreach (var e in Enemies.OrderBy(e => Math.Abs(e.X - h.X)))
        {
            if (e.Dead || !InReach(e.X)) continue;
            // What each can be hit at: imps and snakes are low on the ground, an imp in the air high; a jar in the air only high.
            bool hit = e.Kind switch
            {
                EnemyKind.Snake => level == Level.Low,
                EnemyKind.Imp => e.Leaping || e.Holding ? level != Level.Low : level == Level.Low,
                EnemyKind.Jar => level == Level.High && e.Y > 40 && e.Y < 190,
                _ => true,
            };
            if (!hit) continue;
            h.Struck = true;
            Effects.Add(new Effect { Kind = EffectKind.Spark, X = e.X - h.Facing * 14, Y = level == Level.Low ? 26 : level == Level.Mid ? 80 : 130 });
            if (--e.Hp > 0)
            {
                e.Stagger = 0.4;
                e.X += h.Facing * 30;
                Play(Sfx.Hit);
                return;
            }
            Knock(e, h.Facing);
            return;
        }
    }

    private void Knock(Enemy e, int dir)
    {
        e.Dead = true;
        e.Holding = false;
        e.Falling = 0.6;
        e.Vx = dir * 260;
        e.Vy = 300;
        AddScore(e.Points, e.X, 170);
        Play(Sfx.Hit);
    }

    private void Hurt(double damage)
    {
        var h = Hero;
        if (h.Guard > 0) return;
        h.Energy = Math.Max(0, h.Energy - damage);
        h.Guard = 0.6;
        if (!h.Airborne) { h.Act = Act.Hurt; h.ActTime = 0; }
        Play(Sfx.Hurt);
    }

    // ───────────────────────────── the floor's crowd ─────────────────────────────

    private void Arrivals(double dt)
    {
        if (Calm || BossFight || Boss.Beaten) return;
        var h = Hero;
        _spawnIn -= dt;
        if (_spawnIn <= 0 && Enemies.Count(e => !e.Dead && e.Kind != EnemyKind.Jar) < 6)
        {
            _spawnIn = (0.9 + _rng.NextDouble() * 0.9) / Pace;
            int side = _rng.NextDouble() < 0.65 ? Way : -Way;
            double x = h.X + side * (Field.Width / 2 + 60);
            if (x > 20 && x < Field.Length - 20 && Math.Abs(x - Stairs) > Field.Arena)
            {
                int roll = _rng.Next(100);
                var kind = Floor switch
                {
                    1 => roll < 72 ? EnemyKind.Clinger : roll < 92 ? EnemyKind.BladeMan : EnemyKind.Imp,
                    2 or 4 => roll < 60 ? EnemyKind.Clinger : roll < 80 ? EnemyKind.Imp : EnemyKind.BladeMan,
                    3 => roll < 45 ? EnemyKind.Clinger : roll < 72 ? EnemyKind.BladeMan : EnemyKind.Imp,
                    _ => roll < 40 ? EnemyKind.Clinger : roll < 70 ? EnemyKind.BladeMan : EnemyKind.Imp,
                };
                Enemies.Add(new Enemy { Kind = kind, X = x, Facing = -side, Hp = kind == EnemyKind.BladeMan ? 2 : 1, Timer = 1 + _rng.NextDouble() });
            }
        }
        // Jars drop from the rafters on floors two and four, and break open into snakes.
        if (Floor is 2 or 4)
        {
            _jarIn -= dt;
            if (_jarIn <= 0)
            {
                _jarIn = (2.4 + _rng.NextDouble() * 1.6) / Pace;
                double x = h.X + Way * (100 + _rng.NextDouble() * 260);
                if (Math.Abs(x - Stairs) > Field.Arena) Enemies.Add(new Enemy { Kind = EnemyKind.Jar, X = x, Y = 470 });
            }
        }
    }

    private void MoveEnemies(double dt)
    {
        var h = Hero;
        foreach (var e in Enemies)
        {
            e.Age += dt;
            if (e.Dead)
            {
                // Knocked flying.
                e.Falling -= dt;
                e.X += e.Vx * dt;
                e.Vy -= Gravity * dt;
                e.Y = Math.Max(0, e.Y + e.Vy * dt);
                continue;
            }
            if (e.Stagger > 0) { e.Stagger -= dt; continue; }
            if (e.Holding) { h.Energy = Math.Max(0, h.Energy - HoldDrain * dt); e.X = h.X - e.Facing * (e.Kind == EnemyKind.Imp ? 0 : 26); continue; }
            double dx = h.X - e.X, dist = Math.Abs(dx);
            int toward = Math.Sign(dx) == 0 ? 1 : Math.Sign(dx);
            switch (e.Kind)
            {
                case EnemyKind.Clinger:
                    e.Facing = toward;
                    if (dist > 28) e.X += toward * 105 * Pace * dt;
                    else if (Phase == Phase.Play && !h.Airborne)
                    {
                        e.Holding = true;
                        h.Shake = 0;
                        Play(Sfx.Grab);
                    }
                    break;
                case EnemyKind.BladeMan:
                    e.Facing = toward;
                    if (dist > 260) e.X += toward * 95 * Pace * dt;
                    else if (dist < 150) e.X -= toward * 60 * dt;
                    e.Timer -= dt;
                    if (e.Timer <= 0 && dist < 420 && Phase == Phase.Play)
                    {
                        e.Timer = (1.6 + _rng.NextDouble() * 0.8) / Pace;
                        var level = _rng.Next(2) == 0 ? Level.High : Level.Low;
                        Missiles.Add(new Missile { X = e.X + toward * 30, Vx = toward * 430, Level = level, StartX = e.X });
                        Play(Sfx.Throw);
                    }
                    break;
                case EnemyKind.Imp:
                    if (e.Leaping)
                    {
                        e.X += e.Vx * dt;
                        e.Vy -= Gravity * dt;
                        e.Y += e.Vy * dt;
                        // Onto the hero's head.
                        if (Math.Abs(e.X - h.X) < 26 && e.Y > 70 && !h.Crouching && h.Guard <= 0 && Phase == Phase.Play)
                        {
                            e.Holding = true;
                            e.Leaping = false;
                            h.Shake = 0;
                            Play(Sfx.Grab);
                        }
                        else if (e.Y <= 0) { e.Y = 0; e.Leaping = false; e.Timer = 0.6; }
                    }
                    else
                    {
                        e.Facing = toward;
                        e.Timer -= dt;
                        e.X += toward * 165 * Pace * dt;
                        if (dist < 115 && e.Timer <= 0)
                        {
                            e.Leaping = true;
                            e.Vy = 560;
                            e.Vx = toward * 230;
                        }
                    }
                    break;
                case EnemyKind.Jar:
                    e.Vy -= Gravity * 0.5 * dt;
                    e.Y += e.Vy * dt;
                    if (e.Y <= 0)
                    {
                        e.Y = 0;
                        if (dist < 34) Hurt(JarDamage);
                        // It breaks, and a snake slides out.
                        e.Kind = EnemyKind.Snake;
                        e.Facing = toward;
                        Play(Sfx.Jar);
                    }
                    break;
                case EnemyKind.Snake:
                    e.Facing = toward;
                    e.X += toward * 120 * Pace * dt;
                    if (dist < 24 && h.Y < 40)
                    {
                        Hurt(SnakeDamage);
                        e.Dead = true;
                        e.Falling = 0;
                        Play(Sfx.Hiss);
                    }
                    break;
            }
            // Those left far behind give up.
            if (dist > Field.Width * 1.2) { e.Dead = true; e.Falling = 0; }
        }
    }

    private void MoveMissiles(double dt)
    {
        var h = Hero;
        foreach (var m in Missiles)
        {
            m.Age += dt;
            m.X += m.Vx * dt;
            if (m.Returns && !m.Returning && Math.Abs(m.X - m.StartX) > 380) { m.Returning = true; m.Vx = -m.Vx; }
            if (m.Returns && m.Returning && Math.Abs(m.X - Boss.X) < 20) m.Dead = true;
            if (m.Age > 3) m.Dead = true;
            if (m.Dead || Math.Abs(m.X - h.X) > 22) continue;
            // High ones pass over a crouch, low ones under a jump.
            bool dodged = m.Level == Level.High ? h.Crouching : h.Y > 40;
            if (dodged) continue;
            m.Dead = true;
            Hurt(m.Fire ? 12 : m.Returns ? 12 : KnifeDamage);
        }
    }

    // ───────────────────────────── the keeper of the stairs ─────────────────────────────

    /// <summary>How long a keeper winds up before it strikes: the master is quickest, the giant slowest.</summary>
    public double WindUp => (Boss.Kind switch { BossKind.Master => 0.34, BossKind.Giant => 0.6, _ => 0.46 }) / Math.Sqrt(Pace);

    private void MoveBoss(double dt)
    {
        var b = Boss;
        var h = Hero;
        if (b.Beaten) return;
        b.Age += dt;
        b.MoveTime += dt;
        if (b.Stagger > 0) { b.Stagger -= dt; return; }
        double dx = h.X - b.X, dist = Math.Abs(dx);
        b.Facing = Math.Sign(dx) == 0 ? b.Facing : Math.Sign(dx);
        double speed = (b.Kind switch { BossKind.Giant => 60, BossKind.Master => 130, _ => 90 }) * Pace;
        // Keepers never leave their arena.
        double near = Stairs - Way * 60, far = Stairs - Way * (Field.Arena - 40);
        double lo = Math.Min(near, far), hi = Math.Max(near, far);

        if (b.Kind == BossKind.Sorcerer)
        {
            // Seen for a while, throwing fire once; then gone, and somewhere else.
            if (b.Hidden)
            {
                if (b.MoveTime > 0.7)
                {
                    b.Hidden = false;
                    b.MoveTime = 0;
                    // Always back between the hero and the stairs.
                    b.X = Math.Clamp(h.X + Way * (200 + _rng.NextDouble() * 110), lo, hi);
                    b.Struck = false;
                    Play(Sfx.Teleport);
                }
                return;
            }
            if (b.MoveTime > 0.45 && !b.Struck)
            {
                b.Struck = true;
                b.Aim = _rng.Next(2) == 0 ? Level.High : Level.Low;
                Missiles.Add(new Missile { X = b.X + b.Facing * 30, Vx = b.Facing * 360, Level = b.Aim, Fire = true, StartX = b.X });
                Play(Sfx.Throw);
            }
            if (b.MoveTime > 2.4) { b.Hidden = true; b.MoveTime = 0; Play(Sfx.Teleport); }
            return;
        }

        switch (b.Move)
        {
            case "idle":
            {
                double want = b.Kind == BossKind.Thrower ? 290 : b.Reach - 10;
                if (b.Kind == BossKind.Thrower && dist < 110)
                {
                    // Too close for throwing: a quick kick.
                    StartWindUp(b, Level.Low);
                    break;
                }
                if (Math.Abs(dist - want) > 18 && b.MoveTime > 0.15) b.X = Math.Clamp(b.X + Math.Sign(dist - want) * b.Facing * speed * dt, lo, hi);
                else if (b.MoveTime > 0.35) StartWindUp(b, _rng.Next(2) == 0 ? Level.High : Level.Low);
                break;
            }
            case "windup":
                if (b.MoveTime > WindUp)
                {
                    b.Move = "strike";
                    b.MoveTime = 0;
                    if (b.Kind == BossKind.Thrower && dist >= 110)
                    {
                        Missiles.Add(new Missile { X = b.X + b.Facing * 30, Vx = b.Facing * 400, Level = b.Aim, Returns = true, StartX = b.X });
                        Play(Sfx.Throw);
                    }
                    else
                    {
                        Play(Sfx.Swish);
                        double reach = b.Kind == BossKind.Thrower ? 80 : b.Reach;
                        bool dodged = b.Aim == Level.High ? h.Crouching : h.Y > 40;
                        if (dist < reach + 14 && !dodged) Hurt(b.Kind == BossKind.Thrower ? 8 : b.Damage);
                    }
                }
                break;
            case "strike":
                if (b.MoveTime > 0.16) { b.Move = "recover"; b.MoveTime = 0; }
                break;
            case "recover":
                if (b.MoveTime > (b.Kind == BossKind.Thrower ? 1.1 : 0.55))
                {
                    b.Move = "idle";
                    b.MoveTime = 0;
                    // Now and then a step back.
                    if (_rng.NextDouble() < 0.3) b.X = Math.Clamp(b.X - b.Facing * 40, lo, hi);
                }
                break;
        }
    }

    private static void StartWindUp(Boss b, Level aim)
    {
        b.Move = "windup";
        b.MoveTime = 0;
        b.Aim = aim;
    }

    private void Beaten()
    {
        var b = Boss;
        b.Beaten = true;
        b.Hidden = false;
        Missiles.Clear();
        Effects.Add(new Effect { Kind = EffectKind.Word, X = b.X, Y = 260, Text = "K.O.", Colour = 0xFFFFE14A, Life = 1.6 });
        Play(Sfx.BossDown);
        Music(false);
        foreach (var e in Enemies.Where(e => !e.Dead)) Knock(e, Math.Sign(e.X - Hero.X) == 0 ? 1 : Math.Sign(e.X - Hero.X));
        Hero.Act = Act.Stand;
        SetPhase(Phase.ToStairs);
    }

    private void FloorDone()
    {
        int time = (int)TimeLeft * 5, energy = (int)Hero.Energy * 20;
        AddScore(time + energy, Hero.X, 220, 0xFFFFE14A);
        Say($"FLOOR {Floor} CLEARED", $"TIME {time}  +  ENERGY {energy}", ClearSeconds);
        Play(Sfx.FloorClear);
        SetPhase(Phase.FloorClear);
    }

    private void Lose(string cause)
    {
        Cause = cause;
        Lives--;
        Hero.Act = Act.Down;
        Hero.ActTime = 0;
        foreach (var e in Enemies) e.Holding = false;
        Play(Sfx.Death);
        Music(false);
        SetPhase(Phase.Dying);
        Say(cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "LIFE" : "LIVES")} LEFT" : null, DyingSeconds);
    }

    private void AddScore(int points, double x, double y, uint colour = 0xFFF2F2F2)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Colour = colour });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a floor.</summary>
    internal void Go(int floor = 1)
    {
        StartGame();
        BeginFloor(floor);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Enemy Put(EnemyKind kind, double ahead, double y = 0)
    {
        var e = new Enemy { Kind = kind, X = Hero.X + Hero.Facing * ahead, Y = y, Hp = kind == EnemyKind.BladeMan ? 2 : 1, Timer = 99 };
        e.Facing = -Hero.Facing;
        Enemies.Add(e);
        return e;
    }

    internal Missile PutMissile(double ahead, Level level)
    {
        var m = new Missile { X = Hero.X + Hero.Facing * ahead, Vx = -Hero.Facing * 430, Level = level };
        Missiles.Add(m);
        return m;
    }

    /// <summary>Puts the hero a little way short of the keeper.</summary>
    internal void ToBoss(double gap = 100)
    {
        Hero.X = Boss.X - Way * gap;
        Hero.Facing = Way;
        Enemies.Clear();
    }

    internal void SetTime(double t) => TimeLeft = t;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetEnergy(double e) => Hero.Energy = e;
}
