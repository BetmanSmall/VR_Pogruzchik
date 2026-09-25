using System.Collections.Generic;
using System.Linq;
using MikeNspired.XRIStarterKit;
using Unity.Cinemachine;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VR_Pogruzchik.Spectator
{
    /// <summary>
    /// Зрительская камера для второго монитора. Рисуется в окно приложения поверх зеркала шлема
    /// и управляется геймпадом (Xbox 360, Steam Controller через Steam Input), клавиатурой и мышью.
    /// Режимы: облёт цели, свободный полёт, точки-штативы, вид из-за спины игрока.
    /// LIV и шлем не затрагивает: это отдельная камера Unity с выключенным XR Rendering.
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
        [Tooltip("Геймпад и мышь не должны нажимать кнопки VR-интерфейса.")]
        [FormerlySerializedAs("disableUiGamepadNavigation")]
        [SerializeField] private bool disableUiDesktopInput = true;
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

        [Header("Keyboard and mouse")]
        [Tooltip("Градусов поворота на пиксель движения мыши с зажатой правой кнопкой.")]
        [SerializeField] private float mouseSensitivity = 0.15f;
        [Tooltip("На какую долю меняется расстояние облёта за щелчок колеса.")]
        [SerializeField] private float wheelOrbitStep = 0.1f;
        [Tooltip("На сколько градусов меняется угол обзора за щелчок колеса.")]
        [SerializeField] private float wheelFovStep = 3f;

        private const float HintDuration = 2.5f;

        /// <summary>Ввод за кадр, собранный с геймпада, клавиатуры и мыши.</summary>
        private struct InputFrame
        {
            public Vector2 Move;        // левый стик; стрелки в полёте
            public Vector2 Look;        // правый стик; стрелки при облёте
            public Vector2 MouseLook;   // градусы от мыши с зажатой правой кнопкой
            public float Vertical;      // RT − LT, PageUp − PageDown
            public float Zoom;          // крестовина ↓ − ↑, «−» и «+»: скорость изменения угла обзора
            public float Wheel;         // щелчок колеса от себя = +1
            public bool Fast, Slow;
            public int ModeStep, TargetStep;
            public Mode? ModeSelect;
            public bool Reset;
        }

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
        private bool helpToggled;
        private bool cursorCaptured;
        private GUIStyle hintStyle;
        private Texture2D hintBackground;

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

            // URP решает, рисовать ли камеру в шлем, по allowXRRendering, а не по Target Eye.
            // Зрительская камера должна попадать только в окно, в шлеме остаётся камера XR Origin.
            outputCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;

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

            if (disableUiDesktopInput)
            {
                // Окно показывает зрительскую камеру, а мышь XR UI целится через камеру шлема,
                // поэтому клики в окне попадали бы в невидимые кнопки
                foreach (var module in FindObjectsByType<XRUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    module.enableGamepadInput = false;
                    module.enableJoystickInput = false;
                    module.enableMouseInput = false;
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
            SetCursorCaptured(false);
            if (hintBackground) Destroy(hintBackground);
        }

        private void OnDisable() => SetCursorCaptured(false);

        private void Update()
        {
            UpdateOutputTarget();
            if (!mirrorApplied) mirrorApplied = SetMirror(disableHmdMirror);

            float dt = Time.unscaledDeltaTime;
            var input = ReadInput();

            if (input.ModeSelect is { } selected) SetMode(selected);
            else if (input.ModeStep != 0) SetMode(StepMode(input.ModeStep > 0 ? 1 : -1));
            if (input.Reset) ResetView(mode);

            if (LiveCamera() is { } live)
            {
                float fov = live.Lens.FieldOfView + input.Zoom * zoomFovSpeed * dt;
                if (mode != Mode.Orbit) fov -= input.Wheel * wheelFovStep;   // при облёте колесо меняет расстояние
                live.Lens.FieldOfView = Mathf.Clamp(fov, fovRange.x, fovRange.y);
            }

            switch (mode)
            {
                case Mode.Orbit:
                    if (input.TargetStep != 0) SetTarget(targetIndex + input.TargetStep);
                    UpdateOrbit(input, dt);
                    break;
                case Mode.FreeFly:
                    UpdateFreeFly(input, dt);
                    break;
                case Mode.Tripods:
                    if (input.TargetStep != 0) SetTripod(tripodIndex + input.TargetStep);
                    break;
            }
        }

        private InputFrame ReadInput()
        {
            var input = new InputFrame();
            bool helpHeld = false;

            if (Gamepad.current is { } pad)
            {
                input.Move = pad.leftStick.ReadValue();
                input.Look = pad.rightStick.ReadValue();
                input.Vertical = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                input.Zoom = Axis(pad.dpad.down, pad.dpad.up);
                input.Fast = pad.rightShoulder.isPressed;
                input.Slow = pad.leftShoulder.isPressed;
                if (pad.buttonNorth.wasPressedThisFrame) input.ModeStep++;
                if (pad.buttonWest.wasPressedThisFrame) input.ModeStep--;
                if (pad.rightShoulder.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame) input.TargetStep++;
                if (pad.leftShoulder.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame) input.TargetStep--;
                input.Reset |= pad.buttonSouth.wasPressedThisFrame;
                if (pad.startButton.wasPressedThisFrame) showModeHint = !showModeHint;
                helpHeld = pad.selectButton.isPressed;
            }

            // Клавиатуру и мышь слушаем только при фокусе окна: геймпад работает и без него,
            // а набор текста в OBS не должен двигать камеру. W, A, S, D, R, F заняты управлением погрузчиком.
            bool focused = Application.isFocused;
            if (focused && Keyboard.current is { } kb)
            {
                var arrows = new Vector2(Axis(kb.rightArrowKey, kb.leftArrowKey), Axis(kb.upArrowKey, kb.downArrowKey));
                if (mode == Mode.FreeFly) input.Move += arrows;
                else input.Look += arrows;
                input.Vertical += Axis(kb.pageUpKey, kb.pageDownKey);
                input.Zoom += Axis(kb.minusKey, kb.equalsKey) + Axis(kb.numpadMinusKey, kb.numpadPlusKey);
                input.Fast |= kb.shiftKey.isPressed;
                input.Slow |= kb.ctrlKey.isPressed;

                if (kb.tabKey.wasPressedThisFrame) input.ModeStep += kb.shiftKey.isPressed ? -1 : 1;
                if (kb.digit1Key.wasPressedThisFrame) input.ModeSelect = Mode.Orbit;
                if (kb.digit2Key.wasPressedThisFrame) input.ModeSelect = Mode.FreeFly;
                if (kb.digit3Key.wasPressedThisFrame) input.ModeSelect = Mode.Tripods;
                if (kb.digit4Key.wasPressedThisFrame) input.ModeSelect = Mode.BehindPlayer;
                if (kb.periodKey.wasPressedThisFrame) input.TargetStep++;
                if (kb.commaKey.wasPressedThisFrame) input.TargetStep--;
                input.Reset |= kb.homeKey.wasPressedThisFrame;
                if (kb.f1Key.wasPressedThisFrame) helpToggled = !helpToggled;
                if (kb.f2Key.wasPressedThisFrame) showModeHint = !showModeHint;
            }

            // Мышь поворачивает камеру только с зажатой правой кнопкой, курсор на это время прячется
            var mouse = focused ? Mouse.current : null;
            bool mouseLook = mouse != null && mouse.rightButton.isPressed;
            SetCursorCaptured(mouseLook);
            if (mouse != null)
            {
                if (mouseLook) input.MouseLook = mouse.delta.ReadValue() * mouseSensitivity;
                float scroll = mouse.scroll.ReadValue().y;
                input.Wheel = scroll > 0f ? 1f : scroll < 0f ? -1f : 0f;
            }

            input.Move = Vector2.ClampMagnitude(input.Move, 1f);
            input.Look = Vector2.ClampMagnitude(input.Look, 1f);
            input.Vertical = Mathf.Clamp(input.Vertical, -1f, 1f);
            input.Zoom = Mathf.Clamp(input.Zoom, -1f, 1f);
            helpVisible = helpHeld || helpToggled;
            return input;
        }

        private static float Axis(ButtonControl positive, ButtonControl negative) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);

        private void SetCursorCaptured(bool captured)
        {
            if (captured == cursorCaptured) return;
            cursorCaptured = captured;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        private void UpdateOrbit(InputFrame input, float dt)
        {
            if (!orbitFollow) return;
            float yaw = input.Look.x * orbitYawSpeed * dt + input.MouseLook.x;
            float pitch = input.Look.y * orbitPitchSpeed * dt + input.MouseLook.y;
            orbitFollow.HorizontalAxis.Value = Mathf.Repeat(orbitFollow.HorizontalAxis.Value + yaw + 180f, 360f) - 180f;
            orbitFollow.VerticalAxis.Value = Mathf.Clamp(orbitFollow.VerticalAxis.Value - pitch, orbitPitchRange.x, orbitPitchRange.y);

            // RT и PageUp приближают, LT и PageDown отдаляют; колесо от себя приближает
            float radius = orbitFollow.Radius - input.Vertical * orbitZoomSpeed * dt;
            radius *= 1f - input.Wheel * wheelOrbitStep;
            orbitFollow.Radius = Mathf.Clamp(radius, orbitRadiusRange.x, orbitRadiusRange.y);
        }

        private void UpdateFreeFly(InputFrame input, float dt)
        {
            flyYaw += input.Look.x * lookSpeed * dt + input.MouseLook.x;
            flyPitch = Mathf.Clamp(flyPitch - input.Look.y * lookSpeed * dt - input.MouseLook.y, -85f, 85f);
            var rotation = Quaternion.Euler(flyPitch, flyYaw, 0f);

            float speed = flySpeed;
            if (input.Fast) speed *= flyFastMultiplier;
            if (input.Slow) speed *= flySlowMultiplier;

            // RT и PageUp вверх, LT и PageDown вниз
            Vector3 wanted = (rotation * new Vector3(input.Move.x, 0f, input.Move.y) + Vector3.up * input.Vertical) * speed;
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
            if (hintStyle == null)
            {
                // Стандартный фон IMGUI почти прозрачный, на светлых контейнерах текст теряется
                hintBackground = new Texture2D(1, 1);
                hintBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
                hintBackground.Apply();
                hintStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = Mathf.Max(16, Screen.height / 45),
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(14, 14, 10, 10),
                    wordWrap = false,
                    normal = { background = hintBackground, textColor = Color.white }
                };
            }

            if (helpVisible)
            {
                DrawHelp();
                return;
            }
            if (Time.unscaledTime >= hintUntil) return;

            var content = new GUIContent(HintText());
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

        private static readonly string[][] HelpRows =
        {
            new[] { "", "Геймпад", "Клавиатура и мышь" },
            new[] { "Режим", "Y / X", "1–4, Tab / Shift+Tab" },
            new[] { "Поворот", "правый стик", "ПКМ + мышь; стрелки при облёте" },
            new[] { "Движение (полёт)", "левый стик", "стрелки" },
            new[] { "Вверх / вниз (полёт)", "RT / LT", "PageUp / PageDown" },
            new[] { "Ближе / дальше (облёт)", "RT / LT", "PageUp / PageDown, колесо" },
            new[] { "Быстро / медленно (полёт)", "RB / LB", "Shift / Ctrl" },
            new[] { "Цель или штатив", "LB / RB, крестовина ← →", "« , » и « . »" },
            new[] { "Зум", "крестовина ↑ ↓", "« + » и « − », колесо вне облёта" },
            new[] { "Сбросить вид", "A", "Home" },
            new[] { "Эта справка", "Back (держать)", "F1" },
            new[] { "Подсказки вкл/выкл", "Start", "F2" },
        };

        private GUIStyle helpCellStyle, helpHeaderStyle;

        private void DrawHelp()
        {
            helpCellStyle ??= new GUIStyle(GUI.skin.label) { fontSize = hintStyle.fontSize, wordWrap = false };
            helpHeaderStyle ??= new GUIStyle(helpCellStyle) { fontStyle = FontStyle.Bold };

            var widths = new float[3];
            foreach (var row in HelpRows)
                for (int c = 0; c < 3; c++)
                    widths[c] = Mathf.Max(widths[c], helpHeaderStyle.CalcSize(new GUIContent(row[c])).x);

            const float padding = 14f, gap = 28f;
            float rowHeight = helpCellStyle.CalcSize(new GUIContent("Ay")).y;
            var box = new Rect(24, 24, widths.Sum() + gap * 2 + padding * 2, rowHeight * HelpRows.Length + padding * 2);
            GUI.Box(box, GUIContent.none, hintStyle);

            for (int r = 0; r < HelpRows.Length; r++)
            {
                float x = box.x + padding;
                for (int c = 0; c < 3; c++)
                {
                    var style = r == 0 || c == 0 ? helpHeaderStyle : helpCellStyle;
                    GUI.Label(new Rect(x, box.y + padding + r * rowHeight, widths[c], rowHeight), HelpRows[r][c], style);
                    x += widths[c] + gap;
                }
            }
        }
    }
}
