using System.Collections.Generic;
using System.Linq;
using MikeNspired.XRIStarterKit;
using Unity.Cinemachine;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VR_Pogruzchik.Spectator
{
    /// <summary>
    /// Зрительская камера для второго монитора. Рисуется в окно приложения поверх зеркала шлема
    /// и управляется геймпадом (Xbox 360, Steam Controller через Steam Input).
    /// Режимы: облёт цели, свободный полёт, точки-штативы, вид из-за спины игрока.
    /// LIV и шлем не затрагивает: это отдельная камера Unity с Target Eye = None.
    /// </summary>
    public class SpectatorCameraController : MonoBehaviour
    {
        public enum Mode { Orbit, FreeFly, Tripods, BehindPlayer }

        [Header("Cameras")]
        [SerializeField] private Camera outputCamera;
        [SerializeField] private CinemachineCamera orbitCamera;
        [SerializeField] private CinemachineCamera freeFlyCamera;
        [SerializeField] private CinemachineCamera behindPlayerCamera;
        [SerializeField] private CinemachineCamera[] tripodCameras;

        [Header("Targets (empty = found automatically)")]
        [Tooltip("Цели для облёта и штативов. Пусто: все погрузчики в сцене и голова игрока.")]
        [SerializeField] private Transform[] orbitTargets;
        [SerializeField] private Transform playerHead;

        [Header("Output")]
        [SerializeField] private Mode startMode = Mode.Orbit;
        [Tooltip("1 = рисовать прямо в окно. Меньше 1 = рисовать в уменьшенную текстуру и растягивать её на окно (экономит видеокарту).")]
        [Range(0.25f, 1f)] [SerializeField] private float renderScale = 1f;
        [Tooltip("Не рисовать зеркало шлема в окно: окно показывает только зрительскую камеру.")]
        [SerializeField] private bool disableHmdMirror = true;
        [Tooltip("Геймпад не должен нажимать кнопки VR-интерфейса через навигацию UI.")]
        [SerializeField] private bool disableUiGamepadNavigation = true;
        [Tooltip("Геймпад работает, даже когда фокус в другом окне (например, в OBS на первом мониторе). Только в сборке.")]
        [SerializeField] private bool gamepadWorksWithoutFocus = true;
        [SerializeField] private bool showModeHint = true;

        [Header("Orbit")]
        [SerializeField] private float orbitYawSpeed = 90f;
        [SerializeField] private float orbitPitchSpeed = 60f;
        [SerializeField] private float orbitZoomSpeed = 6f;
        [SerializeField] private Vector2 orbitRadiusRange = new(2.5f, 25f);
        [SerializeField] private Vector2 orbitPitchRange = new(-5f, 75f);

        [Header("Free fly")]
        [SerializeField] private float flySpeed = 4f;
        [SerializeField] private float flyFastMultiplier = 3f;
        [SerializeField] private float flySlowMultiplier = 0.3f;
        [SerializeField] private float lookSpeed = 90f;
        [SerializeField] private float flySmoothing = 8f;

        [Header("Lens")]
        [SerializeField] private float zoomFovSpeed = 30f;
        [SerializeField] private Vector2 fovRange = new(20f, 90f);
        [SerializeField] private float defaultFov = 60f;

        private const float HintDuration = 2.5f;

        private Mode mode;
        private int targetIndex;
        private int tripodIndex;
        private CinemachineOrbitalFollow orbitFollow;
        private readonly List<Transform> targets = new();
        private readonly List<string> targetNames = new();

        private float flyYaw, flyPitch;
        private Vector3 flyVelocity;

        private RenderTexture outputTexture;
        private Canvas overlayCanvas;
        private RawImage overlayImage;
        private Vector2Int lastScreenSize;
        private float lastRenderScale = -1f;
        private bool mirrorApplied;

        private float hintUntil;
        private bool helpVisible;
        private GUIStyle hintStyle;

#if UNITY_EDITOR
        // В редакторе OpenXR регистрирует раскладки всех профилей, даже выключенных, причём с задержкой,
        // уже после Awake сцены. Профиль D-Pad Binding занимает имя "Dpad", и Input System перестаёт создавать
        // геймпады ("Cannot instantiate device layout 'Dpad' as child of '/Gamepad'"). В сборке выключенные
        // профили не регистрируются, поэтому возвращаем стандартную раскладку только в редакторе.
        private void Awake()
        {
            InputSystem.onLayoutChange += OnLayoutChange;
            RestoreDpadLayout();
        }

        private static void OnLayoutChange(string layoutName, InputControlLayoutChange change)
        {
            if (change != InputControlLayoutChange.Removed && string.Equals(layoutName, "Dpad", System.StringComparison.OrdinalIgnoreCase))
                RestoreDpadLayout();
        }

        private static void RestoreDpadLayout()
        {
            // Если профиль D-Pad Binding включён, раскладка нужна ему: ничего не трогаем
            var dpadFeature = UnityEngine.XR.OpenXR.OpenXRSettings.Instance
                ? UnityEngine.XR.OpenXR.OpenXRSettings.Instance.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.DPadInteraction>()
                : null;
            if (dpadFeature != null && dpadFeature.enabled) return;

            var dpad = InputSystem.LoadLayout("Dpad");
            if (dpad == null || !dpad.isDeviceLayout) return;
            InputSystem.RegisterLayout<UnityEngine.InputSystem.Controls.DpadControl>("Dpad");
            Debug.LogWarning("[Spectator] Restored the Input System 'Dpad' layout overridden by OpenXR D-Pad Binding, so gamepads work in the Editor.");
        }
#endif

        private void Start()
        {
            // В префабе камера и brain выключены: иначе Cinemachine двигает камеру прямо в редакторе
            // и оставляет в сцене лишние переопределения
            outputCamera.enabled = true;
            if (outputCamera.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = true;

            orbitFollow = orbitCamera ? orbitCamera.GetComponent<CinemachineOrbitalFollow>() : null;
            if (orbitFollow)
            {
                orbitFollow.HorizontalAxis.Wrap = true;
                orbitFollow.VerticalAxis.Range = orbitPitchRange;
            }

            if (!playerHead)
            {
                var origin = FindAnyObjectByType<XROrigin>();
                playerHead = origin && origin.Camera ? origin.Camera.transform : Camera.main ? Camera.main.transform : null;
            }
            CollectTargets();

            if (behindPlayerCamera && playerHead)
                behindPlayerCamera.Follow = behindPlayerCamera.LookAt = playerHead;

            // По умолчанию Input System отключает при потере фокуса устройства, не умеющие работать в фоне.
            // Клавиатура погрузчика и трейлера читается через старый Input и по-прежнему зависит от фокуса.
            // В редакторе настройки не трогаем: там это ассет проекта, а не копия в памяти.
            if (gamepadWorksWithoutFocus && !Application.isEditor)
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            if (disableUiGamepadNavigation)
            {
                foreach (var module in FindObjectsByType<XRUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    module.enableGamepadInput = false;
                    module.enableJoystickInput = false;
                }
            }

            SetTarget(0);
            ResetView(Mode.Orbit);
            SetMode(startMode);
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            InputSystem.onLayoutChange -= OnLayoutChange;
#endif
            ReleaseOutputTexture();
            SetMirror(false);
        }

        private void Update()
        {
            UpdateOutputTarget();
            if (!mirrorApplied) mirrorApplied = SetMirror(disableHmdMirror);

            var pad = Gamepad.current;
            if (pad == null) return;

            float dt = Time.unscaledDeltaTime;
            Vector2 leftStick = pad.leftStick.ReadValue();
            Vector2 rightStick = pad.rightStick.ReadValue();
            float triggers = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            bool previous = pad.leftShoulder.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame;
            bool next = pad.rightShoulder.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame;

            if (pad.buttonNorth.wasPressedThisFrame) SetMode(StepMode(+1));
            if (pad.buttonWest.wasPressedThisFrame) SetMode(StepMode(-1));
            if (pad.buttonSouth.wasPressedThisFrame) ResetView(mode);
            if (pad.startButton.wasPressedThisFrame) showModeHint = !showModeHint;
            helpVisible = pad.selectButton.isPressed;

            float zoom = (pad.dpad.down.isPressed ? 1f : 0f) - (pad.dpad.up.isPressed ? 1f : 0f);
            if (zoom != 0f && LiveCamera() is { } live)
                live.Lens.FieldOfView = Mathf.Clamp(live.Lens.FieldOfView + zoom * zoomFovSpeed * dt, fovRange.x, fovRange.y);

            switch (mode)
            {
                case Mode.Orbit:
                    if (previous) SetTarget(targetIndex - 1);
                    if (next) SetTarget(targetIndex + 1);
                    UpdateOrbit(rightStick, triggers, dt);
                    break;
                case Mode.FreeFly:
                    UpdateFreeFly(pad, leftStick, rightStick, triggers, dt);
                    break;
                case Mode.Tripods:
                    if (previous) SetTripod(tripodIndex - 1);
                    if (next) SetTripod(tripodIndex + 1);
                    break;
            }
        }

        private void UpdateOrbit(Vector2 look, float triggers, float dt)
        {
            if (!orbitFollow) return;
            orbitFollow.HorizontalAxis.Value = Mathf.Repeat(orbitFollow.HorizontalAxis.Value + look.x * orbitYawSpeed * dt + 180f, 360f) - 180f;
            orbitFollow.VerticalAxis.Value = Mathf.Clamp(orbitFollow.VerticalAxis.Value - look.y * orbitPitchSpeed * dt, orbitPitchRange.x, orbitPitchRange.y);
            // RT приближает, LT отдаляет
            orbitFollow.Radius = Mathf.Clamp(orbitFollow.Radius - triggers * orbitZoomSpeed * dt, orbitRadiusRange.x, orbitRadiusRange.y);
        }

        private void UpdateFreeFly(Gamepad pad, Vector2 move, Vector2 look, float triggers, float dt)
        {
            flyYaw += look.x * lookSpeed * dt;
            flyPitch = Mathf.Clamp(flyPitch - look.y * lookSpeed * dt, -85f, 85f);
            var rotation = Quaternion.Euler(flyPitch, flyYaw, 0f);

            float speed = flySpeed;
            if (pad.rightShoulder.isPressed) speed *= flyFastMultiplier;
            if (pad.leftShoulder.isPressed) speed *= flySlowMultiplier;

            // RT вверх, LT вниз
            Vector3 wanted = (rotation * new Vector3(move.x, 0f, move.y) + Vector3.up * triggers) * speed;
            flyVelocity = Vector3.Lerp(flyVelocity, wanted, 1f - Mathf.Exp(-flySmoothing * dt));

            var t = freeFlyCamera.transform;
            t.SetPositionAndRotation(t.position + flyVelocity * dt, rotation);
        }

        public void SetMode(Mode newMode)
        {
            if (!HasCamera(newMode)) newMode = Mode.Orbit;

            if (newMode == Mode.FreeFly && mode != Mode.FreeFly && freeFlyCamera)
            {
                // Продолжаем полёт из того места, где сейчас стоит камера, без скачка
                var from = outputCamera.transform;
                freeFlyCamera.transform.SetPositionAndRotation(from.position, from.rotation);
                freeFlyCamera.Lens.FieldOfView = outputCamera.fieldOfView;
                var euler = from.eulerAngles;
                flyYaw = euler.y;
                flyPitch = euler.x > 180f ? euler.x - 360f : euler.x;
                flyVelocity = Vector3.zero;
            }

            mode = newMode;
            MakeLive(mode == Mode.Tripods ? tripodCameras[tripodIndex] : CameraFor(mode));
            ShowHint();
        }

        private void ResetView(Mode forMode)
        {
            var target = CurrentTarget();
            switch (forMode)
            {
                case Mode.Orbit when orbitFollow != null:
                    orbitFollow.HorizontalAxis.Value = target ? Mathf.DeltaAngle(0f, target.eulerAngles.y) : 0f;
                    orbitFollow.VerticalAxis.Value = 20f;
                    orbitFollow.Radius = 8f;
                    break;
                case Mode.FreeFly when freeFlyCamera != null && target != null:
                    var rotation = Quaternion.Euler(20f, target.eulerAngles.y, 0f);
                    freeFlyCamera.transform.SetPositionAndRotation(target.position + Vector3.up * 1.5f + rotation * new Vector3(0f, 0f, -8f), rotation);
                    flyYaw = target.eulerAngles.y;
                    flyPitch = 20f;
                    flyVelocity = Vector3.zero;
                    break;
            }

            if (CameraFor(forMode) is { } cam) cam.Lens.FieldOfView = defaultFov;
            if (forMode == Mode.Tripods && tripodCameras != null)
                foreach (var tripod in tripodCameras) tripod.Lens.FieldOfView = defaultFov;
        }

        private void SetTarget(int index)
        {
            if (targets.Count == 0) return;
            targetIndex = (index % targets.Count + targets.Count) % targets.Count;
            var target = targets[targetIndex];

            if (orbitCamera) orbitCamera.Follow = orbitCamera.LookAt = target;
            if (tripodCameras != null)
                foreach (var tripod in tripodCameras) tripod.LookAt = target;
            ShowHint();
        }

        private void SetTripod(int index)
        {
            if (tripodCameras == null || tripodCameras.Length == 0) return;
            tripodIndex = (index % tripodCameras.Length + tripodCameras.Length) % tripodCameras.Length;
            MakeLive(tripodCameras[tripodIndex]);
            ShowHint();
        }

        private void CollectTargets()
        {
            targets.Clear();
            targetNames.Clear();

            if (orbitTargets != null && orbitTargets.Length > 0)
            {
                foreach (var t in orbitTargets.Where(t => t != null))
                {
                    targets.Add(t);
                    targetNames.Add(t.name);
                }
                return;
            }

            var vehicles = FindObjectsByType<ArticulationBodyVehicle>(FindObjectsSortMode.None)
                .OrderBy(v => HierarchyPath(v.transform))
                .ToList();
            for (int i = 0; i < vehicles.Count; i++)
            {
                targets.Add(vehicles[i].transform);
                targetNames.Add(vehicles.Count > 1 ? $"Погрузчик {i + 1}" : "Погрузчик");
            }

            if (playerHead)
            {
                targets.Add(playerHead);
                targetNames.Add("Игрок");
            }
        }

        private void MakeLive(CinemachineCamera live)
        {
            foreach (var cam in AllCameras())
                cam.Priority = cam == live ? 10 : 0;
        }

        private IEnumerable<CinemachineCamera> AllCameras()
        {
            if (orbitCamera) yield return orbitCamera;
            if (freeFlyCamera) yield return freeFlyCamera;
            if (behindPlayerCamera) yield return behindPlayerCamera;
            if (tripodCameras != null)
                foreach (var tripod in tripodCameras.Where(t => t != null))
                    yield return tripod;
        }

        private CinemachineCamera CameraFor(Mode m) => m switch
        {
            Mode.Orbit => orbitCamera,
            Mode.FreeFly => freeFlyCamera,
            Mode.BehindPlayer => behindPlayerCamera,
            _ => null
        };

        private CinemachineCamera LiveCamera() => mode == Mode.Tripods ? tripodCameras[tripodIndex] : CameraFor(mode);

        private bool HasCamera(Mode m) => m == Mode.Tripods
            ? tripodCameras != null && tripodCameras.Length > 0
            : CameraFor(m) != null && (m != Mode.BehindPlayer || playerHead != null);

        private Mode StepMode(int step)
        {
            int count = System.Enum.GetValues(typeof(Mode)).Length;
            var m = mode;
            for (int i = 0; i < count; i++)
            {
                m = (Mode)(((int)m + step + count) % count);
                if (HasCamera(m)) return m;
            }
            return mode;
        }

        private Transform CurrentTarget() => targets.Count > 0 ? targets[targetIndex] : null;

        private static string HierarchyPath(Transform t) => t.parent ? HierarchyPath(t.parent) + "/" + t.name : t.name;

        // ----- Вывод в окно -----

        private void UpdateOutputTarget()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen == lastScreenSize && Mathf.Approximately(renderScale, lastRenderScale)) return;
            lastScreenSize = screen;
            lastRenderScale = renderScale;

            if (renderScale >= 0.999f)
            {
                ReleaseOutputTexture();
                if (overlayCanvas) overlayCanvas.enabled = false;
                return;
            }

            int width = Mathf.Max(64, Mathf.RoundToInt(screen.x * renderScale));
            int height = Mathf.Max(64, Mathf.RoundToInt(screen.y * renderScale));
            ReleaseOutputTexture();
            outputTexture = new RenderTexture(width, height, 24) { name = "Spectator Camera" };
            outputTexture.Create();
            outputCamera.targetTexture = outputTexture;

            if (!overlayCanvas)
            {
                var go = new GameObject("Spectator Output Overlay", typeof(Canvas), typeof(RawImage));
                go.transform.SetParent(transform, false);
                overlayCanvas = go.GetComponent<Canvas>();
                overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                overlayCanvas.sortingOrder = 30000;
                overlayImage = go.GetComponent<RawImage>();
                overlayImage.raycastTarget = false;
                var rect = overlayImage.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            overlayImage.texture = outputTexture;
            overlayCanvas.enabled = true;
        }

        private void ReleaseOutputTexture()
        {
            if (!outputTexture) return;
            if (outputCamera) outputCamera.targetTexture = null;
            outputTexture.Release();
            Destroy(outputTexture);
            outputTexture = null;
        }

        /// <summary>Включает или выключает зеркало шлема в окне. Возвращает false, пока XR не запущен.</summary>
        private static bool SetMirror(bool hide)
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            bool applied = false;
            foreach (var display in displays.Where(d => d.running))
            {
                display.SetPreferredMirrorBlitMode(hide ? XRMirrorViewBlitMode.None : XRMirrorViewBlitMode.Default);
                applied = true;
            }
            return applied;
        }

        // ----- Подсказки на экране -----

        private void ShowHint() => hintUntil = Time.unscaledTime + HintDuration;

        private void OnGUI()
        {
            if (!showModeHint && !helpVisible) return;
            hintStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.Max(16, Screen.height / 45),
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(14, 14, 10, 10),
                wordWrap = false
            };

            string text = helpVisible ? HelpText : Time.unscaledTime < hintUntil ? HintText() : null;
            if (text == null) return;

            var content = new GUIContent(text);
            var size = hintStyle.CalcSize(content);
            GUI.Box(new Rect(24, 24, size.x, size.y), content, hintStyle);
        }

        private string HintText()
        {
            string modeName = mode switch
            {
                Mode.Orbit => "Облёт",
                Mode.FreeFly => "Свободный полёт",
                Mode.Tripods => $"Штатив {tripodIndex + 1} из {tripodCameras.Length}",
                _ => "За спиной игрока"
            };
            bool usesTarget = (mode is Mode.Orbit or Mode.Tripods) && targets.Count > 0;
            return usesTarget ? $"{modeName}: {targetNames[targetIndex]}" : modeName;
        }

        private const string HelpText =
            "Y / X — следующий / предыдущий режим\n" +
            "Правый стик — поворот камеры\n" +
            "Левый стик — движение (свободный полёт)\n" +
            "RT / LT — ближе / дальше (облёт), вверх / вниз (полёт)\n" +
            "LB / RB, крестовина ← → — другая цель или штатив\n" +
            "LB / RB в полёте — медленно / быстро\n" +
            "Крестовина ↑ ↓ — зум\n" +
            "A — сбросить вид\n" +
            "Start — подсказки вкл/выкл, Back — эта справка";
    }
}
