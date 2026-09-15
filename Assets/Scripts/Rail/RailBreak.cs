using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Точка разрыва (стыка) рельса вдоль сплайна
/// Указывает место, где рельс разрывается и добавляется стыковая накладка
/// </summary>
public class RailBreak : MonoBehaviour
{
    [Header("Позиция на сплайне")]
    [SerializeField]
    public float distanceAlongSpline = 0f; // расстояние от начала в метрах
    
    [SerializeField]
    [Range(0f, 1f)]
    public float splineT = 0f; // альтернативный параметр t (0-1)

    [Header("Параметры разрыва")]
    [SerializeField]
    public float breakWidth = 0.03f; // ширина зазора (3 см)

    [Header("Визуализация")]
    [SerializeField]
    public bool showGizmo = true;
    
    [SerializeField]
    public Color gizmoColor = Color.red;

    public bool enabled = true;

    private SplineContainer splineContainer;
    private int splineIndex = 0;

    private void OnEnable()
    {
        // Ищем SplineContainer в родительских объектах
        splineContainer = GetComponentInParent<SplineContainer>();
    }

    /// <summary>
    /// Получить расстояние вдоль сплайна
    /// </summary>
    public float GetDistance()
    {
        return distanceAlongSpline;
    }

    /// <summary>
    /// Получить параметр t на сплайне (0-1)
    /// </summary>
    public float GetSplineT()
    {
        if (splineContainer != null)
        {
            Spline spline = splineContainer.Splines[splineIndex];
            float length = spline.GetLength();
            return distanceAlongSpline / length;
        }
        return splineT;
    }

    /// <summary>
    /// Установить позицию по расстоянию
    /// </summary>
    public void SetDistanceAlongSpline(float distance)
    {
        distanceAlongSpline = distance;
        if (splineContainer != null)
        {
            Spline spline = splineContainer.Splines[splineIndex];
            splineT = distance / spline.GetLength();
        }
    }

    /// <summary>
    /// Получить позицию разрыва в мировых координатах
    /// </summary>
    public Vector3 GetPosition()
    {
        if (splineContainer != null)
        {
            Spline spline = splineContainer.Splines[splineIndex];
            float t = GetSplineT();
            
            Vector3 position = spline.EvaluatePosition(t);
            return position;
        }
        return transform.position;
    }

    /// <summary>
    /// Получить направление касательной в точке разрыва
    /// </summary>
    public Vector3 GetTangent()
    {
        if (splineContainer != null)
        {
            Spline spline = splineContainer.Splines[splineIndex];
            float t = GetSplineT();
            
            Vector3 tangent = spline.EvaluateTangent(t);
            return tangent.normalized;
        }
        return Vector3.forward;
    }

    /// <summary>
    /// Получить вектор вверх в точке разрыва
    /// </summary>
    public Vector3 GetUpVector()
    {
        return Vector3.up;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        SplineContainer container = GetComponentInParent<SplineContainer>();
        if (container == null) return;

        Spline spline = container.Splines[splineIndex];
        float t = GetSplineT();

        Vector3 position = spline.EvaluatePosition(t);
        Vector3 tangent = spline.EvaluateTangent(t).normalized;
        Vector3 upVector = Vector3.up;
        
        // Рисуем точку разрыва
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(position, 0.1f);
        
        // Рисуем линию разрыва
        Vector3 right = Vector3.Cross(tangent, upVector).normalized;
        float halfWidth = breakWidth * 0.5f;
        
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.5f);
        Gizmos.DrawLine(position - right * halfWidth, position + right * halfWidth);
    }
#endif
}
