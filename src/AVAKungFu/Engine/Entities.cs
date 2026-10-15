namespace AVAKungFu.Engine;

/// <summary>Walk (−1 left, +1 right), up to jump and down to crouch, and the two attack buttons.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Punch = false, bool Kick = false);

/// <summary>
/// A temple floor seen from the side: positions are pixels along the floor, the camera following the
/// hero. Heights are measured up from the floor.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bottom of the score bar, and where the floor is on screen.</summary>
    public const double Top = 64, FloorY = 640;
    /// <summary>How long a floor is, and the boss's arena at its far end.</summary>
    public const double Length = 3200, Arena = 560;
    public const int Floors = 5;
}

/// <summary>Where an attack lands, or what a thrown thing flies at: head, body or legs.</summary>
public enum Level { High, Mid, Low }

public enum Act
{
    Stand, Walk, Crouch, Jump,
    Punch, Kick, CrouchPunch, CrouchKick, JumpKick,
    Hurt, Held, Down,
}

/// <summary>The hero: one fighter against the temple.</summary>
public sealed class Hero
{
    public double X, Y, Vy;
    /// <summary>−1 facing left, +1 facing right.</summary>
    public int Facing = -1;
    public Act Act = Act.Stand;
    /// <summary>How far into the current move, and how far its feet have walked.</summary>
    public double ActTime, Walk;
    public double Energy = 100;
    /// <summary>A moment of grace after a hit.</summary>
    public double Guard;
    /// <summary>Wriggles towards shaking off whatever has hold.</summary>
    public int Shake;
    public bool Struck;

    public bool Airborne => Y > 0.5;
    public bool Crouching => Act is Act.Crouch or Act.CrouchPunch or Act.CrouchKick;
    public bool Attacking => Act is Act.Punch or Act.Kick or Act.CrouchPunch or Act.CrouchKick or Act.JumpKick;
}

public enum EnemyKind { Clinger, BladeMan, Imp, Jar, Snake }

public sealed class Enemy
{
    public EnemyKind Kind;
    public double X, Y, Vx, Vy, Age;
    public int Facing = 1, Hp = 1;
    /// <summary>Holding on to the hero.</summary>
    public bool Holding;
    /// <summary>A blade man's time to his next throw; an imp's leap; a stagger.</summary>
    public double Timer, Stagger;
    public bool Leaping, Dead;
    /// <summary>Knocked down and flying off: dead but still on screen for a moment.</summary>
    public double Falling;

    public int Points => Kind switch
    {
        EnemyKind.Clinger => 100, EnemyKind.BladeMan => 500, EnemyKind.Imp => 200, EnemyKind.Jar => 100, _ => 200,
    };
}

/// <summary>A knife, a boomerang or a fireball: flies at one height and hurts what it meets there.</summary>
public sealed class Missile
{
    public double X, Vx, StartX;
    public Level Level;
    /// <summary>A boomerang comes back.</summary>
    public bool Returns, Returning;
    public bool Fire, Dead;
    public double Age;
}

public enum BossKind { Staff, Thrower, Giant, Sorcerer, Master }

/// <summary>The keeper of a floor's stairs.</summary>
public sealed class Boss
{
    public BossKind Kind;
    public double X, Hp, MaxHp, Age;
    public int Facing = 1;
    /// <summary>Winding up an attack, striking, or recovering; and how long it has been doing it.</summary>
    public string Move = "idle";
    public double MoveTime;
    public Level Aim;
    public double Stagger;
    /// <summary>The sorcerer, between appearances.</summary>
    public bool Hidden;
    public bool Struck;
    public bool Beaten;

    public string Name => Kind switch
    {
        BossKind.Staff => "BO OF THE STAFF", BossKind.Thrower => "KIRI THE THROWER", BossKind.Giant => "GROMMEL THE GIANT",
        BossKind.Sorcerer => "SHADE THE SORCERER", _ => "THE JADE MASTER",
    };

    /// <summary>How far its blows reach, how hard they land, and how much it can take.</summary>
    public double Reach => Kind switch { BossKind.Staff => 120, BossKind.Giant => 100, BossKind.Master => 88, _ => 70 };
    public double Damage => Kind switch { BossKind.Giant => 20, BossKind.Staff => 14, BossKind.Master => 15, _ => 12 };
    public double Size => Kind == BossKind.Giant ? 1.35 : 1.05;
}

public enum EffectKind { Score, Spark, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
