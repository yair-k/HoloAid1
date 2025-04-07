using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using HoloToolkit.Unity;
using HoloToolkit.Unity.InputModule;
using HoloToolkit.Unity.SpatialMapping;

[RequireComponent(typeof(AudioSource))]
public class ObstacleBeaconScript : MonoBehaviour {

    [Header("Core References")]
    [SerializeField] private GameObject obstacleBeaconManager;
    [SerializeField] private GameObject textManager;
    [SerializeField] private GameObject beaconManager;

    [Header("Detection Modes")]
    [SerializeField] private bool spotlightMode = true;
    [SerializeField] private bool proximityMode = false;
    [SerializeField] private bool obstacleMode = false;
    [SerializeField] private bool distanceRefresh = true;
    [SerializeField] private bool enableGPUAcceleration = true;
    
    [Header("Obstacle Detection Parameters")]
    [SerializeField] [Range(10f, 60f)] private float coneCastAngle = 30f;
    [SerializeField] [Range(0.2f, 3.0f)] private float spotlightSize = 1f;
    [SerializeField] [Range(0.5f, 10f)] private float obstacleRefreshDistance = 2f;
    [SerializeField] [Range(2f, 30f)] private float obstacleRefreshTime = 8f;
    [SerializeField] [Range(5f, 30f)] private float depth = 10f;
    [SerializeField] [Range(10, 50)] private int maxBeacons = 20;
    [SerializeField] [Range(0.05f, 0.5f)] private float deviation = 0.2f;
    
    [Header("Beacon Prefabs")]
    [SerializeField] private GameObject obstacleBeaconLog;
    [SerializeField] private GameObject obstacleBeaconLin;
    [SerializeField] private Material obstacleMaterial;
    [SerializeField] private GameObject wallBeaconLog;
    [SerializeField] private GameObject wallBeaconLin;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material dynamicObstacleMaterial;

    [Header("Advanced Settings")]
    [SerializeField] private bool useSpatialUnderstanding = true;
    [SerializeField] private bool useAdaptiveSampling = true;
    [SerializeField] private bool adaptiveBeaconPlacement = true;
    [SerializeField] [Range(0.05f, 1.0f)] private float minimumInterBeaconDistance = 0.3f;
    [SerializeField] private LayerMask obstacleDetectionLayers = Physics.DefaultRaycastLayers;
    [SerializeField] [Range(1, 8)] private int multithreadingLevel = 4;
    [SerializeField] private bool enableDynamicObstacleTracking = true;
    [SerializeField] private bool usePathPrediction = true;
    [SerializeField] private float pathPredictionWeight = 0.7f;
    
    [HideInInspector] public GameObject obstacleBeacon;
    [HideInInspector] public GameObject wallBeacon;

    private Vector3 startPos;
    private float currentDistance;
    private float timeSinceLastRefresh = 0f;
    private Physics physicsCaster;
    private List<Vector3> placedBeaconPositions = new List<Vector3>();
    private Camera mainCamera;
    private SpatialUnderstanding spatialUnderstanding;
    private float adaptiveRefreshTime;
    private Vector3 lastHeadPosition;
    private Quaternion lastHeadRotation;
    private float headMovementThreshold = 0.5f;
    private float headRotationThreshold = 15f;
    private bool isInitialized = false;
    
    private readonly Dictionary<GameObject, float> beaconImportanceScores = new Dictionary<GameObject, float>();
    private Dictionary<Vector3, RaycastRequest> cachedRayRequests = new Dictionary<Vector3, RaycastRequest>();
    private Dictionary<string, DateTime> dynamicObstacleDetectionTimes = new Dictionary<string, DateTime>();
    private Queue<Vector3> recentHeadPositions = new Queue<Vector3>();
    private Vector3 predictedMovementDirection = Vector3.zero;
    private CancellationTokenSource refreshCancellationToken;
    private ComputeShader raycastComputeShader;
    private int raycastKernel;
    private bool gpuAccelerationAvailable = false;
    
    private const float WALL_NORMAL_THRESHOLD = 0.8f;
    private const float FLOOR_NORMAL_THRESHOLD = 0.8f;
    private const float CEILING_NORMAL_THRESHOLD = -0.8f;
    private const float DYNAMIC_OBSTACLE_TRACKING_TIMEOUT = 5.0f;
    private const float PATH_PREDICTION_UPDATE_INTERVAL = 0.5f;
    private const int PATH_PREDICTION_SAMPLE_COUNT = 10;
    private const int GPU_BATCH_SIZE = 64;
    
    private void Awake() 
    {
        physicsCaster = new Physics();
        
        if (obstacleBeacon == null)
            obstacleBeacon = proximityMode ? obstacleBeaconLog : obstacleBeaconLin;
            
        if (wallBeacon == null)
            wallBeacon = proximityMode ? wallBeaconLog : wallBeaconLin;
            
        if (beaconManager == null)
            beaconManager = new GameObject("BeaconManager");
            
        adaptiveRefreshTime = obstacleRefreshTime;
        
        InitializeGPUAcceleration();
    }

    private void InitializeGPUAcceleration()
    {
        if (enableGPUAcceleration)
        {
            try
            {
                raycastComputeShader = Resources.Load<ComputeShader>("ObstacleDetection/RaycastCompute");
                if (raycastComputeShader != null)
                {
                    raycastKernel = raycastComputeShader.FindKernel("RaycastKernel");
                    gpuAccelerationAvailable = true;
                    Debug.Log("GPU acceleration for obstacle detection initialized");
                }
                else
                {
                    Debug.LogWarning("GPU acceleration compute shader not found");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize GPU acceleration: {ex.Message}");
                gpuAccelerationAvailable = false;
            }
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
        if (useSpatialUnderstanding)
        {
            spatialUnderstanding = SpatialUnderstanding.Instance;
        }
        
        lastHeadPosition = mainCamera.transform.position;
        lastHeadRotation = mainCamera.transform.rotation;
        
        StartCoroutine(InitializeSystem());
        
        if (usePathPrediction)
        {
            InvokeRepeating("UpdatePathPrediction", 0.5f, PATH_PREDICTION_UPDATE_INTERVAL);
        }
    }

    private void OnDestroy()
    {
        if (refreshCancellationToken != null)
        {
            refreshCancellationToken.Cancel();
            refreshCancellationToken.Dispose();
        }
        
        CancelInvoke();
    }

    private IEnumerator InitializeSystem()
    {
        yield return new WaitForSeconds(1.0f);
        
        if (useSpatialUnderstanding && spatialUnderstanding != null)
        {
            while (!spatialUnderstanding.AllowSpatialUnderstanding)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        isInitialized = true;
        Debug.Log("ObstacleBeaconScript: System initialized");
    }
    
    private void UpdatePathPrediction()
    {
        if (recentHeadPositions.Count >= PATH_PREDICTION_SAMPLE_COUNT)
        {
            recentHeadPositions.Dequeue();
        }
        
        recentHeadPositions.Enqueue(mainCamera.transform.position);
        
        if (recentHeadPositions.Count >= 3)
        {
            var posArray = recentHeadPositions.ToArray();
            Vector3 averageDirection = Vector3.zero;
            
            for (int i = 1; i < posArray.Length; i++)
            {
                Vector3 segmentDirection = (posArray[i] - posArray[i-1]).normalized;
                float weight = (float)i / posArray.Length;
                averageDirection += segmentDirection * weight;
            }
            
            if (averageDirection.magnitude > 0.01f)
            {
                averageDirection.Normalize();
                predictedMovementDirection = Vector3.Lerp(predictedMovementDirection, averageDirection, 0.3f);
            }
        }
    }
    
    void Update()
    {
        if (!isInitialized)
            return;
            
        Vector3 headPosition = mainCamera.transform.position;
        Quaternion headRotation = mainCamera.transform.rotation;
            
        if (obstacleMode)
        {
            if (distanceRefresh)
            {
                currentDistance = Vector3.Distance(headPosition, startPos);
                if (currentDistance >= obstacleRefreshDistance)
                {
                    startPos = headPosition;
                    RefreshBeacons();
                }
            }
            else
            {
                timeSinceLastRefresh += Time.deltaTime;
                
                if (useAdaptiveSampling)
                {
                    float headMovement = Vector3.Distance(headPosition, lastHeadPosition);
                    float headRotationDelta = Quaternion.Angle(headRotation, lastHeadRotation);
                    
                    if (headMovement > headMovementThreshold || headRotationDelta > headRotationThreshold)
                    {
                        adaptiveRefreshTime = Mathf.Max(obstacleRefreshTime * 0.5f, 2f);
                    }
                    else
                    {
                        adaptiveRefreshTime = obstacleRefreshTime;
                    }
                }
                
                if (timeSinceLastRefresh >= adaptiveRefreshTime)
                {
                    RefreshBeacons();
                    timeSinceLastRefresh = 0;
                }
            }
        }

        if (spotlightMode)
        {
            MuteAllObstacleBeacons();
            
            RaycastHit[] spotlightHits = physicsCaster.ConeCastAll(
                headPosition, 
                spotlightSize, 
                mainCamera.transform.forward, 
                depth, 
                coneCastAngle,
                obstacleDetectionLayers);

            ProcessSpotlightHits(spotlightHits);
        }
        
        lastHeadPosition = headPosition;
        lastHeadRotation = headRotation;
        
        if (enableDynamicObstacleTracking)
        {
            CleanupExpiredDynamicObstacles();
        }
    }

    private void CleanupExpiredDynamicObstacles()
    {
        var expiredKeys = new List<string>();
        foreach (var kvp in dynamicObstacleDetectionTimes)
        {
            if ((DateTime.Now - kvp.Value).TotalSeconds > DYNAMIC_OBSTACLE_TRACKING_TIMEOUT)
            {
                expiredKeys.Add(kvp.Key);
            }
        }
        
        foreach (var key in expiredKeys)
        {
            dynamicObstacleDetectionTimes.Remove(key);
        }
    }

    private async void RefreshBeacons()
    {
        if (refreshCancellationToken != null)
        {
            refreshCancellationToken.Cancel();
            refreshCancellationToken.Dispose();
        }
        
        refreshCancellationToken = new CancellationTokenSource();
        
        try
        {
            DeleteBeacons();
            await ConeShotAsync(refreshCancellationToken.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Beacon refresh operation was canceled");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error refreshing beacons: {ex.Message}");
        }
    }

    private void ProcessSpotlightHits(RaycastHit[] hits)
    {
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null) continue;
            
            if (hit.transform.CompareTag("Obstacle Beacon") || hit.transform.CompareTag("Wall Beacon"))
            {
                AudioSource beaconAudio = hit.transform.GetComponent<AudioSource>();
                if (beaconAudio != null)
                {
                    beaconAudio.volume = 1f;
                    
                    float distanceFactor = 1f - Mathf.Clamp01(hit.distance / depth);
                    Vector3 beaconDir = (hit.transform.position - mainCamera.transform.position).normalized;
                    float dotProduct = Vector3.Dot(mainCamera.transform.forward, beaconDir);
                    float centralityFactor = Mathf.Clamp01((dotProduct + 1f) / 2f);
                    
                    beaconAudio.volume = Mathf.Lerp(0.7f, 1.2f, centralityFactor * distanceFactor);
                }
                
                Renderer renderer = hit.transform.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (hit.transform.CompareTag("Obstacle Beacon"))
                    {
                        renderer.material = obstacleMaterial;
                    }
                    else if (hit.transform.CompareTag("Wall Beacon"))
                    {
                        renderer.material = wallMaterial;
                    }
                }
            }
        }
    }

    public void ObstaclesOn()
    {
        if (obstacleMode) return;
        
        obstacleMode = true;
        
        // Initial scan
        ConeShot();
        
        if (distanceRefresh)
        {
            startPos = mainCamera.transform.position;
        }
        else 
        { 
            timeSinceLastRefresh = 0; 
        }
        
        textManager.GetComponent<TextToSpeechGoogle>().playTextGoogle("Obstacles on");
    }

    public void ObstaclesOff()
    {
        obstacleMode = false;
        DeleteBeacons();
        
        if (!distanceRefresh)
        {
            timeSinceLastRefresh = 0;
        }
        
        textManager.GetComponent<TextToSpeechGoogle>().playTextGoogle("Obstacles off");
    }

    public void SpotlightOn()
    {
        spotlightMode = true;
        MuteAllObstacleBeacons();
        
        if (GameObject.Find("Spotlight Text") != null)
        {
            GameObject.Find("Spotlight Text").GetComponent<TextMesh>().text = "Spotlight Mode: On";
        }
        
        textManager.GetComponent<TextToSpeechGoogle>().playTextGoogle("Spotlight mode on");
    }

    public void SpotlightOff()
    {
        spotlightMode = false;
        UnmuteAllObstacleBeacons();
        
        if (GameObject.Find("Spotlight Text") != null)
        {
            GameObject.Find("Spotlight Text").GetComponent<TextMesh>().text = "Spotlight Mode: Off";
        }
        
        textManager.GetComponent<TextToSpeechGoogle>().playTextGoogle("Spotlight mode off");
    }

    public void ProximityModeOn()
    {
        proximityMode = true;
        obstacleBeacon = obstacleBeaconLog;
        wallBeacon = wallBeaconLog;
        
        if (GameObject.Find("Proximity Text") != null)
        {
            GameObject.Find("Proximity Text").GetComponent<TextMesh>().text = "Proximity Mode: On";
        }
        
        if (obstacleMode)
        {
            DeleteBeacons();
            ConeShot();
        }
        
        textManager.GetComponent<TextToSpeechGoogle>().playTextGoogle("Proximity mode on");
    }

    public void ProximityModeOff()
    {
        proximityMode = false;
        obstacleBeacon = obstacleBeaconLin;
        wallBeacon = wallBeaconLin;
        
        if (GameObject.Find("Proximity Text") != null)
        {
            GameObject.Find("Proximity Text").GetComponent<TextMesh>().text = "Proximity Mode: Off";
        }
        
        if (obstacleMode)
        {
            DeleteBeacons();
            ConeShot();
        }
        
        textManager.GetComponent<TextToSpeechGoogle>().playTextGoogle("Proximity mode off");
    }

    public void SingleShot()
    {
        Vector3 origin = mainCamera.transform.position;
        Vector3 direction = mainCamera.transform.forward;
        RaycastHit hit;

        if (Physics.Raycast(origin, direction, out hit, 30.0f, obstacleDetectionLayers))
        {
            ProcessHitForBeaconPlacement(hit);
        }
    }

    public void ConeShot()
    {
        placedBeaconPositions.Clear();
        beaconImportanceScores.Clear();
        Vector3 origin = mainCamera.transform.position;
        Vector3 forward = mainCamera.transform.forward;
        
        List<RaycastRequest> rayRequests = new List<RaycastRequest>();
        GenerateRayRequests(ref rayRequests, origin, forward);
        
        if (gpuAccelerationAvailable && enableGPUAcceleration)
        {
            PerformGPURaycasts(rayRequests);
        }
        else
        {
            PerformCPURaycasts(rayRequests);
        }
        
        OptimizeBeaconPlacement();
    }
    
    private async Task ConeShotAsync(CancellationToken cancellationToken)
    {
        placedBeaconPositions.Clear();
        beaconImportanceScores.Clear();
        Vector3 origin = mainCamera.transform.position;
        Vector3 forward = mainCamera.transform.forward;
        
        List<RaycastRequest> rayRequests = new List<RaycastRequest>();
        GenerateRayRequests(ref rayRequests, origin, forward);
        
        if (gpuAccelerationAvailable && enableGPUAcceleration)
        {
            await Task.Run(() => PerformGPURaycasts(rayRequests), cancellationToken);
        }
        else
        {
            await PerformCPURaycastsAsync(rayRequests, cancellationToken);
        }
        
        OptimizeBeaconPlacement();
    }
    
    private void GenerateRayRequests(ref List<RaycastRequest> rayRequests, Vector3 origin, Vector3 forward)
    {
        cachedRayRequests.Clear();
        Vector3 rightDir = Vector3.Cross(forward, Vector3.up).normalized;
        Vector3 upDir = Vector3.Cross(rightDir, forward).normalized;
        
        for (int i = 0; i < maxBeacons; i++)
        {
            Vector3 randomDirection;
            
            if (i < maxBeacons / 4 && usePathPrediction && predictedMovementDirection.magnitude > 0.01f)
            {
                randomDirection = Vector3.Lerp(forward, predictedMovementDirection, pathPredictionWeight);
                randomDirection = randomDirection.normalized;
                randomDirection = ApplyRandomDeviation(randomDirection, coneCastAngle * 0.5f);
            }
            else
            {
                randomDirection = GetRandomConeDirection(forward, coneCastAngle);
            }
            
            var request = new RaycastRequest {
                Origin = origin,
                Direction = randomDirection,
                MaxDistance = depth,
                LayerMask = obstacleDetectionLayers,
                Priority = 1.0f
            };
            
            rayRequests.Add(request);
            
            if (!cachedRayRequests.ContainsKey(randomDirection))
            {
                cachedRayRequests.Add(randomDirection, request);
            }
        }
        
        if (useSpatialUnderstanding && spatialUnderstanding != null && 
            spatialUnderstanding.ScanState == SpatialUnderstanding.ScanStates.Done)
        {
            AddIntelligentScanPoints(ref rayRequests, origin, forward);
        }
    }
    
    private Vector3 ApplyRandomDeviation(Vector3 direction, float maxAngle)
    {
        Vector3 randomDir = UnityEngine.Random.onUnitSphere;
        if (Vector3.Dot(randomDir, direction) > 0.99f)
        {
            randomDir = Vector3.Cross(direction, Vector3.up);
            if (randomDir.magnitude < 0.01f)
            {
                randomDir = Vector3.Cross(direction, Vector3.right);
            }
        }
        
        Vector3 perpendicular = Vector3.Cross(direction, randomDir).normalized;
        Vector3 perpendicular2 = Vector3.Cross(direction, perpendicular).normalized;
        
        float angle = UnityEngine.Random.Range(0, maxAngle);
        float radiansAngle = angle * Mathf.Deg2Rad;
        float x = Mathf.Sin(radiansAngle) * Mathf.Cos(UnityEngine.Random.Range(0f, 2f * Mathf.PI));
        float y = Mathf.Sin(radiansAngle) * Mathf.Sin(UnityEngine.Random.Range(0f, 2f * Mathf.PI));
        float z = Mathf.Cos(radiansAngle);
        
        return direction * z + perpendicular * x + perpendicular2 * y;
    }
    
    private void PerformGPURaycasts(List<RaycastRequest> rayRequests)
    {
        int totalRaycasts = rayRequests.Count;
        int batchCount = Mathf.CeilToInt((float)totalRaycasts / GPU_BATCH_SIZE);
        
        for (int batch = 0; batchCount > batch; batch++)
        {
            int startIdx = batch * GPU_BATCH_SIZE;
            int count = Mathf.Min(GPU_BATCH_SIZE, totalRaycasts - startIdx);
            
            ComputeBuffer originBuffer = new ComputeBuffer(count, sizeof(float) * 3);
            ComputeBuffer directionBuffer = new ComputeBuffer(count, sizeof(float) * 3);
            ComputeBuffer distanceBuffer = new ComputeBuffer(count, sizeof(float));
            ComputeBuffer resultBuffer = new ComputeBuffer(count, sizeof(float) * 7); // hit bool, position(3), normal(3)
            
            Vector3[] origins = new Vector3[count];
            Vector3[] directions = new Vector3[count];
            float[] distances = new float[count];
            
            for (int i = 0; i < count; i++)
            {
                int idx = startIdx + i;
                if (idx < rayRequests.Count)
                {
                    origins[i] = rayRequests[idx].Origin;
                    directions[i] = rayRequests[idx].Direction;
                    distances[i] = rayRequests[idx].MaxDistance;
                }
            }
            
            originBuffer.SetData(origins);
            directionBuffer.SetData(directions);
            distanceBuffer.SetData(distances);
            
            raycastComputeShader.SetBuffer(raycastKernel, "Origins", originBuffer);
            raycastComputeShader.SetBuffer(raycastKernel, "Directions", directionBuffer);
            raycastComputeShader.SetBuffer(raycastKernel, "MaxDistances", distanceBuffer);
            raycastComputeShader.SetBuffer(raycastKernel, "Results", resultBuffer);
            
            int threadGroupsX = Mathf.CeilToInt(count / 64.0f);
            raycastComputeShader.Dispatch(raycastKernel, threadGroupsX, 1, 1);
            
            float[] results = new float[count * 7];
            resultBuffer.GetData(results);
            
            for (int i = 0; i < count; i++)
            {
                int baseIdx = i * 7;
                bool hit = results[baseIdx] > 0.5f;
                
                if (hit)
                {
                    Vector3 hitPoint = new Vector3(results[baseIdx + 1], results[baseIdx + 2], results[baseIdx + 3]);
                    Vector3 hitNormal = new Vector3(results[baseIdx + 4], results[baseIdx + 5], results[baseIdx + 6]);
                    
                    RaycastHit syntheticHit = new RaycastHit();
                    typeof(RaycastHit).GetField("m_Point", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(syntheticHit, hitPoint);
                    typeof(RaycastHit).GetField("m_Normal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(syntheticHit, hitNormal);
                    
                    float distance = Vector3.Distance(rayRequests[startIdx + i].Origin, hitPoint);
                    typeof(RaycastHit).GetField("m_Distance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(syntheticHit, distance);
                    
                    ProcessHitForBeaconPlacement(syntheticHit);
                }
            }
            
            originBuffer.Dispose();
            directionBuffer.Dispose();
            distanceBuffer.Dispose();
            resultBuffer.Dispose();
        }
    }
    
    private void PerformCPURaycasts(List<RaycastRequest> rayRequests)
    {
        foreach (var request in rayRequests)
        {
            RaycastHit hit;
            if (Physics.Raycast(request.Origin, request.Direction, out hit, request.MaxDistance, request.LayerMask))
            {
                ProcessHitForBeaconPlacement(hit);
            }
        }
    }
    
    private async Task PerformCPURaycastsAsync(List<RaycastRequest> rayRequests, CancellationToken cancellationToken)
    {
        int totalRaycasts = rayRequests.Count;
        int batchSize = Mathf.CeilToInt((float)totalRaycasts / multithreadingLevel);
        
        List<Task> tasks = new List<Task>();
        
        for (int i = 0; i < multithreadingLevel; i++)
        {
            int fromIndex = i * batchSize;
            int toIndex = Mathf.Min(fromIndex + batchSize, totalRaycasts);
            
            if (fromIndex >= totalRaycasts)
                break;
            
            tasks.Add(Task.Run(() => {
                for (int j = fromIndex; j < toIndex; j++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    
                    RaycastRequest request = rayRequests[j];
                    RaycastHit hit;
                    if (Physics.Raycast(request.Origin, request.Direction, out hit, request.MaxDistance, request.LayerMask))
                    {
                        // We can't process the hit directly in a background thread since Unity API calls aren't thread-safe
                        UnityEngine.WSA.Application.InvokeOnAppThread(() => {
                            if (!cancellationToken.IsCancellationRequested)
                                ProcessHitForBeaconPlacement(hit);
                        }, false);
                    }
                }
            }, cancellationToken));
        }
        
        await Task.WhenAll(tasks);
    }
    
    private void ProcessHitForBeaconPlacement(RaycastHit hit)
    {
        if (hit.transform == null) return;
        
        Vector3 hitPosition = hit.point;
        Vector3 hitNormal = hit.normal;
        float distanceFromHead = Vector3.Distance(mainCamera.transform.position, hitPosition);
        float heightDifference = hitPosition.y - mainCamera.transform.position.y;
        
        // Skip if hit point is a floor, ceiling, existing beacon, or outside height threshold
        if (hit.transform.CompareTag("Floor") ||
            hit.transform.CompareTag("Ceiling") ||
            hit.transform.CompareTag("Obstacle Beacon") ||
            hit.transform.CompareTag("Wall Beacon") ||
            heightDifference <= -1.0f || 
            heightDifference >= 1.0f)
        {
            return;
        }
        
        // Adaptive beacon placement - don't place beacons too close to each other
        if (adaptiveBeaconPlacement)
        {
            foreach (Vector3 existingPosition in placedBeaconPositions)
            {
                if (Vector3.Distance(existingPosition, hitPosition) < minimumInterBeaconDistance)
                {
                    return;
                }
            }
        }
        
        // Enhanced surface classification
        ObstacleSurfaceType surfaceType = ClassifySurface(hit);
        
        // Place appropriate beacon type
        GameObject beacon;
        bool isDynamicObstacle = surfaceType == ObstacleSurfaceType.Dynamic;
        
        if (surfaceType == ObstacleSurfaceType.Wall)
        {
            beacon = Instantiate(wallBeacon, hitPosition, Quaternion.identity, beaconManager.transform);
            beacon.tag = "Wall Beacon";
            
            // Calculate importance score for walls
            float importanceScore = CalculateBeaconImportance(hitPosition, true, distanceFromHead);
            beaconImportanceScores[beacon] = importanceScore;
        }
        else
        {
            beacon = Instantiate(obstacleBeacon, hitPosition, Quaternion.identity, beaconManager.transform);
            beacon.tag = "Obstacle Beacon";
            
            // Calculate importance score for obstacles
            float importanceScore = CalculateBeaconImportance(hitPosition, false, distanceFromHead);
            beaconImportanceScores[beacon] = importanceScore;
            
            if (isDynamicObstacle && dynamicObstacleMaterial != null)
            {
                var renderer = beacon.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = dynamicObstacleMaterial;
                }
            }
        }
        
        // Track dynamic obstacles
        if (isDynamicObstacle && enableDynamicObstacleTracking)
        {
            string obstacleId = $"dyn_{hitPosition.x:F2}_{hitPosition.y:F2}_{hitPosition.z:F2}";
            dynamicObstacleDetectionTimes[obstacleId] = DateTime.Now;
        }
        
        // Set obstacle type for audio characteristics
        ObstacleAudio audio = beacon.GetComponent<ObstacleAudio>();
        if (audio != null)
        {
            string obstacleType = surfaceType.ToString().ToLower();
            audio.SetObstacleType(obstacleType);
        }
        
        placedBeaconPositions.Add(hitPosition);
    }
    
    private enum ObstacleSurfaceType
    {
        Wall,
        Floor,
        Ceiling,
        Overhead,
        Ground,
        Dynamic
    }
    
    private ObstacleSurfaceType ClassifySurface(RaycastHit hit)
    {
        Vector3 normal = hit.normal;
        float heightDifference = hit.point.y - mainCamera.transform.position.y;
        float upDot = Vector3.Dot(normal, Vector3.up);
        
        // Check if object is potentially movable/dynamic
        Rigidbody rb = hit.rigidbody;
        if (rb != null && (!rb.isKinematic || rb.mass < 10f))
        {
            return ObstacleSurfaceType.Dynamic;
        }
        
        // Check surface type based on normal and position
        if (upDot > FLOOR_NORMAL_THRESHOLD)
        {
            return ObstacleSurfaceType.Floor;
        }
        else if (upDot < CEILING_NORMAL_THRESHOLD)
        {
            return ObstacleSurfaceType.Ceiling;
        }
        else if (Mathf.Abs(upDot) < 0.3f)
        {
            return ObstacleSurfaceType.Wall;
        }
        else if (heightDifference > 0.5f)
        {
            return ObstacleSurfaceType.Overhead;
        }
        else if (heightDifference < -0.5f)
        {
            return ObstacleSurfaceType.Ground;
        }
        
        return ObstacleSurfaceType.Dynamic;
    }
    
    private float CalculateBeaconImportance(Vector3 position, bool isWall, float distance)
    {
        float distanceFactor = 1.0f - Mathf.Clamp01(distance / depth);
        
        Vector3 dirToObject = (position - mainCamera.transform.position).normalized;
        float alignmentFactor = Vector3.Dot(mainCamera.transform.forward, dirToObject);
        alignmentFactor = (alignmentFactor + 1.0f) / 2.0f;
        
        // Enhanced importance calculation incorporating predicted movement direction
        float pathAlignmentFactor = 1.0f;
        if (usePathPrediction && predictedMovementDirection.magnitude > 0.01f)
        {
            float pathAlignment = Vector3.Dot(predictedMovementDirection, dirToObject);
            pathAlignmentFactor = Mathf.Lerp(1.0f, 1.5f, Mathf.Clamp01((pathAlignment + 1.0f) / 2.0f));
        }
        
        float heightDiff = Mathf.Abs(position.y - mainCamera.transform.position.y);
        float heightFactor = 1.0f - Mathf.Clamp01(heightDiff / 1.0f);
        
        float typeMultiplier = isWall ? 1.2f : 1.0f;
        
        // More sophisticated formula with path prediction
        return (distanceFactor * 0.5f + 
                alignmentFactor * 0.3f * pathAlignmentFactor + 
                heightFactor * 0.2f) * typeMultiplier;
    }
    
    private void OptimizeBeaconPlacement()
    {
        int maxKeepBeacons = (int)(maxBeacons * 1.5f);
        
        if (beaconImportanceScores.Count > maxKeepBeacons)
        {
            List<KeyValuePair<GameObject, float>> sortedBeacons = new List<KeyValuePair<GameObject, float>>(beaconImportanceScores);
            sortedBeacons.Sort((a, b) => b.Value.CompareTo(a.Value));
            
            for (int i = maxKeepBeacons; i < sortedBeacons.Count; i++)
            {
                if (sortedBeacons[i].Key != null)
                {
                    Destroy(sortedBeacons[i].Key);
                }
            }
        }
    }
    
    private Vector3 GetRandomConeDirection(Vector3 coneDirection, float coneAngle)
    {
        float angle = UnityEngine.Random.Range(0, coneAngle);
        Vector3 randomDir = UnityEngine.Random.onUnitSphere;
        
        if (Vector3.Dot(randomDir, coneDirection) > 0.99f)
        {
            randomDir = Vector3.Cross(coneDirection, Vector3.up);
            if (randomDir.magnitude < 0.01f)
            {
                randomDir = Vector3.Cross(coneDirection, Vector3.right);
            }
        }
        
        Vector3 perpendicular = Vector3.Cross(coneDirection, randomDir).normalized;
        Vector3 perpendicular2 = Vector3.Cross(coneDirection, perpendicular).normalized;
        
        float radiansAngle = angle * Mathf.Deg2Rad;
        float x = Mathf.Sin(radiansAngle) * Mathf.Cos(UnityEngine.Random.Range(0f, 2f * Mathf.PI));
        float y = Mathf.Sin(radiansAngle) * Mathf.Sin(UnityEngine.Random.Range(0f, 2f * Mathf.PI));
        float z = Mathf.Cos(radiansAngle);
        
        return coneDirection * z + perpendicular * x + perpendicular2 * y;
    }
    
    private void AddIntelligentScanPoints(ref List<RaycastRequest> rayRequests, Vector3 origin, Vector3 forward)
    {
        try
        {
            // Add rays toward detected doorways
            Vector3 position = Vector3.zero;
            if (SpatialUnderstanding.Instance.UnderstandingCustomMesh.FindPositionOnTopObstacle(
                out position, origin, 0.5f, 5.0f))
            {
                Vector3 direction = (position - origin).normalized;
                rayRequests.Add(new RaycastRequest {
                    Origin = origin,
                    Direction = direction,
                    MaxDistance = depth,
                    LayerMask = obstacleDetectionLayers,
                    Priority = 2.0f
                });
            }
            
            // Add rays toward potential pathways/walkable areas
            Vector3[] topSurfacePositions;
            if (GetWalkableSurfacePositions(out topSurfacePositions, 3))
            {
                foreach (Vector3 surfacePos in topSurfacePositions)
                {
                    Vector3 direction = (surfacePos - origin).normalized;
                    
                    if (Vector3.Dot(direction, forward) > 0.3f)
                    {
                        rayRequests.Add(new RaycastRequest {
                            Origin = origin,
                            Direction = direction,
                            MaxDistance = depth,
                            LayerMask = obstacleDetectionLayers,
                            Priority = 1.5f
                        });
                    }
                }
            }
            
            // Enhanced object detection - check junctions and intersections
            AddSpecialAreaScanPoints(ref rayRequests, origin, forward);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error in intelligent scan: {ex.Message}");
        }
    }
    
    private void AddSpecialAreaScanPoints(ref List<RaycastRequest> rayRequests, Vector3 origin, Vector3 forward)
    {
        if (spatialUnderstanding == null || 
            spatialUnderstanding.ScanState != SpatialUnderstanding.ScanStates.Done) 
            return;
        
        try
        {
            // Find doorways and openings
            IntPtr doorwayPtr = SpatialUnderstanding.Instance.UnderstandingDLL.PinObject(null);
            if (SpatialUnderstanding.Instance.UnderstandingDLL.QueryTopology_FindPositionsOnDoorways(
                minimumInterBeaconDistance, 0.0f, 3, doorwayPtr) > 0)
            {
                // Access marshaled data and add rays to doorway positions
                object[] doorObjs = SpatialUnderstanding.Instance.UnderstandingDLL.GetThingsArray(doorwayPtr);
                
                if (doorObjs != null && doorObjs.Length > 0)
                {
                    foreach (object obj in doorObjs)
                    {
                        Vector3 doorPos = (Vector3)obj;
                        
                        Vector3 directionToDoor = (doorPos - origin).normalized;
                        rayRequests.Add(new RaycastRequest {
                            Origin = origin,
                            Direction = directionToDoor,
                            MaxDistance = depth * 1.5f, // Extra range for doorways
                            LayerMask = obstacleDetectionLayers,
                            Priority = 2.5f
                        });
                    }
                }
            }
            
            // Extract and check for room corners - valuable navigation points
            IntPtr cornerPtr = SpatialUnderstanding.Instance.UnderstandingDLL.PinObject(null);
            if (SpatialUnderstanding.Instance.UnderstandingDLL.QueryTopology_FindPositionsOnEdges(
                minimumInterBeaconDistance, 0.5f, 5, cornerPtr) > 0)
            {
                object[] cornerObjs = SpatialUnderstanding.Instance.UnderstandingDLL.GetThingsArray(cornerPtr);
                
                if (cornerObjs != null && cornerObjs.Length > 0)
                {
                    foreach (object obj in cornerObjs)
                    {
                        Vector3 cornerPos = (Vector3)obj;
                        Vector3 directionToCorner = (cornerPos - origin).normalized;
                        
                        // Only add if it's somewhat in direction of travel
                        if (Vector3.Dot(directionToCorner, forward) > 0.1f)
                        {
                            rayRequests.Add(new RaycastRequest {
                                Origin = origin,
                                Direction = directionToCorner,
                                MaxDistance = depth,
                                LayerMask = obstacleDetectionLayers,
                                Priority = 1.8f
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in special area detection: {ex.Message}");
        }
    }
    
    private bool GetWalkableSurfacePositions(out Vector3[] positions, int maxPositions)
    {
        positions = new Vector3[maxPositions];
        int found = 0;
        
        try
        {
            Vector3 origin = mainCamera.transform.position;
            origin.y -= 0.3f;
            
            // Forward, left, right raycasts for floor surfaces
            RaycastHit hit;
            if (Physics.Raycast(origin, mainCamera.transform.forward, out hit, 10.0f))
            {
                if (hit.normal.y > 0.7f)
                {
                    positions[found++] = hit.point;
                }
            }
            
            if (found < maxPositions && Physics.Raycast(origin, -mainCamera.transform.right, out hit, 5.0f))
            {
                if (hit.normal.y > 0.7f) positions[found++] = hit.point;
            }
            
            if (found < maxPositions && Physics.Raycast(origin, mainCamera.transform.right, out hit, 5.0f))
            {
                if (hit.normal.y > 0.7f) positions[found++] = hit.point;
            }
            
            // Look for surfaces at 45° angles
            if (found < maxPositions)
            {
                Vector3 forwardRight = (mainCamera.transform.forward + mainCamera.transform.right).normalized;
                if (Physics.Raycast(origin, forwardRight, out hit, 7.0f) && hit.normal.y > 0.7f)
                {
                    positions[found++] = hit.point;
                }
            }
            
            if (found < maxPositions)
            {
                Vector3 forwardLeft = (mainCamera.transform.forward - mainCamera.transform.right).normalized;
                if (Physics.Raycast(origin, forwardLeft, out hit, 7.0f) && hit.normal.y > 0.7f)
                {
                    positions[found++] = hit.point;
                }
            }
            
            return found > 0;
        }
        catch
        {
            return false;
        }
    }

    public void DeleteBeacons()
    {
        foreach (Transform beacon in beaconManager.transform)
        {
            Destroy(beacon.gameObject);
        }
        placedBeaconPositions.Clear();
        beaconImportanceScores.Clear();
    }

    public void MuteAllObstacleBeacons()
    {
        foreach (Transform beacon in beaconManager.transform)
        {
            AudioSource audioSource = beacon.gameObject.GetComponent<AudioSource>();
            if (audioSource != null) audioSource.volume = 0;
            
            Renderer renderer = beacon.gameObject.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = Color.gray;
        }
    }

    public void UnmuteAllObstacleBeacons()
    {
        foreach (Transform beacon in beaconManager.transform)
        {
            AudioSource audioSource = beacon.gameObject.GetComponent<AudioSource>();
            if (audioSource != null) audioSource.volume = 1;
            
            Renderer renderer = beacon.gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (beacon.gameObject.CompareTag("Obstacle Beacon"))
                {
                    renderer.material = obstacleMaterial;
                }
                else if (beacon.gameObject.CompareTag("Wall Beacon"))
                {
                    renderer.material = wallMaterial;
                }
            }
        }
    }
    
    public async Task<int> PerformEnvironmentAnalysis()
    {
        int beaconsPlaced = 0;
        
        try
        {
            if (!useSpatialUnderstanding || spatialUnderstanding == null)
                return 0;
                
            if (spatialUnderstanding.ScanState != SpatialUnderstanding.ScanStates.Done)
                return 0;
                
            await Task.Yield();
            DeleteBeacons();
            
            // Enhanced environment analysis - handle walls, floors, objects and special areas
            await PerformEnhancedEnvironmentMapping();
            beaconsPlaced = placedBeaconPositions.Count;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in environment analysis: {ex.Message}");
        }
        
        return beaconsPlaced;
    }
    
    private async Task PerformEnhancedEnvironmentMapping()
    {
        // Find walls
        var wallsQuery = spatialUnderstanding.UnderstandingCustomMesh.QueryTopology_FindPositionsOnWalls(
            minimumInterBeaconDistance, 
            0.5f,
            1.5f,
            2.0f,
            15);
        
        if (wallsQuery != null && wallsQuery.Length > 0)
        {
            foreach (var pos in wallsQuery)
            {
                if (placedBeaconPositions.Count < maxBeacons)
                {
                    PlaceWallBeacon(pos);
                }
            }
        }
        
        await Task.Yield();
        
        // Find large obstacles
        Vector3 cameraPos = mainCamera.transform.position;
        var obstacleQuery = spatialUnderstanding.UnderstandingCustomMesh.QueryTopology_FindPositionsOnLargeObjects(
            minimumInterBeaconDistance,
            0.1f,
            cameraPos.y + 0.1f,
            15);
        
        if (obstacleQuery != null && obstacleQuery.Length > 0)
        {
            foreach (var pos in obstacleQuery)
            {
                if (placedBeaconPositions.Count < maxBeacons)
                {
                    PlaceObstacleBeacon(pos);
                }
            }
        }
        
        await Task.Yield();
        
        // Find room corners
        var cornerQuery = spatialUnderstanding.UnderstandingCustomMesh.QueryTopology_FindPositionsOnEdges(
            minimumInterBeaconDistance * 2f, 
            0.5f, 
            10);
            
        if (cornerQuery != null && cornerQuery.Length > 0)
        {
            foreach (var pos in cornerQuery)
            {
                if (placedBeaconPositions.Count < maxBeacons)
                {
                    PlaceWallBeacon(pos);
                }
            }
        }
        
        // Ensure beacons are optimally placed based on importance
        OptimizeBeaconPlacement();
    }
    
    private void PlaceWallBeacon(Vector3 position)
    {
        GameObject beacon = Instantiate(wallBeacon, position, Quaternion.identity, beaconManager.transform);
        beacon.tag = "Wall Beacon";
        
        ObstacleAudio audio = beacon.GetComponent<ObstacleAudio>();
        if (audio != null)
        {
            audio.SetObstacleType("wall");
        }
        
        float distanceFromHead = Vector3.Distance(mainCamera.transform.position, position);
        float importanceScore = CalculateBeaconImportance(position, true, distanceFromHead);
        beaconImportanceScores[beacon] = importanceScore;
        
        placedBeaconPositions.Add(position);
    }
    
    private void PlaceObstacleBeacon(Vector3 position)
    {
        GameObject beacon = Instantiate(obstacleBeacon, position, Quaternion.identity, beaconManager.transform);
        beacon.tag = "Obstacle Beacon";
        
        ObstacleAudio audio = beacon.GetComponent<ObstacleAudio>();
        if (audio != null)
        {
            audio.SetObstacleType("dynamic");
        }
        
        float distanceFromHead = Vector3.Distance(mainCamera.transform.position, position);
        float importanceScore = CalculateBeaconImportance(position, false, distanceFromHead);
        beaconImportanceScores[beacon] = importanceScore;
        
        placedBeaconPositions.Add(position);
    }
    
    public struct RaycastRequest
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float MaxDistance;
        public LayerMask LayerMask;
        public float Priority;
    }
}
