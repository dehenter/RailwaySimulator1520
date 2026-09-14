using UnityEngine;

/// <summary>
/// Система для привязки колёс поезда к рельсам
/// Обеспечивает движение поезда вдоль сплайна с физическим взаимодействием
/// </summary>
public class TrainWheelConstraint : MonoBehaviour
{
    [Header("Ссылки")]
    [SerializeField]
    private RailSpline railSpline;

    [SerializeField]
    private Rigidbody trainRigidbody;

    [Header("Параметры колёс")]
    [SerializeField]
    private float wheelRadius = 0.5f;
    
    [SerializeField]
    private float wheelBase = 2.0f; // расстояние между передней и задней осью
    
    [SerializeField]
    private Vector3 frontWheelLocalPos = new Vector3(0, 0, 1); // локальная позиция передней оси относительно центра тяжести
    
    [SerializeField]
    private Vector3 rearWheelLocalPos = new Vector3(0, 0, -1); // локальная позиция задней оси

    [Header("Текущее состояние")]
    [SerializeField]
    private float distanceAlongTrack = 0f; // расстояние вдоль пути в метрах
    
    private float trackParameter = 0f; // параметр t на сплайне (0 до 1)

    [Header("Физика")]
    [SerializeField]
    private float stiffness = 1000f; // жёсткость привязки к рельсам
    
    [SerializeField]
    private float damping = 50f; // демпфирование
    
    [SerializeField]
    private float maxLateralForce = 10000f; // максимальная боковая сила от рельсов
    
    [SerializeField]
    private float maxVerticalForce = 50000f; // максимальная вертикальная сила

    [Header("Ограничения")]
    [SerializeField]
    private float maxLateralDeviation = 0.1f; // максимальное отклонение от рельса
    
    [SerializeField]
    private float maxVerticalDeviation = 0.2f; // максимальное отклонение по высоте

    private Vector3 previousPosition;
    private Vector3 previousVelocity;
    private bool isInitialized = false;

    private void OnEnable()
    {
        if (railSpline == null)
            railSpline = GetComponentInParent<RailSpline>();

        if (trainRigidbody == null)
            trainRigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        previousPosition = transform.position;
        isInitialized = true;
    }

    private void FixedUpdate()
    {
        if (!isInitialized || railSpline == null || trainRigidbody == null)
            return;

        UpdateTrackPosition();
        ApplyRailConstraints();
        ApplyOrientationConstraint();
    }

    /// <summary>
    /// Обновить позицию поезда вдоль пути на основе текущего расстояния
    /// </summary>
    private void UpdateTrackPosition()
    {
        float totalLength = railSpline.GetTotalLength();
        
        // Получаем текущую скорость поезда вдоль пути
        Vector3 velocity = trainRigidbody.velocity;
        RailTrackData trackData = railSpline.GetTrackData(trackParameter);
        
        float forwardSpeed = Vector3.Dot(velocity, trackData.tangent);
        
        // Обновляем расстояние
        distanceAlongTrack += forwardSpeed * Time.fixedDeltaTime;
        
        // Зажимаем или зацикливаем расстояние
        if (distanceAlongTrack < 0) distanceAlongTrack = 0;
        if (distanceAlongTrack > totalLength) distanceAlongTrack = totalLength;
        
        // Конвертируем расстояние в параметр t
        trackParameter = distanceAlongTrack / totalLength;
    }

    /// <summary>
    /// Применить ограничения рельсов (боковые и вертикальные силы)
    /// </summary>
    private void ApplyRailConstraints()
    {
        RailTrackData trackData = railSpline.GetTrackData(trackParameter);
        
        // Целевая позиция (центр поезда на рельсе)
        Vector3 targetPosition = trackData.position;
        
        // Получаем текущее отклонение от рельса
        Vector3 deviation = transform.position - targetPosition;
        
        // Локальный координатная система рельса
        Vector3 forward = trackData.tangent;
        Vector3 upDirection = CalculateUpDirection(forward, trackData.longitudinalGrade, trackData.bankAngle);
        Vector3 rightDirection = Vector3.Cross(forward, upDirection).normalized;
        upDirection = Vector3.Cross(rightDirection, forward).normalized;
        
        // Компоненты отклонения
        float lateralDeviation = Vector3.Dot(deviation, rightDirection);
        float verticalDeviation = Vector3.Dot(deviation, upDirection);
        float forwardDeviation = Vector3.Dot(deviation, forward);
        
        // Ограничиваем отклонение
        lateralDeviation = Mathf.Clamp(lateralDeviation, -maxLateralDeviation, maxLateralDeviation);
        verticalDeviation = Mathf.Clamp(verticalDeviation, -maxVerticalDeviation, maxVerticalDeviation);
        
        // Рассчитываем коррекционные силы
        Vector3 correctionForce = Vector3.zero;
        
        // Боковая сила (рельсы не позволяют боковое отклонение)
        if (Mathf.Abs(lateralDeviation) > 0.01f)
        {
            float lateralForce = -lateralDeviation * stiffness;
            lateralForce = Mathf.Clamp(lateralForce, -maxLateralForce, maxLateralForce);
            correctionForce += rightDirection * lateralForce;
            
            // Добавляем демпфирование
            Vector3 lateralVelocity = Vector3.Project(trainRigidbody.velocity, rightDirection);
            correctionForce -= lateralVelocity * damping;
        }
        
        // Вертикальная сила (рельсы поддерживают вес)
        if (Mathf.Abs(verticalDeviation) > 0.01f)
        {
            float verticalForce = -verticalDeviation * stiffness;
            verticalForce = Mathf.Clamp(verticalForce, -maxVerticalForce, maxVerticalForce);
            correctionForce += upDirection * verticalForce;
            
            // Добавляем демпфирование
            Vector3 verticalVelocity = Vector3.Project(trainRigidbody.velocity, upDirection);
            correctionForce -= verticalVelocity * damping;
        }
        
        // Применяем силу
        trainRigidbody.AddForce(correctionForce, ForceMode.Force);
    }

    /// <summary>
    /// Применить ориентацию поезда в соответствии с направлением рельса
    /// </summary>
    private void ApplyOrientationConstraint()
    {
        RailTrackData trackData = railSpline.GetTrackData(trackParameter);
        
        Vector3 forward = trackData.tangent;
        Vector3 upDirection = CalculateUpDirection(forward, trackData.longitudinalGrade, trackData.bankAngle);
        
        // Целевой кватернион
        Quaternion targetRotation = Quaternion.LookRotation(forward, upDirection);
        
        // Плавная интерполяция текущей ориентации к целевой
        float rotationSpeed = 5f;
        trainRigidbody.rotation = Quaternion.Lerp(trainRigidbody.rotation, targetRotation, 
            rotationSpeed * Time.fixedDeltaTime);
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
    /// Получить текущую позицию вдоль пути в метрах
    /// </summary>
    public float GetDistanceAlongTrack()
    {
        return distanceAlongTrack;
    }

    /// <summary>
    /// Установить позицию вдоль пути в метрах
    /// </summary>
    public void SetDistanceAlongTrack(float distance)
    {
        distanceAlongTrack = distance;
        trackParameter = distance / railSpline.GetTotalLength();
    }

    /// <summary>
    /// Получить текущий параметр t на сплайне (0 до 1)
    /// </summary>
    public float GetTrackParameter()
    {
        return trackParameter;
    }

    /// <summary>
    /// Получить текущие данные трека в позиции поезда
    /// </summary>
    public RailTrackData GetCurrentTrackData()
    {
        return railSpline.GetTrackData(trackParameter);
    }

    /// <summary>
    /// Получить текущее боковое отклонение от рельса (м)
    /// </summary>
    public float GetLateralDeviation()
    {
        RailTrackData trackData = railSpline.GetTrackData(trackParameter);
        Vector3 forward = trackData.tangent;
        Vector3 upDirection = CalculateUpDirection(forward, trackData.longitudinalGrade, trackData.bankAngle);
        Vector3 rightDirection = Vector3.Cross(forward, upDirection).normalized;
        
        Vector3 deviation = transform.position - trackData.position;
        return Vector3.Dot(deviation, rightDirection);
    }

    /// <summary>
    /// Получить текущее вертикальное отклонение от рельса (м)
    /// </summary>
    public float GetVerticalDeviation()
    {
        RailTrackData trackData = railSpline.GetTrackData(trackParameter);
        Vector3 forward = trackData.tangent;
        Vector3 upDirection = CalculateUpDirection(forward, trackData.longitudinalGrade, trackData.bankAngle);
        
        Vector3 deviation = transform.position - trackData.position;
        return Vector3.Dot(deviation, upDirection);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || railSpline == null)
            return;

        RailTrackData trackData = railSpline.GetTrackData(trackParameter);
        
        Vector3 forward = trackData.tangent;
        Vector3 upDirection = CalculateUpDirection(forward, trackData.longitudinalGrade, trackData.bankAngle);
        Vector3 rightDirection = Vector3.Cross(forward, upDirection).normalized;

        // Рисуем целевую позицию
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(trackData.position, 0.2f);
        
        // Рисуем оси рельса
        Gizmos.color = Color.red;
        Gizmos.DrawLine(trackData.position, trackData.position + rightDirection * 1f);
        
        Gizmos.color = Color.green;
        Gizmos.DrawLine(trackData.position, trackData.position + upDirection * 1f);
        
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(trackData.position, trackData.position + forward * 1f);
        
        // Рисуем отклонение
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(trackData.position, transform.position);
    }
#endif
}
