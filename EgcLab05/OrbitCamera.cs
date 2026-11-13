using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Camera orbită (TEMA): privește mereu spre o țintă și se mișcă pe o sferă în jurul ei.
/// Poziția pe sferă = distanța (raza) + două unghiuri: azimutul (în jurul axei Y, ca yaw)
/// și elevația (deasupra planului orizontal, ca pitch).
/// Comenzi: clic stânga + mouse sau săgeți = rotire; rotița = distanța; W A S D = mută ținta.
/// </summary>
public class OrbitCamera : Camera
{
    // --- Starea camerei ---------------------------------------------------------

    /// <summary>Punctul spre care privește camera (în coordonate de lume).</summary>
    public Vector3 Target { get; private set; }

    /// <summary>Distanța de la cameră la țintă.</summary>
    public float Distance { get; private set; }

    /// <summary>Azimutul, în grade: unghiul în jurul axei Y. 90° = camera pe axa +Z, privind spre -Z.</summary>
    public float Azimuth { get; private set; }

    /// <summary>Elevația, în grade: unghiul deasupra planului orizontal. Limitat la ±MaxElevation.</summary>
    public float Elevation { get; private set; }

    // --- Parametri ----------------------------------------------------------------

    public const float MaxElevation = 89.0f;   // același motiv ca la pitch (secțiunea 5.3)
    public const float MinDistance = 1.5f;
    public const float MaxDistance = 40.0f;    // sub Far (100), ca ținta să rămână în câmpul vizual

    public float MouseSensitivity = 0.3f;      // grade pe pixel
    public float KeyRotationSpeed = 90.0f;     // grade pe secundă (săgeți)
    public float ZoomFactor = 0.9f;            // un pas al rotiței înmulțește distanța cu 0.9 (sau 1/0.9)
    public float TargetSpeed = 4.0f;           // unități pe secundă (W A S D, bonus)

    private static readonly Vector3 StartTarget = new(0.0f, 1.0f, 0.0f);
    private const float StartDistance = 8.0f;
    private const float StartAzimuth = 90.0f;
    private const float StartElevation = 20.0f;

    public OrbitCamera()
    {
        Reset();
    }

    public override string Name => "Orbită";

    /// <summary>Poziția = țintă + distanță · (cos el · cos az, sin el, cos el · sin az).</summary>
    public override Vector3 Position
    {
        get
        {
            float az = MathHelper.DegreesToRadians(Azimuth);
            float el = MathHelper.DegreesToRadians(Elevation);
            return Target + Distance * new Vector3(
                MathF.Cos(el) * MathF.Cos(az),
                MathF.Sin(el),
                MathF.Cos(el) * MathF.Sin(az));
        }
    }

    /// <summary>Direcția privirii: de la cameră spre țintă.</summary>
    public override Vector3 Front => Vector3.Normalize(Target - Position);

    public override Matrix4 ViewMatrix => Matrix4.LookAt(Position, Target, Vector3.UnitY);

    public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
    {
        // --- 1. Rotirea: clic stânga ținut + mouse ---------------------------------
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            Azimuth += mouseDelta.X * MouseSensitivity;
            Elevation += mouseDelta.Y * MouseSensitivity;   // Y pe ecran crește în jos: tragere în jos = camera urcă
        }

        // --- 2. Rotirea: săgeți -----------------------------------------------------
        float step = KeyRotationSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Right)) Azimuth += step;
        if (keyboard.IsKeyDown(Keys.Left)) Azimuth -= step;
        if (keyboard.IsKeyDown(Keys.Up)) Elevation += step;
        if (keyboard.IsKeyDown(Keys.Down)) Elevation -= step;

        Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);
        Azimuth -= 360.0f * MathF.Floor(Azimuth / 360.0f);   // păstrat în [0, 360)

        // --- 3. Distanța: rotița (NU schimbă FOV-ul) ------------------------------
        if (mouse.ScrollDelta.Y != 0.0f)
        {
            Distance *= MathF.Pow(ZoomFactor, mouse.ScrollDelta.Y);
            Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
        }

        // --- 4. Bonus: W A S D mută ținta în planul orizontal -----------------------
        Vector3 front = Front;
        Vector3 forward = new Vector3(front.X, 0.0f, front.Z);
        if (forward.LengthSquared > 0.0f)
        {
            forward.Normalize();
            Vector3 right = Vector3.Cross(forward, Vector3.UnitY);

            Vector3 move = Vector3.Zero;
            if (keyboard.IsKeyDown(Keys.W)) move += forward;
            if (keyboard.IsKeyDown(Keys.S)) move -= forward;
            if (keyboard.IsKeyDown(Keys.D)) move += right;
            if (keyboard.IsKeyDown(Keys.A)) move -= right;

            if (move.LengthSquared > 0.0f)
            {
                move.Normalize();
                Target += move * TargetSpeed * dt;
            }
        }
    }

    /// <summary>
    /// Bonus: preia poziția și direcția camerei FPS, fără salt al imaginii.
    /// Ținta = punctul aflat la <paramref name="distance"/> în fața camerei FPS;
    /// unghiurile rezultă din vectorul unitar țintă -> cameră, o = -front:
    /// elevația = asin(o.y), azimutul = atan2(o.z, o.x).
    /// </summary>
    public void SetFromFps(Vector3 position, Vector3 front, float distance)
    {
        front.Normalize();
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
        Target = position + front * Distance;

        Vector3 o = -front;
        Elevation = Math.Clamp(MathHelper.RadiansToDegrees(MathF.Asin(o.Y)), -MaxElevation, MaxElevation);
        Azimuth = MathHelper.RadiansToDegrees(MathF.Atan2(o.Z, o.X));
        Azimuth -= 360.0f * MathF.Floor(Azimuth / 360.0f);
    }

    public override void Reset()
    {
        Target = StartTarget;
        Distance = StartDistance;
        Azimuth = StartAzimuth;
        Elevation = StartElevation;
        FieldOfView = 60.0f;
    }

    public override IEnumerable<string> Describe()
    {
        Vector3 p = Position;
        yield return $"Țintă          : ({Target.X,6:F2}, {Target.Y,6:F2}, {Target.Z,6:F2})";
        yield return $"Distanță       : {Distance,6:F2}   (între {MinDistance} și {MaxDistance})";
        yield return $"Azimut / elev. : {Azimuth,7:F1}° / {Elevation,6:F1}°";
        yield return $"Poziție        : ({p.X,6:F2}, {p.Y,6:F2}, {p.Z,6:F2})";
    }
}