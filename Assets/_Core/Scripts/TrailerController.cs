using System.Collections;
using MikeNspired.XRIStarterKit;
using UnityEngine;
using UnityEngine.Playables;

namespace VR_Pogruzchik.Trailer
{
    /// <summary>
    /// Контроллер для съемки трейлера VR погрузчика.
    /// Управляет Cinemachine камерами, действиями Forklift и Timeline.
    /// Запускается по нажатию на пробел или автоматически при старте сцены.
    /// </summary>
    public class TrailerController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private GameObject trailerCamera;
        [SerializeField] private PlayableDirector timeline;
        [SerializeField] private Animator forkliftAnimator;

        [Header("Forklift References")]
        [SerializeField] private ForkliftControls forkliftControls;
        [SerializeField] private ArticulationBodyVehicle forkliftVehicle;
        [SerializeField] private VehicleTeleportPlayer vehicleTeleport;

        [Header("Cameras")]
        [SerializeField] private GameObject cmAerial;
        [SerializeField] private GameObject cmApproach;
        [SerializeField] private GameObject cmPlayerEntry;
        [SerializeField] private GameObject cmFirstPerson;
        [SerializeField] private GameObject cmExitWide;

        [Header("Player Body (XR Origin)")]
        [SerializeField] private Transform playerRig;

        [Header("Settings")]
        [SerializeField] private bool autoStart = false;
        [SerializeField] private KeyCode startKey = KeyCode.Space;

        private bool isRecording = false;

        private void Start()
        {
            if (autoStart)
                StartTrailerSequence();
        }

        private void Update()
        {
            if (Input.GetKeyDown(startKey) && !isRecording)
                StartTrailerSequence();
        }

        [ContextMenu("Start Trailer Sequence")]
        public void StartTrailerSequence()
        {
            if (isRecording) return;
            isRecording = true;
            StartCoroutine(TrailerSequence());
        }

        private IEnumerator TrailerSequence()
        {
            Debug.Log("[Trailer] Начало съемки трейлера!");

            // Включаем Trailer Camera
            if (trailerCamera) trailerCamera.SetActive(true);

            // ===== СЕКЦИЯ 1: Общий план сверху (0-2 сек) =====
            Debug.Log("[Trailer] Секция 1: Общий план сверху");
            SetCameraActive(cmAerial);
            yield return new WaitForSeconds(2f);

            // ===== СЕКЦИЯ 2: Пролет к погрузчику (2-6 сек) =====
            Debug.Log("[Trailer] Секция 2: Пролет к погрузчику");
            SetCameraActive(cmApproach);
            yield return new WaitForSeconds(3f);

            // ===== СЕКЦИЯ 3: Показ игрока + посадка (6-9 сек) =====
            Debug.Log("[Trailer] Секция 3: Посадка в погрузчик");
            SetCameraActive(cmPlayerEntry);

            // Телепортируем игрока в погрузчик
            if (vehicleTeleport)
            {
                vehicleTeleport.EnterVehicle();
                // После телепортации привязываем FirstPerson камеру к голове игрока
                if (cmFirstPerson && playerRig)
                {
                    var xrCamera = playerRig.GetComponentInChildren<Camera>();
                    if (xrCamera)
                    {
                        cmFirstPerson.transform.SetParent(xrCamera.transform);
                        cmFirstPerson.transform.localPosition = Vector3.zero;
                        cmFirstPerson.transform.localRotation = Quaternion.identity;
                    }
                }
            }

            yield return new WaitForSeconds(3f);

            // ===== СЕКЦИЯ 4: Переход в FirstPerson (9-10 сек) =====
            Debug.Log("[Trailer] Секция 4: Вид от первого лица");
            SetCameraActive(cmFirstPerson);
            yield return new WaitForSeconds(1f);

            // ===== СЕКЦИЯ 5: Управление Forklift (10-25 сек) =====
            Debug.Log("[Trailer] Секция 5: Управление погрузчиком");

            // Заводим двигатель
            if (forkliftVehicle)
            {
                forkliftVehicle.SetDrivingGearForward();
                forkliftVehicle.TurnOn();
                forkliftVehicle.EngineState(1);
                Debug.Log("[Trailer] Двигатель запущен");
            }

            yield return new WaitForSeconds(1f);

            // Поднимаем вилы
            if (forkliftControls)
            {
                forkliftControls.UpdateLift(1f);
                Debug.Log("[Trailer] Подъем вил");
            }
            yield return new WaitForSeconds(2f);

            // Наклон вил
            if (forkliftControls)
            {
                forkliftControls.UpdateTilt(0.8f);
                Debug.Log("[Trailer] Наклон вил");
            }
            yield return new WaitForSeconds(1.5f);

            // Выравниваем вилы
            if (forkliftControls)
            {
                forkliftControls.UpdateTilt(0f);
            }
            yield return new WaitForSeconds(1f);

            // Едем вперед
            if (forkliftVehicle)
            {
                forkliftVehicle.SetSpeed(0.4f);
                forkliftVehicle.SetDirection(0f);
                Debug.Log("[Trailer] Движение вперед");
            }
            yield return new WaitForSeconds(3f);

            // Поворот налево
            if (forkliftVehicle)
            {
                forkliftVehicle.SetDirection(-0.5f);
            }
            yield return new WaitForSeconds(1.5f);

            // Выравнивание
            if (forkliftVehicle)
            {
                forkliftVehicle.SetDirection(0f);
            }
            yield return new WaitForSeconds(1f);

            // Опускаем вилы (имитация подхвата паллеты)
            if (forkliftControls)
            {
                forkliftControls.UpdateLift(-1f);
                Debug.Log("[Trailer] Опускание вил");
            }
            yield return new WaitForSeconds(2f);

            // Поднимаем вилы с грузом
            if (forkliftControls)
            {
                forkliftControls.UpdateLift(1f);
            }
            yield return new WaitForSeconds(2f);

            // Едем назад + поворот
            if (forkliftVehicle)
            {
                forkliftVehicle.SetSpeed(-0.3f);
                forkliftVehicle.SetDirection(0.4f);
                Debug.Log("[Trailer] Движение назад с поворотом");
            }
            yield return new WaitForSeconds(3f);

            // Останавливаемся
            if (forkliftVehicle)
            {
                forkliftVehicle.SetSpeed(0f);
                forkliftVehicle.SetDirection(0f);
            }

            // Опускаем вилы
            if (forkliftControls)
            {
                forkliftControls.UpdateLift(-1f);
            }
            yield return new WaitForSeconds(1f);

            // Останавливаем звуки
            if (forkliftControls)
            {
                forkliftControls.UpdateLift(0f);
                forkliftControls.UpdateTilt(0f);
            }
            yield return new WaitForSeconds(0.5f);

            // ===== СЕКЦИЯ 6: Отдаление камеры (25-28 сек) =====
            Debug.Log("[Trailer] Секция 6: Отдаление");
            SetCameraActive(cmExitWide);
            yield return new WaitForSeconds(3f);

            // ===== ФИНАЛ =====
            Debug.Log("[Trailer] Съемка трейлера завершена!");
            isRecording = false;

            // Возвращаем Aerial камеру для финального кадра
            SetCameraActive(cmAerial);
        }

        private void SetCameraActive(GameObject activeCam)
        {
            // Отключаем все виртуальные камеры, включаем только активную
            if (cmAerial) cmAerial.SetActive(cmAerial == activeCam);
            if (cmApproach) cmApproach.SetActive(cmApproach == activeCam);
            if (cmPlayerEntry) cmPlayerEntry.SetActive(cmPlayerEntry == activeCam);
            if (cmFirstPerson) cmFirstPerson.SetActive(cmFirstPerson == activeCam);
            if (cmExitWide) cmExitWide.SetActive(cmExitWide == activeCam);
        }
    }
}