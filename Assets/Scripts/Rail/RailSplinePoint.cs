using UnityEngine;

/// <summary>
/// Контрольная точка сплайна рельсового пути
/// Содержит геометрические параметры трека в данной точке
/// </summary>
public class RailSplinePoint : MonoBehaviour
{
    [Header("Базовые параметры")]
    /// <summary>Позиция точки на пути</summary>
    public Vector3 position;

    [Header("Кривизна - Горизонтальная плоскость (повороты влево-вправо)")]
    /// <summary>Радиус горизонтальной кривой в метрах. Бесконечность = прямая</summary>
    [Min(0.1f)]
    public float horizontalRadius = float.PositiveInfinity;
    
    /// <summary>Направление поворота: 1 = влево, -1 = вправо, 0 = прямая</summary>
    [Range(-1, 1)]
    public int horizontalTurnDirection = 0;

    [Header("Кривизна - Вертикальная плоскость (горбы и впадины)")]
    /// <summary>Радиус вертикальной кривой в метрах. Бесконечность = прямая</summary>
    [Min(0.1f)]
    public float verticalRadius = float.PositiveInfinity;

    [Header("Уклон пути")]
    /// <summary>Уклон пути в градусах (положительное = подъём, отрицательное = спуск)</summary>
    [Range(-45, 45)]
    public float longitudinalGrade = 0f;

    [Header("Крен (наклон в поворотах)")]
    /// <summary>Угол крена в градусах на кривых (автоматический расчёт или ручной)</summary>
    [Range(-45, 45)]
    public float bankAngle = 0f;
    
    /// <summary>Использовать автоматический расчёт крена по формуле физики</summary>
    public bool autoCalculateBank = true;
    
    /// <summary>Скорость расчётного поезда для автоматического крена (км/ч)</summary>
    [Min(1)]
    public float designSpeed = 80f;

    [Header("Расстояния между рельсами")]
    /// <summary>Ширина колеи (1520 мм для советской колеи)</summary>
    [Range(0.5f, 2f)]
    public float gaugeWidth = 1.52f;

    [Header("Визуализация в редакторе")]
    public bool showGizmos = true;
    public float gizmoSize = 0.5f;
    public Color gizmoColor = Color.green;

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        position = transform.position;
        
        // Рисуем точку
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(position, gizmoSize);

        // Рисуем индекс
        Vector3 labelPos = position + Vector3.up * gizmoSize * 2;
    }

    /// <summary>
    /// Автоматический расчёт крена по формуле для железной дороги
    /// Крен = arctan(v² / (g * R))
    /// где v - скорость, g - ускорение свободного падения, R - радиус кривой
    /// </summary>
    public void CalculateBankAngle()
    {
        if (!autoCalculateBank || horizontalRadius >= float.PositiveInfinity)
        {
            bankAngle = 0f;
            return;
        }

        // Конвертируем скорость из км/ч в м/с
        float speedMs = designSpeed / 3.6f;
        
        // Гравитация
        float g = 9.81f;
        
        // Формула крена: tan(θ) = v² / (g * R)
        float tanBank = (speedMs * speedMs) / (g * horizontalRadius);
        bankAngle = Mathf.Atan(tanBank) * Mathf.Rad2Deg;
        
        // Ограничиваем крен до 45 градусов
        bankAngle = Mathf.Clamp(bankAngle, -45f, 45f);
        
        // Учитываем направление поворота
        if (horizontalTurnDirection == -1)
            bankAngle = -bankAngle;
    }

    /// <summary>
    /// Получить локальное направление вперёд в этой точке сплайна
    /// </summary>
    public Vector3 GetForwardDirection()
    {
        return transform.forward;
    }

    /// <summary>
    /// Получить локальное направление вправо в этой точке сплайна
    /// </summary>
    public Vector3 GetRightDirection()
    {
        return transform.right;
    }

    /// <summary>
    /// Получить локальное направление вверх с учётом крена
    /// </summary>
    public Vector3 GetUpDirection()
    {
        return transform.up;
    }

    private void OnValidate()
    {
        position = transform.position;
        
        if (autoCalculateBank && Application.isPlaying == false)
        {
            CalculateBankAngle();
        }
    }
}
