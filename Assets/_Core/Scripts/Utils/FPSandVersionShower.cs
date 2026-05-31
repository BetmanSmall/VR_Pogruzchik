using TMPro;
using UnityEngine;

public class FPSandVersionShower : MonoBehaviour
{
    // Поле для перетаскивания текстового элемента из инспектора
    [SerializeField] private TextMeshProUGUI versionText;
    // Поле для перетаскивания текстового элемента из инспектора
    [SerializeField] private TextMeshProUGUI fpsText;
    
    // Интервал обновления текста в секундах (по умолчанию 0.5 сек)
    [SerializeField] private float updateInterval = 0.5f;

    private int frameCount = 0;
    private float timeElapsed = 0f;

    private void Start()
    {
        if (versionText != null) versionText.text = "ver. " + Application.version;
    }

    private void Update()
    {
        // Увеличиваем счетчик кадров и накопленное время
        frameCount++;
        timeElapsed += Time.unscaledDeltaTime;

        // Проверяем, прошло ли достаточно времени для обновления
        if (timeElapsed >= updateInterval)
        {
            // Вычисляем FPS
            float fps = frameCount / timeElapsed;
            
            // Формируем текст с цветом в зависимости от производительности
            string fpsFormatted = FormatFpsString(fps);
            
            // Обновляем текст на экране
            fpsText.SetText(fpsFormatted);
            
            // Сбрасываем счетчики для следующего интервала
            frameCount = 0;
            timeElapsed = 0f;
        }
    }

    private string FormatFpsString(float fps)
    {
        string color;
        if (fps >= 50)
            color = "green";
        else if (fps >= 30)
            color = "yellow";
        else
            color = "red";

        // Возвращаем строку с HTML-тегом цвета
        return $"<color={color}>FPS: {Mathf.RoundToInt(fps)}</color>";
    }
}
