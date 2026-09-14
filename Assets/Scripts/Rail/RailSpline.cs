using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Главный класс сплайна рельсового пути
/// Использует интерполяцию Catmull-Rom для плавных кривых через контрольные точки
/// Предоставляет позицию, направление и геометрические параметры в любой точке пути
/// </summary>
public class RailSpline : MonoBehaviour
{
    [SerializeField]
    private List<RailSplinePoint> controlPoints = new List<RailSplinePoint>();

    [Header("Параметры сплайна")]
    [SerializeField]
    [Range(0.01f, 1f)]
    private float segmentResolution = 0.1f; // шаг интерполяции между контрольными точками

    [SerializeField]
    private bool closedLoop = false; // замкнутый цикл (для кольцевых маршрутов)

    [Header("Кэширование")]
    [SerializeField]
    private bool useDistanceLUT = true; // использовать таблицу поиска расстояний для быстрого доступа
    
    private float cachedTotalLength = -1f;
    private List<float> distanceLUT; // таблица расстояний от начала до каждой точки выборки

    private void OnEnable()
    {
        // Ищем контрольные точки в дочерних объектах
        if (controlPoints.Count == 0)
        {
            RailSplinePoint[] childPoints = GetComponentsInChildren<RailSplinePoint>();
            controlPoints.AddRange(childPoints);
        }

        if (useDistanceLUT)
        {
            RecalculateDistanceLUT();
        }
    }

    /// <summary>
    /// Добавить контрольную точку в конец пути
    /// </summary>
    public void AddControlPoint(RailSplinePoint point)
    {
        controlPoints.Add(point);
        InvalidateCache();
    }

    /// <summary>
    /// Вставить контрольную точку в конкретную позицию
    /// </summary>
    public void InsertControlPoint(int index, RailSplinePoint point)
    {
        controlPoints.Insert(index, point);
        InvalidateCache();
    }

    /// <summary>
    /// Удалить контрольную точку
    /// </summary>
    public void RemoveControlPoint(RailSplinePoint point)
    {
        controlPoints.Remove(point);
        InvalidateCache();
    }

    /// <summary>
    /// Получить количество контрольных точек
    /// </summary>
    public int GetPointCount()
    {
        return controlPoints.Count;
    }

    /// <summary>
    /// Получить контрольную точку по индексу
    /// </summary>
    public RailSplinePoint GetControlPoint(int index)
    {
        if (index < 0 || index >= controlPoints.Count)
            return null;
        return controlPoints[index];
    }

    /// <summary>
    /// Получить позицию на сплайне по параметру t (0 до 1)
    /// t = 0 - начало пути, t = 1 - конец пути
    /// </summary>
    public Vector3 GetPosition(float t)
    {
        if (controlPoints.Count == 0) return Vector3.zero;
        if (controlPoints.Count == 1) return controlPoints[0].position;

        t = Mathf.Clamp01(t);

        // Находим два сегмента, между которыми лежит параметр t
        int segmentCount = closedLoop ? controlPoints.Count : controlPoints.Count - 1;
        float segmentLength = 1f / segmentCount;
        
        int currentSegment = Mathf.FloorToInt(t / segmentLength);
        if (currentSegment >= segmentCount) currentSegment = segmentCount - 1;

        float localT = (t - currentSegment * segmentLength) / segmentLength;

        return GetPositionOnSegment(currentSegment, localT);
    }

    /// <summary>
    /// Получить касательный вектор (направление) на сплайне в точке t
    /// </summary>
    public Vector3 GetTangent(float t)
    {
        if (controlPoints.Count < 2) return Vector3.forward;

        t = Mathf.Clamp01(t);

        int segmentCount = closedLoop ? controlPoints.Count : controlPoints.Count - 1;
        float segmentLength = 1f / segmentCount;
        
        int currentSegment = Mathf.FloorToInt(t / segmentLength);
        if (currentSegment >= segmentCount) currentSegment = segmentCount - 1;

        float localT = (t - currentSegment * segmentLength) / segmentLength;

        return GetTangentOnSegment(currentSegment, localT).normalized;
    }

    /// <summary>
    /// Получить параметр t по расстоянию от начала пути (в метрах)
    /// </summary>
    public float GetTByDistance(float distance)
    {
        float totalLength = GetTotalLength();
        
        if (totalLength <= 0) return 0;

        distance = Mathf.Clamp(distance, 0, totalLength);

        if (!useDistanceLUT || distanceLUT == null || distanceLUT.Count == 0)
        {
            return distance / totalLength;
        }

        // Бинарный поиск в таблице расстояний
        int index = distanceLUT.BinarySearch(distance);
        if (index >= 0)
        {
            return (float)index / (distanceLUT.Count - 1);
        }

        // Линейная интерполяция между двумя ближайшими точками
        index = ~index;
        if (index >= distanceLUT.Count) index = distanceLUT.Count - 1;
        if (index <= 0) return 0;

        float d0 = distanceLUT[index - 1];
        float d1 = distanceLUT[index];
        float t0 = (float)(index - 1) / (distanceLUT.Count - 1);
        float t1 = (float)index / (distanceLUT.Count - 1);

        float localT = (distance - d0) / (d1 - d0);
        return Mathf.Lerp(t0, t1, localT);
    }

    /// <summary>
    /// Получить полную длину пути в метрах
    /// </summary>
    public float GetTotalLength()
    {
        if (cachedTotalLength >= 0) return cachedTotalLength;

        cachedTotalLength = CalculateTotalLength();
        return cachedTotalLength;
    }

    /// <summary>
    /// Получить геометрические параметры (кривизна, уклон, крен) в точке t
    /// </summary>
    public RailTrackData GetTrackData(float t)
    {
        if (controlPoints.Count == 0) return new RailTrackData();

        t = Mathf.Clamp01(t);

        int segmentCount = closedLoop ? controlPoints.Count : controlPoints.Count - 1;
        float segmentLength = 1f / segmentCount;
        
        int currentSegment = Mathf.FloorToInt(t / segmentLength);
        if (currentSegment >= segmentCount) currentSegment = segmentCount - 1;

        float localT = (t - currentSegment * segmentLength) / segmentLength;

        // Интерполируем параметры между двумя контрольными точками
        RailSplinePoint p0 = controlPoints[currentSegment];
        RailSplinePoint p1 = controlPoints[(currentSegment + 1) % controlPoints.Count];

        RailTrackData data = new RailTrackData();
        data.position = GetPositionOnSegment(currentSegment, localT);
        data.tangent = GetTangentOnSegment(currentSegment, localT).normalized;
        data.horizontalRadius = Mathf.Lerp(p0.horizontalRadius, p1.horizontalRadius, localT);
        data.verticalRadius = Mathf.Lerp(p0.verticalRadius, p1.verticalRadius, localT);
        data.longitudinalGrade = Mathf.Lerp(p0.longitudinalGrade, p1.longitudinalGrade, localT);
        data.bankAngle = Mathf.Lerp(p0.bankAngle, p1.bankAngle, localT);
        data.gaugeWidth = Mathf.Lerp(p0.gaugeWidth, p1.gaugeWidth, localT);

        return data;
    }

    /// <summary>
    /// Пересчитать кэш расстояний
    /// </summary>
    private void RecalculateDistanceLUT()
    {
        distanceLUT = new List<float>();
        
        int steps = Mathf.RoundToInt(1f / segmentResolution);
        float distance = 0f;

        distanceLUT.Add(0f);

        Vector3 previousPos = GetPosition(0);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 currentPos = GetPosition(t);
            distance += Vector3.Distance(previousPos, currentPos);
            distanceLUT.Add(distance);
            previousPos = currentPos;
        }

        cachedTotalLength = distance;
    }

    /// <summary>
    /// Рассчитать полную длину пути
    /// </summary>
    private float CalculateTotalLength()
    {
        float length = 0f;
        int steps = Mathf.RoundToInt(1f / segmentResolution);

        Vector3 previousPos = GetPosition(0);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 currentPos = GetPosition(t);
            length += Vector3.Distance(previousPos, currentPos);
            previousPos = currentPos;
        }

        return length;
    }

    /// <summary>
    /// Получить позицию на конкретном сегменте сплайна
    /// </summary>
    private Vector3 GetPositionOnSegment(int segmentIndex, float localT)
    {
        if (controlPoints.Count < 2) return controlPoints[0].position;

        // Catmull-Rom интерполяция требует 4 точки
        int p0 = segmentIndex - 1;
        int p1 = segmentIndex;
        int p2 = segmentIndex + 1;
        int p3 = segmentIndex + 2;

        // Обработка границ
        if (closedLoop)
        {
            p0 = (p0 + controlPoints.Count) % controlPoints.Count;
            p1 = (p1 + controlPoints.Count) % controlPoints.Count;
            p2 = (p2 + controlPoints.Count) % controlPoints.Count;
            p3 = (p3 + controlPoints.Count) % controlPoints.Count;
        }
        else
        {
            p0 = Mathf.Clamp(p0, 0, controlPoints.Count - 1);
            p1 = Mathf.Clamp(p1, 0, controlPoints.Count - 1);
            p2 = Mathf.Clamp(p2, 0, controlPoints.Count - 1);
            p3 = Mathf.Clamp(p3, 0, controlPoints.Count - 1);
        }

        Vector3 v0 = controlPoints[p0].position;
        Vector3 v1 = controlPoints[p1].position;
        Vector3 v2 = controlPoints[p2].position;
        Vector3 v3 = controlPoints[p3].position;

        return CatmullRom(v0, v1, v2, v3, localT);
    }

    /// <summary>
    /// Получить касательный вектор на конкретном сегменте
    /// </summary>
    private Vector3 GetTangentOnSegment(int segmentIndex, float localT)
    {
        if (controlPoints.Count < 2) return Vector3.forward;

        int p0 = segmentIndex - 1;
        int p1 = segmentIndex;
        int p2 = segmentIndex + 1;
        int p3 = segmentIndex + 2;

        if (closedLoop)
        {
            p0 = (p0 + controlPoints.Count) % controlPoints.Count;
            p1 = (p1 + controlPoints.Count) % controlPoints.Count;
            p2 = (p2 + controlPoints.Count) % controlPoints.Count;
            p3 = (p3 + controlPoints.Count) % controlPoints.Count;
        }
        else
        {
            p0 = Mathf.Clamp(p0, 0, controlPoints.Count - 1);
            p1 = Mathf.Clamp(p1, 0, controlPoints.Count - 1);
            p2 = Mathf.Clamp(p2, 0, controlPoints.Count - 1);
            p3 = Mathf.Clamp(p3, 0, controlPoints.Count - 1);
        }

        Vector3 v0 = controlPoints[p0].position;
        Vector3 v1 = controlPoints[p1].position;
        Vector3 v2 = controlPoints[p2].position;
        Vector3 v3 = controlPoints[p3].position;

        return CatmullRomDerivative(v0, v1, v2, v3, localT);
    }

    /// <summary>
    /// Интерполяция Catmull-Rom между четырьмя точками
    /// </summary>
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    /// <summary>
    /// Производная интерполяции Catmull-Rom (касательный вектор)
    /// </summary>
    private static Vector3 CatmullRomDerivative(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;

        return 0.5f * (
            (-p0 + p2) +
            (4f * p0 - 10f * p1 + 8f * p2 - 2f * p3) * t +
            (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t2
        );
    }

    /// <summary>
    /// Инвалидировать кэш
    /// </summary>
    private void InvalidateCache()
    {
        cachedTotalLength = -1f;
        if (useDistanceLUT)
        {
            RecalculateDistanceLUT();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (controlPoints.Count < 2) return;

        // Рисуем сплайн
        int steps = Mathf.RoundToInt(1f / segmentResolution);
        Vector3 previousPos = GetPosition(0);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 currentPos = GetPosition(t);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(previousPos, currentPos);
            previousPos = currentPos;
        }

        // Рисуем контрольные точки
        Gizmos.color = Color.green;
        foreach (var point in controlPoints)
        {
            Gizmos.DrawSphere(point.position, 0.3f);
        }
    }
#endif
}

/// <summary>
/// Структура с геометрическими данными трека в конкретной точке пути
/// </summary>
public struct RailTrackData
{
    public Vector3 position;              // Позиция на рельсе
    public Vector3 tangent;               // Направление движения
    public float horizontalRadius;        // Радиус горизонтальной кривой
    public float verticalRadius;          // Радиус вертикальной кривой
    public float longitudinalGrade;       // Уклон пути (градусы)
    public float bankAngle;               // Крен вагонов на кривой
    public float gaugeWidth;              // Ширина колеи
}
