using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.WSA;

public class CognitiveServices : MonoBehaviour
{
    private const string FACE_API_ENDPOINT = "https://westus.api.cognitive.microsoft.com/face/v1.0/";
    private const string VISION_API_ENDPOINT = "https://eastus2.api.cognitive.microsoft.com/vision/v2.0/";
    
    private float contrastEnhancementAlpha = 1.5f;
    private float contrastEnhancementBeta = 2.0f;
    private float spatialMappingResolution = 1024f;
    private float spatialSmoothingFactor = 0.8f;
    
    private List<Vector3> depthPoints = new List<Vector3>();
    private Matrix4x4 worldToDepthCameraMatrix;
    private GameObject spatialMappingAnchor;
    
    private void Awake()
    {
        spatialMappingAnchor = new GameObject("SpatialMappingAnchor");
        spatialMappingAnchor.transform.parent = transform;
        InitializeSpatialMapping();
    }
    
    private async void InitializeSpatialMapping()
    {
        worldToDepthCameraMatrix = Camera.main.worldToCameraMatrix;
        await Task.Yield();
    }
    
    public IEnumerator PostToFace(byte[] imageData)
    {
        byte[] enhancedImageData = ApplyContrastEnhancement(imageData, contrastEnhancementAlpha);
        
        string[] faceAttributes = {"age", "gender", "emotion", "headPose", "facialHair", "blur", "exposure"};
        string url = $"{FACE_API_ENDPOINT}detect?returnFaceId=true&returnFaceAttributes={string.Join(",", faceAttributes)}&recognitionModel=recognition_02";
        
        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            {"Ocp-Apim-Subscription-Key", Constants.MCS_FACEKEY},
            {"Content-Type", "application/octet-stream"}
        };

        UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(enhancedImageData);
        request.downloadHandler = new DownloadHandlerBuffer();
        
        foreach (var header in headers)
            request.SetRequestHeader(header.Key, header.Value);

        yield return request.SendWebRequest();

        if (request.isNetworkError || request.isHttpError)
        {
            Globals.instance.textToSpeech.StartSpeaking($"Network error during facial analysis: {request.error}");
            Debug.LogError($"Network error: {request.error}");
        }
        else
        {
            try
            {
                JSONObject jsonResponse = new JSONObject(request.downloadHandler.text);
                if (jsonResponse != null && jsonResponse.IsArray && jsonResponse.list.Count > 0)
                {
                    EstimateSceneDepth();
                    FaceObject processedFaces = ProcessFaceDetectionResponse(jsonResponse);
                    PlayVoiceMessage.Instance.DeliverAudioFeedback(processedFaces);
                }
                else
                {
                    Globals.instance.textToSpeech.StartSpeaking("No faces were detected in the current field of view.");
                }
            }
            catch (Exception ex)
            {
                Globals.instance.textToSpeech.StartSpeaking("An error occurred while processing facial data.");
                Debug.LogError($"JSON parsing error: {ex.Message}");
            }
        }
    }

    private FaceObject ProcessFaceDetectionResponse(JSONObject jsonResponse)
    {
        FaceObject faceObj = new FaceObject();
        List<Face> faces = new List<Face>();

        foreach (JSONObject faceItem in jsonResponse.list)
        {
            Face face = new Face { faceId = faceItem.GetField("faceId").str };

            JSONObject faceRectangle = faceItem.GetField("faceRectangle");
            face.faceRectangle = new FaceRectangle
            {
                left = (int)faceRectangle.GetField("left").n,
                top = (int)faceRectangle.GetField("top").n,
                width = (int)faceRectangle.GetField("width").n,
                height = (int)faceRectangle.GetField("height").n
            };

            JSONObject faceAttributes = faceItem.GetField("faceAttributes");
            face.faceAttributes = new FaceAttributes
            {
                age = float.Parse(faceAttributes.GetField("age").str),
                gender = faceAttributes.GetField("gender").str.ToLower() == "male" ? 0 : 1,
                blurLevel = faceAttributes.HasField("blur") ? (float)faceAttributes.GetField("blur").n : 0f,
                illuminationLevel = faceAttributes.HasField("exposure") ? (float)faceAttributes.GetField("exposure").n : 0f
            };
            
            if (faceAttributes.HasField("headPose"))
            {
                JSONObject headPose = faceAttributes.GetField("headPose");
                face.faceAttributes.headPose = new HeadPose
                {
                    pitch = (float)headPose.GetField("pitch").n,
                    roll = (float)headPose.GetField("roll").n,
                    yaw = (float)headPose.GetField("yaw").n
                };
            }
            
            if (faceAttributes.HasField("facialHair"))
            {
                JSONObject facialHair = faceAttributes.GetField("facialHair");
                face.faceAttributes.facialHair = new FacialHair
                {
                    moustache = (float)facialHair.GetField("moustache").n,
                    beard = (float)facialHair.GetField("beard").n,
                    sideburns = (float)facialHair.GetField("sideburns").n
                };
            }

            JSONObject emotion = faceAttributes.GetField("emotion");
            face.emotionAttributes = new EmotionAttributes
            {
                anger = (float)emotion.GetField("anger").n,
                contempt = (float)emotion.GetField("contempt").n,
                disgust = (float)emotion.GetField("disgust").n,
                fear = (float)emotion.GetField("fear").n,
                happiness = (float)emotion.GetField("happiness").n,
                neutral = (float)emotion.GetField("neutral").n,
                sadness = (float)emotion.GetField("sadness").n,
                surprise = (float)emotion.GetField("surprise").n
            };
            
            face.spatialPosition = EstimateSpatialPositionFromFace(face);
            faces.Add(face);
        }

        faceObj.faces = faces;
        return faceObj;
    }
    
    public IEnumerator PostToOCR(byte[] imageData)
    {
        byte[] contrastBoostedImage = ApplyAdaptiveContrastEnhancement(imageData);
        
        string url = $"{VISION_API_ENDPOINT}ocr?detectOrientation=true&language=unk";
        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            {"Ocp-Apim-Subscription-Key", Constants.MCS_VISIONKEY},
            {"Content-Type", "application/octet-stream"}
        };

        UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(contrastBoostedImage);
        request.downloadHandler = new DownloadHandlerBuffer();
        
        foreach (var header in headers)
            request.SetRequestHeader(header.Key, header.Value);

        yield return request.SendWebRequest();

        if (request.isNetworkError || request.isHttpError)
        {
            Globals.instance.textToSpeech.StartSpeaking("Network error while trying to read text.");
            Debug.LogError($"OCR network error: {request.error}");
        }
        else
        {
            try
            {
                JSONObject jsonResponse = new JSONObject(request.downloadHandler.text);
                ProcessOCRResponse(jsonResponse);
            }
            catch (Exception ex)
            {
                Globals.instance.textToSpeech.StartSpeaking("Error processing text recognition results.");
                Debug.LogError($"OCR JSON parsing error: {ex.Message}");
            }
        }
    }

    private void ProcessOCRResponse(JSONObject jsonResponse)
    {
        OCRObject textResults = new OCRObject
        {
            language = jsonResponse.HasField("language") ? jsonResponse.GetField("language").str : "unknown",
            orientation = jsonResponse.HasField("orientation") ? jsonResponse.GetField("orientation").str : "up",
            textAngle = jsonResponse.HasField("textAngle") ? jsonResponse.GetField("textAngle").str : "0"
        };

        List<string> extractedWords = new List<string>();
        
        if (jsonResponse.HasField("regions") && jsonResponse.GetField("regions").IsArray)
        {
            JSONObject regions = jsonResponse.GetField("regions");
            foreach (JSONObject region in regions.list)
            {
                if (region.HasField("lines") && region.GetField("lines").IsArray)
                {
                    JSONObject lines = region.GetField("lines");
                    foreach (JSONObject line in lines.list)
                    {
                        if (line.HasField("words") && line.GetField("words").IsArray)
                        {
                            JSONObject words = line.GetField("words");
                            foreach (JSONObject word in words.list)
                            {
                                if (word.HasField("text"))
                                {
                                    string extractedWord = word.GetField("text").str;
                                    extractedWords.Add(extractedWord);
                                }
                            }
                        }
                    }
                }
            }
        }

        textResults.text = string.Join(" ", extractedWords);

        if (extractedWords.Count > 0)
        {
            float baseVolume = CalculateAudioVolumeFromEnvironment();
            Globals.instance.textToSpeech.SetVolumeScale(baseVolume);
            Globals.instance.textToSpeech.StartSpeaking(textResults.text);
        }
        else
        {
            Globals.instance.textToSpeech.StartSpeaking("No readable text was found in the current view.");
        }
    }
    
    private byte[] ApplyContrastEnhancement(byte[] imageData, float contrastFactor)
    {
        try
        {
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(imageData);
            
            float meanIntensity = CalculateMeanIntensity(texture);
            Color[] pixels = texture.GetPixels();
            
            for (int i = 0; i < pixels.Length; i++)
            {
                float r = meanIntensity + contrastFactor * (pixels[i].r - meanIntensity);
                float g = meanIntensity + contrastFactor * (pixels[i].g - meanIntensity);
                float b = meanIntensity + contrastFactor * (pixels[i].b - meanIntensity);
                
                pixels[i] = new Color(
                    Mathf.Clamp01(r),
                    Mathf.Clamp01(g),
                    Mathf.Clamp01(b),
                    pixels[i].a
                );
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            return texture.EncodeToPNG();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error applying contrast enhancement: {ex.Message}");
            return imageData;
        }
    }
    
    private byte[] ApplyAdaptiveContrastEnhancement(byte[] imageData)
    {
        try
        {
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(imageData);
            
            float globalMeanIntensity = CalculateMeanIntensity(texture);
            Color[] pixels = texture.GetPixels();
            int width = texture.width;
            int height = texture.height;
            
            Color[] enhancedPixels = new Color[pixels.Length];
            Array.Copy(pixels, enhancedPixels, pixels.Length);
            
            int blockSize = 32;
            
            for (int blockY = 0; blockY < height; blockY += blockSize)
            {
                for (int blockX = 0; blockX < width; blockX += blockSize)
                {
                    float localMeanIntensity = CalculateLocalMeanIntensity(texture, blockX, blockY, blockSize);
                    float adaptiveContrastFactor = DetermineAdaptiveContrastFactor(localMeanIntensity, globalMeanIntensity);
                    
                    ApplyLocalContrast(ref enhancedPixels, pixels, width, height, 
                                      blockX, blockY, blockSize, localMeanIntensity, adaptiveContrastFactor);
                }
            }
            
            texture.SetPixels(enhancedPixels);
            texture.Apply();
            
            return texture.EncodeToPNG();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error applying adaptive contrast enhancement: {ex.Message}");
            return imageData;
        }
    }
    
    private float CalculateMeanIntensity(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        float totalIntensity = 0f;
        
        foreach (Color pixel in pixels)
        {
            totalIntensity += (pixel.r + pixel.g + pixel.b) / 3f;
        }
        
        return totalIntensity / pixels.Length;
    }
    
    private float CalculateLocalMeanIntensity(Texture2D texture, int startX, int startY, int blockSize)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float totalIntensity = 0f;
        int pixelCount = 0;
        
        for (int y = startY; y < Mathf.Min(startY + blockSize, height); y++)
        {
            for (int x = startX; x < Mathf.Min(startX + blockSize, width); x++)
            {
                int index = y * width + x;
                if (index < pixels.Length)
                {
                    totalIntensity += (pixels[index].r + pixels[index].g + pixels[index].b) / 3f;
                    pixelCount++;
                }
            }
        }
        
        return pixelCount > 0 ? totalIntensity / pixelCount : 0.5f;
    }
    
    private float DetermineAdaptiveContrastFactor(float localMeanIntensity, float globalMeanIntensity)
    {
        float textureFactor = (localMeanIntensity > 0.5f) ? 
                              Mathf.Lerp(contrastEnhancementBeta, contrastEnhancementAlpha, localMeanIntensity) : 
                              contrastEnhancementBeta;
                              
        float relativeBrightness = localMeanIntensity / globalMeanIntensity;
        
        if (relativeBrightness < 0.7f)
            return textureFactor * 1.5f;  // Boost dark areas more
        else if (relativeBrightness > 1.3f)
            return textureFactor * 0.8f;  // Boost bright areas less
            
        return textureFactor;
    }
    
    private void ApplyLocalContrast(ref Color[] outputPixels, Color[] inputPixels, int width, int height,
                                  int startX, int startY, int blockSize, float localMean, float contrastFactor)
    {
        for (int y = startY; y < Mathf.Min(startY + blockSize, height); y++)
        {
            for (int x = startX; x < Mathf.Min(startX + blockSize, width); x++)
            {
                int index = y * width + x;
                if (index < inputPixels.Length)
                {
                    Color pixel = inputPixels[index];
                    
                    float r = localMean + contrastFactor * (pixel.r - localMean);
                    float g = localMean + contrastFactor * (pixel.g - localMean);
                    float b = localMean + contrastFactor * (pixel.b - localMean);
                    
                    outputPixels[index] = new Color(
                        Mathf.Clamp01(r),
                        Mathf.Clamp01(g),
                        Mathf.Clamp01(b),
                        pixel.a
                    );
                }
            }
        }
    }
    
    private void EstimateSceneDepth()
    {
        depthPoints.Clear();
        
        try
        {
            SpatialMappingManager.Instance.GetActiveObserver().Update(SpatialMappingManager.Instance.GetComponent<SpatialMappingObserver>().SurfaceParent);
            MeshFilter[] meshFilters = SpatialMappingManager.Instance.GetComponent<SpatialMappingObserver>().SurfaceParent.GetComponentsInChildren<MeshFilter>();
            
            foreach (MeshFilter meshFilter in meshFilters)
            {
                Vector3[] vertices = meshFilter.mesh.vertices;
                Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
                
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 worldPos = localToWorld.MultiplyPoint3x4(vertex);
                    Vector3 viewPos = Camera.main.WorldToViewportPoint(worldPos);
                    
                    if (viewPos.z > 0 && viewPos.x >= 0 && viewPos.x <= 1 && viewPos.y >= 0 && viewPos.y <= 1)
                    {
                        depthPoints.Add(worldPos);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Scene depth estimation error: {ex.Message}");
        }
    }
    
    private SpatialPosition EstimateSpatialPositionFromFace(Face face)
    {
        SpatialPosition spatialPosition = new SpatialPosition();
        
        try
        {
            Vector2 faceCenter = face.faceRectangle.Center;
            
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(faceCenter.x, faceCenter.y, 0));
            Vector3 estimatedPosition = Vector3.zero;
            float estimatedDistance = 0f;
            float confidenceScore = 0f;
            
            if (depthPoints.Count > 0)
            {
                List<Vector3> nearbyPoints = depthPoints.OrderBy(p => 
                    Vector3.Distance(Camera.main.WorldToScreenPoint(p), new Vector3(faceCenter.x, faceCenter.y, 0)))
                    .Take(10)
                    .ToList();
                    
                if (nearbyPoints.Count > 0)
                {
                    estimatedPosition = nearbyPoints.Aggregate(Vector3.zero, (sum, point) => sum + point) / nearbyPoints.Count;
                    estimatedDistance = Vector3.Distance(Camera.main.transform.position, estimatedPosition);
                    
                    float averageDeviation = nearbyPoints.Sum(p => 
                        Mathf.Abs(Vector3.Distance(Camera.main.transform.position, p) - estimatedDistance)) / nearbyPoints.Count;
                        
                    confidenceScore = 1f - Mathf.Clamp01(averageDeviation / estimatedDistance);
                }
                else
                {
                    estimatedDistance = 2f;  // Default fallback distance
                    estimatedPosition = ray.GetPoint(estimatedDistance);
                    confidenceScore = 0.5f;
                }
            }
            else
            {
                estimatedDistance = 2f;  // Default fallback distance
                estimatedPosition = ray.GetPoint(estimatedDistance);
                confidenceScore = 0.3f;
            }
            
            spatialPosition.position = estimatedPosition;
            spatialPosition.distanceFromCamera = estimatedDistance;
            spatialPosition.confidenceScore = confidenceScore;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Spatial position estimation error: {ex.Message}");
            spatialPosition.position = Camera.main.transform.position + Camera.main.transform.forward * 2f;
            spatialPosition.distanceFromCamera = 2f;
            spatialPosition.confidenceScore = 0.1f;
        }
        
        return spatialPosition;
    }
    
    private float CalculateAudioVolumeFromEnvironment()
    {
        float baseVolume = 1.0f;
        
        try
        {
            float averageDistance = 0f;
            
            if (depthPoints.Count > 0)
            {
                averageDistance = depthPoints.Average(p => Vector3.Distance(Camera.main.transform.position, p));
                
                if (averageDistance < 1f)
                {
                    return Mathf.Lerp(0.7f, 0.9f, averageDistance);  // Quieter in close spaces
                }
                else if (averageDistance > 3f)
                {
                    return Mathf.Lerp(1.0f, 1.5f, Mathf.Min(1f, (averageDistance - 3f) / 5f));  // Louder in open spaces
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Audio volume calculation error: {ex.Message}");
        }
        
        return baseVolume;
    }
}

public class OCRObject
{
    public string language { get; set; }
    public string textAngle { get; set; }
    public string orientation { get; set; }
    public string text { get; set; }
}
