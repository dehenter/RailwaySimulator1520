using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Система для построения визуального меша рельсов вдоль сплайна
/// Экструдирует 2D сечение рельса по всей длине пути с учётом кривизны, уклона и крена
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class RailTrackBuilder : MonoBehaviour
{
    [SerializeField]
    private RailSpline railSpline;

    [Header("Сечение рельса")]
    [SerializeField]
    private Mesh railCrossSectionMesh;
    
    /// <summary>Вертексы сечения рельса в локальных координатах (XY плоскость)</summary>
    private Vector2[] crossSectionVertices;
    
    /// <summary>Индексы треугольников сечения</summary>
    private int[] crossSectionTriangles;

    [Header("Параметры построения")]
    [SerializeField]
    [Range(0.01f, 1f)]
    private float meshResolution = 0.1f; // шаг между сегментами вдоль пути
    
    [SerializeField]
    private bool autoRebuild = true;
    
    [SerializeField]
    private Material railMaterial;

    [Header("Физика")]
    [SerializeField]
    private bool createCollider = true;
    
    [SerializeField]
    private bool convexCollider = false;

    private Mesh generatedMesh;
    private MeshCollider meshCollider;
    private bool needsRebuild = true;

    private void OnEnable()
    {
        if (railSpline == null)
            railSpline = GetComponent<RailSpline>();

        meshCollider = GetComponent<MeshCollider>();

        if (autoRebuild)
            needsRebuild = true;
    }

    private void OnValidate()
    {
        if (autoRebuild)
            needsRebuild = true;
    }

    private void Update()
    {
        if (needsRebuild && railSpline != null && railCrossSectionMesh != null)
        {
            BuildTrackMesh();
            needsRebuild = false;
        }
    }

    /// <summary>
    /// Установить меш сечения рельса
    /// </summary>
    public void SetCrossSectionMesh(Mesh mesh)
    {
        railCrossSectionMesh = mesh;
        ExtractCrossSectionData();
        needsRebuild = true;
    }

    /// <summary>
    /// Пересчитать меш рельсов
    /// </summary>
    public void RebuildTrackMesh()
    {
        needsRebuild = true;
    }

    /// <summary>
    /// Построить меш трека
    /// </summary>
    private void BuildTrackMesh()
    {
        if (railSpline.GetPointCount() < 2)
            return;

        if (crossSectionVertices == null || crossSectionVertices.Length == 0)
        {
            Debug.LogError("Rail cross-section mesh not properly loaded!");
            return;
        }

        // Рассчитываем количество сегментов
        float totalLength = railSpline.GetTotalLength();
        int segmentCount = Mathf.Max(2, Mathf.RoundToInt(totalLength / meshResolution));

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // Для каждого сегмента вдоль пути
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            RailTrackData trackData = railSpline.GetTrackData(t);

            // Получаем локальный координатную систему в этой точке пути
            Vector3 forward = trackData.tangent;
            
            // Рассчитываем up с учётом уклона и крена
            Vector3 upDirection = CalculateUpDirection(forward, trackData.longitudinalGrade, trackData.bankAngle);
            Vector3 rightDirection = Vector3.Cross(forward, upDirection).normalized;
            upDirection = Vector3.Cross(rightDirection, forward).normalized;

            // Для каждой вершины сечения
            int vertexStartIndex = vertices.Count;

            for (int j = 0; j < crossSectionVertices.Length; j++)
            {
                Vector2 crossPos = crossSectionVertices[j];
                
                // Преобразуем 2D сечение (XY) в 3D пространство
                // X сечения = right (ширина колеи)
                // Y сечения = up (высота профиля)
                Vector3 vertexPos = trackData.position 
                    + rightDirection * crossPos.x 
                    + upDirection * crossPos.y;

                vertices.Add(vertexPos);
                uvs.Add(new Vector2(crossPos.x, t)); // UV: горизонтально по сечению, вертикально по пути
            }

            // Соединяем с предыдущим сегментом треугольниками
            if (i > 0)
            {
                int prevSegmentStart = vertexStartIndex - crossSectionVertices.Length;

                for (int j = 0; j < crossSectionTriangles.Length; j += 3)
                {
                    int triA = crossSectionTriangles[j];
                    int triB = crossSectionTriangles[j + 1];
                    int triC = crossSectionTriangles[j + 2];

                    // Два треугольника для каждого четырёхугольника
                    // Используем quad между текущим и предыдущим сегментом

                    // Находим edges текущего треугольника
                    AddExtrудedTriangle(triangles, prevSegmentStart, vertexStartIndex,
                        triA, triB, triC);
                }
            }
        }

        // Создаём меш
        if (generatedMesh == null)
            generatedMesh = new Mesh();

        generatedMesh.Clear();
        generatedMesh.name = "RailTrack";
        generatedMesh.vertices = vertices.ToArray();
        generatedMesh.triangles = triangles.ToArray();
        generatedMesh.uv = uvs.ToArray();
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        // Применяем меш к компоненту
        GetComponent<MeshFilter>().mesh = generatedMesh;

        // Обновляем коллайдер
        if (createCollider && meshCollider != null)
        {
            meshCollider.convex = convexCollider;
            meshCollider.mesh = null;
            meshCollider.mesh = generatedMesh;
        }

        // Применяем материал
        if (railMaterial != null)
        {
            GetComponent<MeshRenderer>().material = railMaterial;
        }
    }

    /// <summary>
    /// Экструдировать треугольник сечения между двумя сегментами пути
    /// </summary>
    private void AddExtrудedTriangle(List<int> triangles, int prevSegmentStart, int currentSegmentStart,
        int triA, int triB, int triC)
    {
        // Для каждого ребра треугольника в сечении создаём quad при экструзии
        AddQuad(triangles, prevSegmentStart + triA, prevSegmentStart + triB,
            currentSegmentStart + triA, currentSegmentStart + triB);

        AddQuad(triangles, prevSegmentStart + triB, prevSegmentStart + triC,
            currentSegmentStart + triB, currentSegmentStart + triC);

        AddQuad(triangles, prevSegmentStart + triC, prevSegmentStart + triA,
            currentSegmentStart + triC, currentSegmentStart + triA);
    }

    /// <summary>
    /// Добавить четырёхугольник (два треугольника) в список
    /// </summary>
    private void AddQuad(List<int> triangles, int v0, int v1, int v2, int v3)
    {
        // Первый треугольник
        triangles.Add(v0);
        triangles.Add(v1);
        triangles.Add(v2);

        // Второй треугольник
        triangles.Add(v1);
        triangles.Add(v3);
        triangles.Add(v2);
    }

    /// <summary>
    /// Рассчитать направление вверх с учётом уклона и крена
    /// </summary>
    private Vector3 CalculateUpDirection(Vector3 forward, float longitudinalGrade, float bankAngle)
    {
        // Уклон наклоняет вверх вдоль направления движения
        Vector3 gradeAxis = Vector3.Cross(Vector3.up, forward).normalized;
        Quaternion gradeRotation = Quaternion.AngleAxis(longitudinalGrade, gradeAxis);
        
        // Крен наклоняет относительно направления движения
        Quaternion bankRotation = Quaternion.AngleAxis(bankAngle, forward);

        Vector3 up = Vector3.up;
        up = gradeRotation * up;
        up = bankRotation * up;

        return up.normalized;
    }

    /// <summary>
    /// Извлечь данные из 2D меша сечения рельса
    /// </summary>
    private void ExtractCrossSectionData()
    {
        if (railCrossSectionMesh == null)
        {
            Debug.LogError("Rail cross-section mesh is not assigned!");
            return;
        }

        Vector3[] mesh3DVertices = railCrossSectionMesh.vertices;
        crossSectionVertices = new Vector2[mesh3DVertices.Length];

        // Преобразуем 3D вертексы в 2D (берём только X и Y, предполагая что меш в XY плоскости)
        for (int i = 0; i < mesh3DVertices.Length; i++)
        {
            crossSectionVertices[i] = new Vector2(mesh3DVertices[i].x, mesh3DVertices[i].y);
        }

        crossSectionTriangles = railCrossSectionMesh.triangles;

        Debug.Log($"Loaded cross-section mesh: {crossSectionVertices.Length} vertices, {crossSectionTriangles.Length / 3} triangles");
    }

#if UNITY_EDITOR
    [ContextMenu("Build Track Mesh")]
    private void ContextBuildTrackMesh()
    {
        if (railCrossSectionMesh != null)
        {
            ExtractCrossSectionData();
        }
        BuildTrackMesh();
    }

    [ContextMenu("Clear Track Mesh")]
    private void ContextClearTrackMesh()
    {
        if (generatedMesh != null)
        {
            DestroyImmediate(generatedMesh);
            generatedMesh = null;
        }

        GetComponent<MeshFilter>().mesh = null;

        if (meshCollider != null)
        {
            meshCollider.mesh = null;
        }
    }
#endif
}
