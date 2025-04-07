using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.WSA;
using UnityEngine.XR.WSA.Input;
using UnityEngine.XR.WSA.Persistence;

public class EnvironmentAnalyzer : MonoBehaviour
{
    [Range(0.01f, 1.0f)]
    public float smoothingFactor = 0.8f;
    
    [Range(8, 32)]
    public int kernelSize = 16;
    
    [Range(0.5f, 5.0f)]
    public float sigmaValue = 2.0f;
    
    [Range(0.5f, 3.0f)]
    public float planeFittingThreshold = 1.0f;
    
    [Range(0.1f, 1.0f)]
    public float audioAttenuationRate = 0.2f;
    
    public AudioSource proximityAudioSource;
    
    private List<Vector3> depthPoints = new List<Vector3>();
    private List<Vector3> surfaceNormals = new List<Vector3>();
    private List<Plane> detectedPlanes = new List<Plane>();
    private List<ObstacleInfo> detectedObstacles = new List<ObstacleInfo>();
    
    private float[,,] gaussianField;
    private float fieldResolution = 0.1f;
    private Vector3 fieldMin = Vector3.zero;
    private Vector3 fieldMax = Vector3.zero;
    private int fieldSizeX, fieldSizeY, fieldSizeZ;
    
    private bool isProcessing = false;
    private bool isFieldCalculated = false;
    
    private void Start()
    {
        InitializeKernel();
    }
    
    private void Update()
    {
        if (detectedObstacles.Count > 0)
        {
            UpdateProximityAudio();
        }
    }
    
    private void InitializeKernel()
    {
        float sigma = sigmaValue;
        float twoSigmaSquared = 2.0f * sigma * sigma;
        float kernelSum = 0;
        
        int center = kernelSize / 2;
        float[,] kernel2D = new float[kernelSize, kernelSize];
        
        for (int x = -center; x <= center; x++)
        {
            for (int y = -center; y <= center; y++)
            {
                int i = x + center;
                int j = y + center;
                
                float distance = Mathf.Sqrt(x * x + y * y);
                kernel2D[i, j] = Mathf.Exp(-(distance * distance) / twoSigmaSquared);
                kernelSum += kernel2D[i, j];
            }
        }
        
        // Normalize the kernel
        for (int i = 0; i < kernelSize; i++)
        {
            for (int j = 0; j < kernelSize; j++)
            {
                kernel2D[i, j] /= kernelSum;
            }
        }
    }
    
    public void ProcessImageWithDepth(byte[] imageData, Matrix4x4 cameraToWorldMatrix, Matrix4x4 projectionMatrix)
    {
        if (isProcessing)
            return;
            
        isProcessing = true;
        StartCoroutine(ProcessDepthAsync(imageData, cameraToWorldMatrix, projectionMatrix));
    }
    
    private IEnumerator ProcessDepthAsync(byte[] imageData, Matrix4x4 cameraToWorldMatrix, Matrix4x4 projectionMatrix)
    {
        yield return CollectSpatialMappingDataAsync();
        
        CalculateGaussianField();
        
        DetectPlanes();
        
        DetectObstacles();
        
        ProduceAudioFeedback();
        
        isProcessing = false;
    }
    
    private IEnumerator CollectSpatialMappingDataAsync()
    {
        depthPoints.Clear();
        surfaceNormals.Clear();
        
        MeshFilter[] meshFilters = SpatialMappingManager.Instance?.GetMeshFilters();
        
        if (meshFilters == null || meshFilters.Length == 0)
        {
            Globals.instance.textToSpeech.StartSpeaking("No spatial mapping data available. Please scan your environment.");
            yield break;
        }
        
        int totalVertices = 0;
        fieldMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        fieldMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        
        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.mesh == null)
                continue;
                
            Mesh mesh = meshFilter.mesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            
            if (vertices == null || vertices.Length == 0)
                continue;
                
            Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
            
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = localToWorld.MultiplyPoint3x4(vertices[i]);
                Vector3 worldNormal = localToWorld.MultiplyVector(normals[i]).normalized;
                
                depthPoints.Add(worldPos);
                surfaceNormals.Add(worldNormal);
                
                fieldMin = Vector3.Min(fieldMin, worldPos);
                fieldMax = Vector3.Max(fieldMax, worldPos);
                
                totalVertices++;
                
                if (totalVertices % 1000 == 0)
                    yield return null;  // Spread processing across frames
            }
        }
        
        // Add margins to field bounds
        fieldMin -= new Vector3(0.5f, 0.5f, 0.5f);
        fieldMax += new Vector3(0.5f, 0.5f, 0.5f);
        
        fieldSizeX = Mathf.CeilToInt((fieldMax.x - fieldMin.x) / fieldResolution);
        fieldSizeY = Mathf.CeilToInt((fieldMax.y - fieldMin.y) / fieldResolution);
        fieldSizeZ = Mathf.CeilToInt((fieldMax.z - fieldMin.z) / fieldResolution);
        
        Debug.Log($"Collected {totalVertices} depth points. Field dimensions: {fieldSizeX}x{fieldSizeY}x{fieldSizeZ}");
    }
    
    private void CalculateGaussianField()
    {
        if (depthPoints.Count == 0)
            return;
            
        // If the field would be too large, adjust resolution
        int maxSize = 100;
        if (fieldSizeX > maxSize || fieldSizeY > maxSize || fieldSizeZ > maxSize)
        {
            float maxDim = Mathf.Max(fieldMax.x - fieldMin.x, Mathf.Max(fieldMax.y - fieldMin.y, fieldMax.z - fieldMin.z));
            fieldResolution = maxDim / maxSize;
            
            fieldSizeX = Mathf.CeilToInt((fieldMax.x - fieldMin.x) / fieldResolution);
            fieldSizeY = Mathf.CeilToInt((fieldMax.y - fieldMin.y) / fieldResolution);
            fieldSizeZ = Mathf.CeilToInt((fieldMax.z - fieldMin.z) / fieldResolution);
        }
        
        gaussianField = new float[fieldSizeX, fieldSizeY, fieldSizeZ];
        
        // Implementation of the formula from the paper:
        // f(r) = (1/N) * sum_j exp(-||r - r_j||^2 / (2*sigma^2))
        
        float twoSigmaSquared = 2.0f * sigmaValue * sigmaValue;
        int pointCount = depthPoints.Count;
        
        System.Threading.Tasks.Parallel.For(0, fieldSizeX, i =>
        {
            float x = fieldMin.x + i * fieldResolution;
            
            for (int j = 0; j < fieldSizeY; j++)
            {
                float y = fieldMin.y + j * fieldResolution;
                
                for (int k = 0; k < fieldSizeZ; k++)
                {
                    float z = fieldMin.z + k * fieldResolution;
                    Vector3 fieldPoint = new Vector3(x, y, z);
                    
                    float sum = 0;
                    for (int p = 0; p < pointCount; p += 10) // Sample every 10th point for efficiency
                    {
                        float sqrDist = (fieldPoint - depthPoints[p]).sqrMagnitude;
                        sum += Mathf.Exp(-sqrDist / twoSigmaSquared);
                    }
                    
                    gaussianField[i, j, k] = sum / (pointCount / 10);
                }
            }
        });
        
        isFieldCalculated = true;
    }
    
    private void DetectPlanes()
    {
        detectedPlanes.Clear();
        
        if (depthPoints.Count < 100)
            return;
            
        int iterations = Mathf.Min(50, depthPoints.Count / 20);
        
        // RANSAC plane fitting algorithm
        for (int iter = 0; iter < iterations; iter++)
        {
            // 1. Select three random points
            int[] indices = GetThreeRandomIndices(depthPoints.Count);
            
            // 2. Compute plane equation
            Vector3 p1 = depthPoints[indices[0]];
            Vector3 p2 = depthPoints[indices[1]];
            Vector3 p3 = depthPoints[indices[2]];
            
            Vector3 normal = Vector3.Cross(p2 - p1, p3 - p1).normalized;
            float d = -Vector3.Dot(normal, p1);
            
            // 3. Count inliers
            List<int> inliers = new List<int>();
            
            for (int i = 0; i < depthPoints.Count; i++)
            {
                float distance = Mathf.Abs(Vector3.Dot(normal, depthPoints[i]) + d);
                
                if (distance < planeFittingThreshold)
                {
                    inliers.Add(i);
                }
            }
            
            // 4. If enough inliers, refine the plane
            if (inliers.Count > depthPoints.Count * 0.2f) // At least 20% of points
            {
                // Calculate centroid
                Vector3 centroid = Vector3.zero;
                foreach (int idx in inliers)
                {
                    centroid += depthPoints[idx];
                }
                centroid /= inliers.Count;
                
                // Refine normal with SVD (simplified here)
                Vector3 refinedNormal = Vector3.zero;
                foreach (int idx in inliers)
                {
                    refinedNormal += surfaceNormals[idx];
                }
                refinedNormal.Normalize();
                
                // Create a plane with the refined parameters
                float refinedD = -Vector3.Dot(refinedNormal, centroid);
                Plane plane = new Plane(refinedNormal, refinedD);
                
                // Only add if not duplicating an existing plane
                bool isUnique = true;
                foreach (Plane existingPlane in detectedPlanes)
                {
                    float angle = Vector3.Angle(existingPlane.normal, refinedNormal);
                    float distDifference = Mathf.Abs(existingPlane.distance - refinedD);
                    
                    if (angle < 15f && distDifference < 0.3f)
                    {
                        isUnique = false;
                        break;
                    }
                }
                
                if (isUnique)
                {
                    detectedPlanes.Add(plane);
                }
                
                // Remove inliers from further consideration (optional)
                // RemoveInliers(inliers);
            }
        }
        
        Debug.Log($"Detected {detectedPlanes.Count} planes");
    }
    
    private int[] GetThreeRandomIndices(int count)
    {
        int[] result = new int[3];
        
        result[0] = UnityEngine.Random.Range(0, count);
        
        do {
            result[1] = UnityEngine.Random.Range(0, count);
        } while (result[1] == result[0]);
        
        do {
            result[2] = UnityEngine.Random.Range(0, count);
        } while (result[2] == result[0] || result[2] == result[1]);
        
        return result;
    }
    
    private void DetectObstacles()
    {
        detectedObstacles.Clear();
        
        if (!isFieldCalculated || detectedPlanes.Count == 0)
            return;
            
        // 1. Find high density regions that are not part of detected planes
        float densityThreshold = 0.6f;
        
        List<Vector3> obstaclePoints = new List<Vector3>();
        List<float> densities = new List<float>();
        
        for (int i = 0; i < fieldSizeX; i++)
        {
            for (int j = 0; j < fieldSizeY; j++)
            {
                for (int k = 0; k < fieldSizeZ; k++)
                {
                    if (gaussianField[i, j, k] > densityThreshold)
                    {
                        Vector3 point = new Vector3(
                            fieldMin.x + i * fieldResolution,
                            fieldMin.y + j * fieldResolution,
                            fieldMin.z + k * fieldResolution
                        );
                        
                        // Check if point is far from any detected plane
                        bool isObstacle = true;
                        foreach (Plane plane in detectedPlanes)
                        {
                            if (Mathf.Abs(plane.GetDistanceToPoint(point)) < 0.2f)
                            {
                                isObstacle = false;
                                break;
                            }
                        }
                        
                        if (isObstacle)
                        {
                            obstaclePoints.Add(point);
                            densities.Add(gaussianField[i, j, k]);
                        }
                    }
                }
            }
        }
        
        // 2. Cluster obstacle points
        List<List<int>> clusters = ClusterObstaclePoints(obstaclePoints);
        
        // 3. Create obstacle objects from clusters
        foreach (List<int> cluster in clusters)
        {
            if (cluster.Count < 5)
                continue;
                
            Vector3 center = Vector3.zero;
            float maxDensity = 0;
            
            foreach (int idx in cluster)
            {
                center += obstaclePoints[idx];
                maxDensity = Mathf.Max(maxDensity, densities[idx]);
            }
            center /= cluster.Count;
            
            // Calculate approximate bounds
            Vector3 minBound = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxBound = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            
            foreach (int idx in cluster)
            {
                minBound = Vector3.Min(minBound, obstaclePoints[idx]);
                maxBound = Vector3.Max(maxBound, obstaclePoints[idx]);
            }
            
            Vector3 size = maxBound - minBound;
            
            // Create obstacle info
            ObstacleInfo obstacle = new ObstacleInfo
            {
                center = center,
                size = size,
                density = maxDensity,
                distanceToCamera = Vector3.Distance(center, Camera.main.transform.position)
            };
            
            detectedObstacles.Add(obstacle);
        }
        
        detectedObstacles = detectedObstacles.OrderBy(o => o.distanceToCamera).ToList();
        
        Debug.Log($"Detected {detectedObstacles.Count} obstacles");
    }
    
    private List<List<int>> ClusterObstaclePoints(List<Vector3> points)
    {
        List<List<int>> clusters = new List<List<int>>();
        bool[] processed = new bool[points.Count];
        
        float clusterThreshold = 0.3f;  // Points within this distance belong to the same cluster
        
        for (int i = 0; i < points.Count; i++)
        {
            if (processed[i])
                continue;
                
            List<int> cluster = new List<int>();
            Queue<int> queue = new Queue<int>();
            
            queue.Enqueue(i);
            processed[i] = true;
            
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                cluster.Add(current);
                
                for (int j = 0; j < points.Count; j++)
                {
                    if (!processed[j] && Vector3.Distance(points[current], points[j]) < clusterThreshold)
                    {
                        queue.Enqueue(j);
                        processed[j] = true;
                    }
                }
            }
            
            clusters.Add(cluster);
        }
        
        return clusters;
    }
    
    private void ProduceAudioFeedback()
    {
        if (detectedObstacles.Count == 0)
        {
            Globals.instance.textToSpeech.StartSpeaking("No obstacles detected in immediate vicinity.");
            return;
        }
        
        StringBuilder message = new StringBuilder();
        
        // Report on nearest obstacles
        int reportCount = Mathf.Min(3, detectedObstacles.Count);
        message.Append($"Detected {detectedObstacles.Count} objects. ");
        
        for (int i = 0; i < reportCount; i++)
        {
            ObstacleInfo obstacle = detectedObstacles[i];
            string direction = GetDirectionToObstacle(obstacle.center);
            
            if (i == 0)
            {
                message.Append($"Nearest object is {obstacle.distanceToCamera:F1} meters away {direction}. ");
            }
            else
            {
                message.Append($"Another object {obstacle.distanceToCamera:F1} meters {direction}. ");
            }
        }
        
        // Report on large surfaces
        if (detectedPlanes.Count > 0)
        {
            Plane closestPlane = GetClosestVerticalPlane();
            if (closestPlane != null)
            {
                float distance = Mathf.Abs(closestPlane.GetDistanceToPoint(Camera.main.transform.position));
                string planeDirection = closestPlane.GetSide(Camera.main.transform.position) ? "in front of" : "behind";
                message.Append($"Wall detected {distance:F1} meters {planeDirection} you. ");
            }
        }
        
        Globals.instance.textToSpeech.StartSpeaking(message.ToString());
    }
    
    private void UpdateProximityAudio()
    {
        if (proximityAudioSource == null || detectedObstacles.Count == 0)
            return;
            
        ObstacleInfo nearestObstacle = detectedObstacles[0];
        float distance = nearestObstacle.distanceToCamera;
        
        // Exponential intensity falloff (from paper)
        float intensity = Mathf.Exp(-audioAttenuationRate * distance);
        
        // Set audio parameters based on distance
        proximityAudioSource.volume = Mathf.Lerp(0.1f, 0.8f, intensity);
        proximityAudioSource.pitch = Mathf.Lerp(0.8f, 1.5f, intensity);
        
        if (!proximityAudioSource.isPlaying && distance < 2.0f)
        {
            proximityAudioSource.Play();
        }
        else if (proximityAudioSource.isPlaying && distance > 2.5f)
        {
            proximityAudioSource.Stop();
        }
        
        // Update position for spatial audio
        proximityAudioSource.transform.position = nearestObstacle.center;
    }
    
    private string GetDirectionToObstacle(Vector3 obstaclePosition)
    {
        Vector3 directionToObstacle = obstaclePosition - Camera.main.transform.position;
        directionToObstacle.y = 0; // Ignore height differences
        
        Vector3 forward = Camera.main.transform.forward;
        forward.y = 0;
        forward.Normalize();
        
        Vector3 right = Camera.main.transform.right;
        right.y = 0;
        right.Normalize();
        
        float forwardDot = Vector3.Dot(directionToObstacle.normalized, forward);
        float rightDot = Vector3.Dot(directionToObstacle.normalized, right);
        
        if (forwardDot > 0.7f)
            return "in front of you";
        if (forwardDot < -0.7f)
            return "behind you";
        if (rightDot > 0.7f)
            return "to your right";
        if (rightDot < -0.7f)
            return "to your left";
        if (rightDot > 0.3f && forwardDot > 0.3f)
            return "to your front right";
        if (rightDot < -0.3f && forwardDot > 0.3f)
            return "to your front left";
        if (rightDot > 0.3f && forwardDot < -0.3f)
            return "to your back right";
        if (rightDot < -0.3f && forwardDot < -0.3f)
            return "to your back left";
            
        return "nearby";
    }
    
    private Plane GetClosestVerticalPlane()
    {
        Plane closestPlane = null;
        float minDistance = float.MaxValue;
        
        foreach (Plane plane in detectedPlanes)
        {
            // Check if the plane is roughly vertical
            float verticalAlignment = Mathf.Abs(Vector3.Dot(plane.normal, Vector3.up));
            
            if (verticalAlignment < 0.3f) // Less than ~17 degrees from vertical
            {
                float distance = Mathf.Abs(plane.GetDistanceToPoint(Camera.main.transform.position));
                
                if (distance < minDistance && distance < 5f)
                {
                    minDistance = distance;
                    closestPlane = plane;
                }
            }
        }
        
        return closestPlane;
    }
    
    public class ObstacleInfo
    {
        public Vector3 center;
        public Vector3 size;
        public float density;
        public float distanceToCamera;
    }
}