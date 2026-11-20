// Varianta A: brațul robotic
// Student: gabriel.ario@student.usv.ro
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Tema, varianta A: un braț robotic construit IERARHIC (pivoți + segmente, secțiunea 6):
///   soclu (fix) · pivot bază (rotește în jurul lui Y) · pivot umăr (Z) · pivot cot (Z) · încheietură · clește.
/// Poza brațului este un Vector3: X = unghiul bazei, Y = unghiul umărului, Z = unghiul cotului (în grade).
///  - articulațiile se mișcă prin acțiuni ȚINUTE (viteză unghiulară × dt), cu limite pentru umăr și cot;
///  - Space deschide / închide clește-ul, lin (Tween);
///  - K salvează poza curentă ca un cadru-cheie, Enter redă pozele în buclă (KeyframeTrack),
///    E schimbă easing-ul redării, Backspace șterge pozele;
///  - BONUS: clește-ul poate prinde o bilă (se închide pe ea), o poate muta și o lasă să cadă (gravitație).
/// Toate animațiile folosesc doar timpul de joc (dt).
/// </summary>
public sealed class RobotArmScene : Scene
{
    // --- Acțiunile scenei (apar în meniul H) ---------------------------------------------------
    private const string BaseCw = "bază ↻ (orar)";
    private const string BaseCcw = "bază ↺ (anti-orar)";
    private const string ShoulderRaise = "umăr: ridică";
    private const string ShoulderLower = "umăr: coboară";
    private const string ElbowRaise = "cot: ridică";
    private const string ElbowLower = "cot: coboară";
    private const string GripperToggle = "clește: deschide / închide (prinde)";
    private const string SavePose = "salvează poza (cadru-cheie)";
    private const string PlayPoses = "redă pozele în buclă / oprește";
    private const string CycleEase = "schimbă easing-ul redării";
    private const string ClearPoses = "șterge pozele";

    // --- Dimensiuni și limite ----------------------------------------------------------------
    private const float UpperLength = 2.2f;      // lungimea brațului (umăr → cot)
    private const float ForeLength = 2.0f;       // lungimea antebrațului (cot → încheietură)
    private const float BaseSpeed = 70.0f;       // grade pe secundă
    private const float ShoulderSpeed = 50.0f;
    private const float ElbowSpeed = 60.0f;

    private const float BaseMin = -180.0f, BaseMax = 180.0f;
    private const float ShoulderMin = -40.0f, ShoulderMax = 100.0f;   // 0 = brațul vertical, + = înainte/jos
    private const float ElbowMin = 0.0f, ElbowMax = 145.0f;           // 0 = antebrațul în prelungire
    private const float MinTipY = 0.0f;                               // vârful clește-ului nu coboară sub podea

    // --- Clește și bilă ---------------------------------------------------------------------------
    private const float FingerWidth = 0.14f, FingerLength = 0.5f, PalmHeight = 0.16f;
    private const float MinHalfGap = 0.04f, MaxHalfGap = 0.42f;       // jumătate din distanța dintre degete
    private const float GripDuration = 0.45f;                         // secunde (închis ↔ deschis)
    private const float BallRadius = 0.22f;
    private const float GrabOpen = (BallRadius - MinHalfGap) / (MaxHalfGap - MinHalfGap);   // deschiderea la care degetele ating bila
    private const float GrabReach = 0.4f;                             // distanța maximă clește – bilă ca să o prindă
    private const float Gravity = 20.0f;                              // unități / s² (secțiunea 5.4)
    private static readonly Vector3 TipLocal = new(0.0f, 0.5f, 0.0f); // punctul de prindere, în sistemul încheieturii
    private static readonly Vector3 PadCenter = new(0.0f, 0.0f, 3.0f);
    private const float PadRadius = 0.7f;

    // --- Ierarhia -------------------------------------------------------------------------------
    private SceneObject _basePivot = null!, _shoulderPivot = null!, _elbowPivot = null!, _wrist = null!, _tip = null!;
    private SceneObject _fingerLeft = null!, _fingerRight = null!, _ball = null!;
    private Mesh _markerMesh = null!;

    // --- Starea brațului ----------------------------------------------------------------------
    private Vector3 _pose = new(0.0f, 35.0f, 75.0f);   // (bază, umăr, cot) în grade

    // --- Clește (Tween pe X: 0 = închis, 1 = deschis) ---------------------------------------
    private readonly Tween _grip = new(Vector3.UnitX, Vector3.UnitX, GripDuration, Easing.EaseInOut);
    private bool _gripOpen = true;
    private bool _tryGrab;          // la închidere, bila se prinde o singură dată, când degetele ajung la ea

    // --- Bila ----------------------------------------------------------------------------------
    private bool _holding;
    private float _ballVy;
    private Tween _snap = new(Vector3.Zero, Vector3.Zero, 0.2f, Easing.EaseOut);

    // --- Pozele salvate și redarea ---------------------------------------------------------------
    private enum Mode { Manual, Transition, Playing }
    private Mode _mode = Mode.Manual;
    private readonly KeyframeTrack _poses = new(1.2f);   // o poză la fiecare 1,2 s
    private readonly List<SceneObject> _markers = new();
    private Tween _transition = new(Vector3.Zero, Vector3.Zero, 0.5f, Easing.EaseInOut);
    private float _playTime;
    private int _easeIndex = 3;                          // implicit EaseInOut

    public override string Name => "braț robotic";

    public override IEnumerable<string> ActionNames => new[]
    {
        BaseCcw, BaseCw, ShoulderRaise, ShoulderLower, ElbowRaise, ElbowLower, GripperToggle,
        SavePose, PlayPoses, CycleEase, ClearPoses,
    };

    private float OpenAmount => MathF.Max(_grip.Value.X, _holding ? GrabOpen : 0.0f);

    private bool BallOnPad
    {
        get
        {
            Vector3 p = _ball.WorldPosition;
            float dx = p.X - PadCenter.X, dz = p.Z - PadCenter.Z;
            return !_holding && p.Y <= BallRadius + 0.01f && MathF.Sqrt(dx * dx + dz * dz) < PadRadius - 0.1f;
        }
    }

    public override string Status =>
        $"{(_mode == Mode.Manual ? "manual" : _mode == Mode.Transition ? "pregătire redare" : $"redare ({Easing.All[_easeIndex].Name})")}" +
        $" | poze: {_poses.Count} | clește {OpenAmount:P0} | " +
        (_holding ? "obiect prins" : BallOnPad ? "obiect pe platformă" : "obiect liber");

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(4.5f, 6.0f, 10.5f);
        CameraTarget = new Vector3(0.8f, 1.5f, 0.5f);

        // Tastele scenei: doar aici. Restul codului întreabă de ACȚIUNI.
        input.Bind(BaseCcw, Keys.A);
        input.Bind(BaseCw, Keys.D);
        input.Bind(ShoulderRaise, Keys.W);
        input.Bind(ShoulderLower, Keys.S);
        input.Bind(ElbowRaise, Keys.Up);
        input.Bind(ElbowLower, Keys.Down);
        input.Bind(GripperToggle, Keys.Space);
        input.Bind(SavePose, Keys.K);
        input.Bind(PlayPoses, Keys.Enter, Keys.KeyPadEnter);
        input.Bind(CycleEase, Keys.E);
        input.Bind(ClearPoses, Keys.Backspace);

        Mesh grid = AddMesh(Shapes.Grid(10, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 32, 16));
        Mesh cyl = AddMesh(Shapes.ShadedCylinder(0.5f, 1.0f, 32));       // raza 0,5, înălțimea 1, axa pe Y
        Mesh disk = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 32));
        _markerMesh = cube;

        AddObject("podea", grid);

        SceneObject pad = AddObject("platformă", disk);
        pad.Transform.Position = new Vector3(PadCenter.X, 0.011f, PadCenter.Z);
        pad.Transform.Scale = new Vector3(PadRadius * 2.0f, 1.0f, PadRadius * 2.0f);
        pad.Tint = new Color4(0.3f, 0.85f, 0.45f, 1.0f);

        BuildArm(cube, cyl, sphere);

        _ball = AddObject("bilă de prins", sphere);
        _ball.Transform.Position = new Vector3(3.0f, BallRadius, 0.0f);
        _ball.Transform.Scale = new Vector3(BallRadius);
        _ball.Tint = new Color4(1.0f, 0.55f, 0.2f, 1.0f);

        _poses.Ease = Easing.All[_easeIndex].Function;
        ApplyPose();
        ApplyFingers();
    }

    // --- Construirea ierarhiei -----------------------------------------------------------------
    // Regula din secțiunea 6: pivoții poartă rotațiile; scalarea neuniformă se pune pe formele-copil
    // (segmentele lungi și subțiri), nu pe pivoții care au copii.

    private void BuildArm(Mesh cube, Mesh cyl, Mesh sphere)
    {
        var steel = new Color4(0.65f, 0.68f, 0.75f, 1.0f);
        var orange = new Color4(1.0f, 0.6f, 0.2f, 1.0f);
        var blue = new Color4(0.35f, 0.6f, 1.0f, 1.0f);
        var dark = new Color4(0.35f, 0.38f, 0.45f, 1.0f);

        // Soclul fix.
        SceneObject socket = AddObject("soclu", cyl);
        socket.Transform.Position = new Vector3(0.0f, 0.15f, 0.0f);
        socket.Transform.Scale = new Vector3(1.6f, 0.3f, 1.6f);
        socket.Tint = dark;

        // Baza rotativă: pivot în vârful soclului; turela este copilul lui.
        _basePivot = AddObject("pivot bază", null);
        _basePivot.Transform.Position = new Vector3(0.0f, 0.3f, 0.0f);

        SceneObject turret = AddObject("turelă", cyl, _basePivot);
        turret.Transform.Position = new Vector3(0.0f, 0.3f, 0.0f);
        turret.Transform.Scale = new Vector3(0.7f, 0.6f, 0.7f);
        turret.Tint = steel;

        // Umărul: pivot (rotație în jurul lui Z) + disc de articulație + brațul.
        _shoulderPivot = AddObject("pivot umăr", null, _basePivot);
        _shoulderPivot.Transform.Position = new Vector3(0.0f, 0.6f, 0.0f);

        SceneObject shoulderJoint = AddObject("articulație umăr", cyl, _shoulderPivot);
        shoulderJoint.Transform.Scale = new Vector3(0.55f, 0.5f, 0.55f);
        shoulderJoint.Transform.Rotation = new Vector3(90.0f, 0.0f, 0.0f);   // axa cilindrului devine Z
        shoulderJoint.Tint = dark;

        SceneObject upper = AddObject("braț", cube, _shoulderPivot);
        upper.Transform.Position = new Vector3(0.0f, UpperLength / 2.0f, 0.0f);
        upper.Transform.Scale = new Vector3(0.3f, UpperLength, 0.3f);
        upper.Tint = orange;

        // Cotul: pivot în capătul brațului (copil al umărului, deci urmează brațul).
        _elbowPivot = AddObject("pivot cot", null, _shoulderPivot);
        _elbowPivot.Transform.Position = new Vector3(0.0f, UpperLength, 0.0f);

        SceneObject elbowJoint = AddObject("articulație cot", cyl, _elbowPivot);
        elbowJoint.Transform.Scale = new Vector3(0.45f, 0.45f, 0.45f);
        elbowJoint.Transform.Rotation = new Vector3(90.0f, 0.0f, 0.0f);
        elbowJoint.Tint = dark;

        SceneObject fore = AddObject("antebraț", cube, _elbowPivot);
        fore.Transform.Position = new Vector3(0.0f, ForeLength / 2.0f, 0.0f);
        fore.Transform.Scale = new Vector3(0.24f, ForeLength, 0.24f);
        fore.Tint = blue;

        // Încheietura și clește-ul.
        _wrist = AddObject("încheietură", null, _elbowPivot);
        _wrist.Transform.Position = new Vector3(0.0f, ForeLength, 0.0f);

        SceneObject wristBall = AddObject("articulație încheietură", sphere, _wrist);
        wristBall.Transform.Scale = new Vector3(0.18f);
        wristBall.Tint = dark;

        SceneObject palm = AddObject("palmă", cube, _wrist);
        palm.Transform.Position = new Vector3(0.0f, PalmHeight / 2.0f, 0.0f);
        palm.Transform.Scale = new Vector3(1.1f, PalmHeight, 0.3f);
        palm.Tint = steel;

        _fingerLeft = AddObject("deget stâng", cube, _wrist);
        _fingerLeft.Transform.Scale = new Vector3(FingerWidth, FingerLength, 0.3f);
        _fingerLeft.Tint = new Color4(0.9f, 0.3f, 0.3f, 1.0f);

        _fingerRight = AddObject("deget drept", cube, _wrist);
        _fingerRight.Transform.Scale = new Vector3(FingerWidth, FingerLength, 0.3f);
        _fingerRight.Tint = new Color4(0.9f, 0.3f, 0.3f, 1.0f);

        // Punctul de prindere: pivot fără formă, între degete.
        _tip = AddObject("vârf clește", null, _wrist);
        _tip.Transform.Position = TipLocal;
    }

    // --- Actualizarea, la fiecare cadru ---------------------------------------------------------

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        HandlePoseCommands(input);
        UpdatePose(dt, input);
        UpdateGripper(dt, input);
        UpdateBall(dt);
    }

    // K / Enter / E / Backspace
    private void HandlePoseCommands(Input input)
    {
        if (input.WasPressed(SavePose))
        {
            if (_mode != Mode.Manual)
            {
                ConsolePanel.PrintMessage("oprește redarea (Enter) înainte de a salva o poză");
            }
            else
            {
                _poses.Add(_pose);
                SceneObject marker = AddObject($"poza {_poses.Count}", _markerMesh);
                marker.Transform.Position = _tip.WorldPosition;
                marker.Transform.Scale = new Vector3(0.14f);
                marker.Tint = new Color4(1.0f, 0.9f, 0.3f, 1.0f);
                _markers.Add(marker);
                ConsolePanel.PrintMessage($"poza {_poses.Count} salvată: bază {_pose.X:F0}°, umăr {_pose.Y:F0}°, cot {_pose.Z:F0}°");
            }
        }

        if (input.WasPressed(PlayPoses))
        {
            if (_mode != Mode.Manual)
            {
                _mode = Mode.Manual;
                ConsolePanel.PrintMessage("redare oprită");
            }
            else if (_poses.Count < 2)
            {
                ConsolePanel.PrintMessage("salvează cel puțin 2 poze (K) ca să pornești redarea");
            }
            else
            {
                // Mai întâi brațul ajunge lin la prima poză, apoi pornește pista (fără salt).
                Vector3 first = _poses.Keys[0];
                float duration = MathF.Max(0.3f, (first - _pose).Length / 120.0f);
                _transition = new Tween(_pose, first, duration, Easing.EaseInOut);
                _mode = Mode.Transition;
                ConsolePanel.PrintMessage($"redare pornită ({_poses.Count} poze, easing {Easing.All[_easeIndex].Name})");
            }
        }

        if (input.WasPressed(CycleEase))
        {
            _easeIndex = (_easeIndex + 1) % Easing.All.Length;
            _poses.Ease = Easing.All[_easeIndex].Function;
            ConsolePanel.PrintMessage($"easing-ul redării: {Easing.All[_easeIndex].Name}");
        }

        if (input.WasPressed(ClearPoses))
        {
            _poses.Clear();
            foreach (SceneObject marker in _markers) Objects.Remove(marker);
            _markers.Clear();
            _mode = Mode.Manual;
            ConsolePanel.PrintMessage("pozele au fost șterse");
        }
    }

    private void UpdatePose(float dt, Input input)
    {
        switch (_mode)
        {
            case Mode.Manual:
            {
                // Acțiuni ȚINUTE: viteză unghiulară × dt, cu limite pentru umăr și cot.
                float oldTipY = _tip.WorldPosition.Y;
                Vector3 old = _pose;

                _pose.X = Math.Clamp(_pose.X + input.Axis(BaseCw, BaseCcw) * BaseSpeed * dt, BaseMin, BaseMax);
                _pose.Y = Math.Clamp(_pose.Y + input.Axis(ShoulderRaise, ShoulderLower) * ShoulderSpeed * dt, ShoulderMin, ShoulderMax);
                _pose.Z = Math.Clamp(_pose.Z + input.Axis(ElbowRaise, ElbowLower) * ElbowSpeed * dt, ElbowMin, ElbowMax);
                ApplyPose();

                // Clește-ul nu intră în podea: o mișcare care l-ar duce sub podea este anulată.
                float newTipY = _tip.WorldPosition.Y;
                if (newTipY < MinTipY && newTipY < oldTipY)
                {
                    _pose = old;
                    ApplyPose();
                }
                break;
            }
            case Mode.Transition:
                _transition.Update(dt);
                _pose = _transition.Value;
                if (_transition.IsFinished)
                {
                    _mode = Mode.Playing;
                    _playTime = 0.0f;
                }
                ApplyPose();
                break;

            case Mode.Playing:
                _playTime += dt;
                _pose = _poses.Sample(_playTime);   // cadrele-cheie, interpolate cu easing-ul ales
                ApplyPose();
                break;
        }
    }

    /// <summary>Copiază unghiurile pozei în rotațiile pivoților (axa Z: + = înainte, de aceea semnul minus).</summary>
    private void ApplyPose()
    {
        _basePivot.Transform.Rotation = new Vector3(0.0f, _pose.X, 0.0f);
        _shoulderPivot.Transform.Rotation = new Vector3(0.0f, 0.0f, -_pose.Y);
        _elbowPivot.Transform.Rotation = new Vector3(0.0f, 0.0f, -_pose.Z);
    }

    private void UpdateGripper(float dt, Input input)
    {
        if (input.WasPressed(GripperToggle))
        {
            _gripOpen = !_gripOpen;
            _grip.GoTo(new Vector3(_gripOpen ? 1.0f : 0.0f, 0.0f, 0.0f), GripDuration);   // pornește din valoarea curentă
            if (_gripOpen && _holding) Release();
            _tryGrab = !_gripOpen;
        }
        _grip.Update(dt);

        // Bonus: în timp ce se închide, când degetele ating bila, o prinde (o singură încercare per închidere).
        if (_tryGrab && _grip.Value.X <= GrabOpen + 0.02f)
        {
            _tryGrab = false;
            if (!_holding && (_tip.WorldPosition - _ball.WorldPosition).Length < GrabReach)
            {
                Grab();
            }
        }
        ApplyFingers();
    }

    private void ApplyFingers()
    {
        float half = Easing.Lerp(MinHalfGap, MaxHalfGap, OpenAmount);
        float x = half + FingerWidth / 2.0f;
        float y = PalmHeight + FingerLength / 2.0f;
        _fingerLeft.Transform.Position = new Vector3(-x, y, 0.0f);
        _fingerRight.Transform.Position = new Vector3(x, y, 0.0f);
    }

    // --- Bonus: prinderea bilei ----------------------------------------------------------------------

    private void Grab()
    {
        // Bila devine copilul încheieturii: de acum urmează brațul. Păstrăm poziția din lume
        // (o exprimăm față de părinte) și o ducem lin în punctul de prindere.
        Vector3 local = Vector3.TransformPosition(_ball.WorldPosition, Matrix4.Invert(_wrist.WorldMatrix));
        _ball.Parent = _wrist;
        _ball.Transform.Position = local;
        _snap = new Tween(local, TipLocal, 0.2f, Easing.EaseOut);
        _holding = true;
        _ballVy = 0.0f;
        ConsolePanel.PrintMessage("obiect prins");
    }

    private void Release()
    {
        Vector3 world = _ball.WorldPosition;
        _ball.Parent = null;
        _ball.Transform.Position = world;
        _holding = false;
        _ballVy = 0.0f;
        ConsolePanel.PrintMessage("obiect eliberat");
    }

    private void UpdateBall(float dt)
    {
        if (_holding)
        {
            _snap.Update(dt);
            _ball.Transform.Position = _snap.Value;
            return;
        }

        // Bila liberă cade: întâi viteza, apoi poziția (Euler semi-implicit, secțiunea 5.4).
        Vector3 p = _ball.Transform.Position;
        if (p.Y > BallRadius + 1e-4f || _ballVy > 0.0f)
        {
            _ballVy -= Gravity * dt;
            p.Y += _ballVy * dt;
        }
        if (p.Y <= BallRadius)
        {
            p.Y = BallRadius;
            _ballVy = 0.0f;
        }
        _ball.Transform.Position = p;
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Brațul        : bază {_pose.X:F1}°   umăr {_pose.Y:F1}° [{ShoulderMin:F0}..{ShoulderMax:F0}]   cot {_pose.Z:F1}° [{ElbowMin:F0}..{ElbowMax:F0}]";
        Vector3 tip = _tip.WorldPosition;
        yield return $"Vârf clește   : ({tip.X:F2}, {tip.Y:F2}, {tip.Z:F2})   deschidere {OpenAmount:P0}";
        yield return $"Mod           : {_mode}   easing redare: {Easing.All[_easeIndex].Name}   t = {_playTime % MathF.Max(_poses.Duration, 0.001f):F2} s";
        yield return $"Poze salvate  : {_poses.Count}";
        for (int i = 0; i < _poses.Count; i++)
        {
            Vector3 k = _poses.Keys[i];
            yield return $"   poza {i + 1}      : bază {k.X:F0}°  umăr {k.Y:F0}°  cot {k.Z:F0}°";
        }
        Vector3 b = _ball.WorldPosition;
        yield return $"Obiect        : ({b.X:F2}, {b.Y:F2}, {b.Z:F2})   {(_holding ? "prins" : BallOnPad ? "pe platformă" : "liber")}";
    }
}
