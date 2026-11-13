using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Camera FPS (first-person): orientarea vine din două unghiuri Euler, yaw și pitch,
/// controlate cu mouse-ul; deplasarea se face cu W A S D.
/// În modul "mers" (implicit) camera rămâne la înălțimea ochilor; în modul "zbor liber"
/// (free-fly, tasta G) se deplasează pe direcția privirii.
/// </summary>
public class FpsCamera : Camera
{
    // --- Starea camerei ---------------------------------------------------------

    private Vector3 _position;
    public override Vector3 Position => _position;

    /// <summary>Girația (yaw), în grade: rotația stânga-dreapta, în jurul axei Y. -90° = privire spre -Z.</summary>
    public float Yaw { get; private set; }

    /// <summary>Tangajul (pitch), în grade: rotația sus-jos. Limitat la ±MaxPitch.</summary>
    public float Pitch { get; private set; }

    /// <summary>Zbor liber (true) sau mers pe sol (false). Comutat cu tasta G (facultativ, TODO 5.7).</summary>
    public bool FreeFly { get; set; }

    // Baza camerei: trei vectori unitate, perpendiculari doi câte doi (secțiunea 5).
    private Vector3 _front = -Vector3.UnitZ;
    private Vector3 _right = Vector3.UnitX;
    private Vector3 _up = Vector3.UnitY;

    public override Vector3 Front => _front;

    // --- Parametri ----------------------------------------------------------------

    public const float MaxPitch = 89.0f;               // grade; de ce nu 90, vezi secțiunea 5.3
    public float MoveSpeed = 3.0f;                     // unități de lume pe secundă
    public float SprintFactor = 2.5f;                  // Shift ținut apăsat
    public float MouseSensitivity = 0.1f;              // grade pe pixel
    public float FovStep = 2.0f;                       // grade pentru un pas al rotiței

    private static readonly Vector3 WorldUp = Vector3.UnitY;
    private static readonly Vector3 StartPosition = new(0.0f, 1.7f, 6.0f);   // 1,7 m = înălțimea ochilor
    private const float StartYaw = -90.0f;
    private const float StartPitch = 0.0f;

    public FpsCamera()
    {
        Reset();
    }

    public override string Name => FreeFly ? "FPS – zbor liber" : "FPS – mers";

    /// <summary>Matricea view: camera aflată în Position privește spre punctul Position + Front.</summary>
    public override Matrix4 ViewMatrix =>
        Matrix4.LookAt(_position, _position + _front, _up);

    public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
    {
        // --- 1. Orientarea: mouse-ul modifică yaw și pitch -------------------------

        // TODO 5.5 (a): mouse-ul orizontal -> yaw, mouse-ul vertical -> pitch (semn schimbat:
        //               pe ecran Y crește în jos, iar pitch pozitiv = privire în sus).
        Yaw += mouseDelta.X * MouseSensitivity;
        Pitch -= mouseDelta.Y * MouseSensitivity;
        Pitch = Math.Clamp(Pitch, -MaxPitch, MaxPitch);

        UpdateVectors();

        // --- 2. FOV: rotița mouse-ului --------------------------------------------

        // TODO 5.1 (c): rotița în sus (ScrollDelta.Y > 0) micșorează FOV-ul (zoom)
        if (mouse.ScrollDelta.Y != 0.0f)
        {
            ChangeFov(-mouse.ScrollDelta.Y * FovStep);
        }

        // --- 3. Deplasarea: W A S D ---------------------------------------------------

        // TODO 5.6 + 5.7: (1) "forward"
        Vector3 forward;
        if (FreeFly)
        {
            forward = _front;                                   // zbor liber: exact direcția privirii
        }
        else
        {
            forward = new Vector3(_front.X, 0.0f, _front.Z);    // mers: proiecția pe planul orizontal
            forward.Normalize();                                // nu poate fi nul: |pitch| <= 89°
        }

        // (2) suma direcțiilor tastelor apăsate
        Vector3 move = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) move += forward;
        if (keyboard.IsKeyDown(Keys.S)) move -= forward;
        if (keyboard.IsKeyDown(Keys.D)) move += _right;
        if (keyboard.IsKeyDown(Keys.A)) move -= _right;

        // TODO 5.7 (facultativ): în zbor liber, Space urcă și Ctrl stânga coboară (axa Y a lumii)
        if (FreeFly)
        {
            if (keyboard.IsKeyDown(Keys.Space)) move += WorldUp;
            if (keyboard.IsKeyDown(Keys.LeftControl)) move -= WorldUp;
        }

        // (3) + (4) normalizare (pe diagonală nu mergem mai repede), apoi viteză · dt
        if (move.LengthSquared > 0.0f)
        {
            move.Normalize();
            float speed = MoveSpeed * (keyboard.IsKeyDown(Keys.LeftShift) ? SprintFactor : 1.0f);
            _position += move * speed * dt;
        }
    }

    /// <summary>
    /// TEMĂ (bonus): așază camera într-o poziție dată, privind într-o direcție dată.
    /// Yaw și pitch rezultă din formulele inverse: pitch = asin(front.y), yaw = atan2(front.z, front.x).
    /// </summary>
    public void PlaceAt(Vector3 position, Vector3 direction)
    {
        _position = position;
        direction.Normalize();
        Pitch = Math.Clamp(MathHelper.RadiansToDegrees(MathF.Asin(direction.Y)), -MaxPitch, MaxPitch);
        Yaw = MathHelper.RadiansToDegrees(MathF.Atan2(direction.Z, direction.X));
        UpdateVectors();
    }

    /// <summary>
    /// Recalculează baza camerei din yaw și pitch (unghiuri Euler -> vector de direcție).
    /// </summary>
    private void UpdateVectors()
    {
        // TODO 5.5 (b): Front din yaw și pitch (formula din secțiunea 5.2), apoi Right și Up
        //               prin produse vectoriale; toți trei normalizați.
        float yaw = MathHelper.DegreesToRadians(Yaw);       // funcțiile trigonometrice cer radiani
        float pitch = MathHelper.DegreesToRadians(Pitch);

        _front = Vector3.Normalize(new Vector3(
            MathF.Cos(yaw) * MathF.Cos(pitch),
            MathF.Sin(pitch),
            MathF.Sin(yaw) * MathF.Cos(pitch)));
        _right = Vector3.Normalize(Vector3.Cross(_front, WorldUp));
        _up = Vector3.Normalize(Vector3.Cross(_right, _front));
    }

    public override void Reset()
    {
        _position = StartPosition;
        Yaw = StartYaw;
        Pitch = StartPitch;
        FieldOfView = 60.0f;
        UpdateVectors();
    }

    public override IEnumerable<string> Describe()
    {
        yield return $"Poziție        : ({_position.X,6:F2}, {_position.Y,6:F2}, {_position.Z,6:F2})";
        yield return $"Yaw / pitch    : {NormalizeAngle(Yaw),7:F1}° / {Pitch,6:F1}°";
        yield return $"Front          : ({_front.X,6:F2}, {_front.Y,6:F2}, {_front.Z,6:F2})";
        yield return $"Right          : ({_right.X,6:F2}, {_right.Y,6:F2}, {_right.Z,6:F2})";
        yield return $"Up             : ({_up.X,6:F2}, {_up.Y,6:F2}, {_up.Z,6:F2})";
    }

    /// <summary>Aduce un unghi în intervalul (-180°, 180°] (doar pentru afișare).</summary>
    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360.0f;
        if (degrees > 180.0f) degrees -= 360.0f;
        if (degrees <= -180.0f) degrees += 360.0f;
        return degrees;
    }
}