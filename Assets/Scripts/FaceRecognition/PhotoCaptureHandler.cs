using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.WSA;
using UnityEngine.XR.WSA.WebCam;

public class PhotoCaptureHandler : MonoBehaviour
{
    private PhotoCapture photoCapture = null;
    private SpatialCoordinateSystem coordSystem = null;
    
    public Zoom zoomScript;
    
    [Range(0.5f, 2.0f)]
    public float captureQualityFactor = 1.0f;
    
    [Range(1, 5)]
    public int maxRetryAttempts = 3;
    
    private int retryCount = 0;
    private bool isCaptureInProgress = false;
    private Matrix4x4 cameraToWorldMatrix;
    private Matrix4x4 projectionMatrix;
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    
    private CameraParameters preferredParameters;
    private Resolution selectedResolution;
    
    private delegate void PhotoDataCallback(byte[] imageData);
    private PhotoDataCallback currentCallback;
    
    private void Awake()
    {
        InitializeCamera();
    }
    
    private async void InitializeCamera()
    {
        try
        {
            WorldManager.GetNativeISpatialCoordinateSystemPtr(out IntPtr ptr);
            coordSystem = Marshal.GetObjectForIUnknown(ptr) as SpatialCoordinateSystem;
            
            cameraPosition = Camera.main.transform.position;
            cameraRotation = Camera.main.transform.rotation;
            cameraToWorldMatrix = Camera.main.cameraToWorldMatrix;
            projectionMatrix = Camera.main.projectionMatrix;
            
            await Task.Yield();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize camera references: {ex.Message}");
        }
    }
    
    public void DoFaceRecognition()
    {
        if (isCaptureInProgress)
        {
            Globals.instance.textToSpeech.StartSpeaking("Capture already in progress. Please wait.");
            return;
        }
        
        Globals.instance.textToSpeech.StartSpeaking("Analyzing faces in view.");
        currentCallback = ExecuteFaceDetection;
        StartCapture(CaptureMode.Standard);
    }
    
    public void DoOCR()
    {
        if (isCaptureInProgress)
        {
            Globals.instance.textToSpeech.StartSpeaking("Capture already in progress. Please wait.");
            return;
        }
        
        Globals.instance.textToSpeech.StartSpeaking("Reading text in view.");
        currentCallback = ExecuteOCR;
        StartCapture(CaptureMode.HighContrast);
    }
    
    public void DoDepthAnalysis()
    {
        if (isCaptureInProgress)
        {
            Globals.instance.textToSpeech.StartSpeaking("Analysis already in progress. Please wait.");
            return;
        }
        
        Globals.instance.textToSpeech.StartSpeaking("Analyzing environment depth.");
        currentCallback = ExecuteDepthMapping;
        StartCapture(CaptureMode.HDR);
    }
    
    private void StartCapture(CaptureMode mode)
    {
        isCaptureInProgress = true;
        retryCount = 0;
        
        if (IsZoomActive())
        {
            ProcessZoomedFrame(mode);
        }
        else
        {
            StartPhotoCapture(mode);
        }
    }
    
    private bool IsZoomActive()
    {
        return zoomScript != null && zoomScript.gameObject.activeInHierarchy && zoomScript.IsReady;
    }
    
    private void ProcessZoomedFrame(CaptureMode mode)
    {
        try
        {
            byte[] imageData = zoomScript.GetEnhancedImageData(mode);
            if (imageData != null && imageData.Length > 0)
            {
                ExecuteCallback(imageData);
            }
            else
            {
                Debug.LogWarning("Zoom camera returned empty image data. Falling back to standard capture.");
                StartPhotoCapture(mode);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing zoomed frame: {ex.Message}");
            StartPhotoCapture(mode);
        }
    }
    
    private void StartPhotoCapture(CaptureMode mode)
    {
        try
        {
            PhotoCapture.CreateAsync(false, OnPhotoCaptureCreated);
            ConfigureCameraParameters(mode);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start photo capture: {ex.Message}");
            HandleCaptureError("Camera initialization failed.");
        }
    }
    
    private void ConfigureCameraParameters(CaptureMode mode)
    {
        preferredParameters = new CameraParameters();
        preferredParameters.hologramOpacity = 0.0f;
        preferredParameters.cameraResolutionWidth = 0;
        preferredParameters.cameraResolutionHeight = 0;
        
        switch (mode)
        {
            case CaptureMode.HighContrast:
                preferredParameters.pixelFormat = CapturePixelFormat.BGRA32;
                break;
            case CaptureMode.HDR:
                preferredParameters.pixelFormat = CapturePixelFormat.BGRA32;
                break;
            default:
                preferredParameters.pixelFormat = CapturePixelFormat.JPEG;
                break;
        }
    }
    
    private void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        if (captureObject == null)
        {
            HandleCaptureError("Failed to initialize camera.");
            return;
        }
        
        photoCapture = captureObject;
        
        try
        {
            selectedResolution = PhotoCapture.SupportedResolutions
                .OrderByDescending(res => res.width * res.height)
                .First();
                
            preferredParameters.cameraResolutionWidth = selectedResolution.width;
            preferredParameters.cameraResolutionHeight = selectedResolution.height;
            
            Matrix4x4 worldToCameraMatrix = cameraToWorldMatrix.inverse;
            
            captureObject.StartPhotoModeAsync(preferredParameters, OnPhotoModeStarted);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error configuring photo capture: {ex.Message}");
            HandleCaptureError("Camera configuration failed.");
            ReleasePhotoCapture();
        }
    }
    
    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        if (!result.success)
        {
            HandleCaptureError("Failed to start camera mode.");
            ReleasePhotoCapture();
            return;
        }
        
        try
        {
            photoCapture.TakePhotoAsync(OnCapturedPhotoToMemory);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error taking photo: {ex.Message}");
            HandleCaptureError("Photo capture failed.");
            ReleasePhotoCapture();
        }
    }
    
    private void OnCapturedPhotoToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame photoCaptureFrame)
    {
        if (!result.success)
        {
            RetryOrFail("Photo capture failed.");
            return;
        }
        
        try
        {
            List<byte> imageBufferList = new List<byte>();
            photoCaptureFrame.CopyRawImageDataIntoBuffer(imageBufferList);
            
            if (imageBufferList.Count > 0)
            {
                Matrix4x4 cameraToWorldMatrix;
                photoCaptureFrame.TryGetCameraToWorldMatrix(out cameraToWorldMatrix);
                
                Matrix4x4 projectionMatrix;
                photoCaptureFrame.TryGetProjectionMatrix(out projectionMatrix);
                
                if (projectionMatrix != Matrix4x4.identity)
                {
                    this.projectionMatrix = projectionMatrix;
                }
                
                if (cameraToWorldMatrix != Matrix4x4.identity)
                {
                    this.cameraToWorldMatrix = cameraToWorldMatrix;
                }
                
                ExecuteCallback(imageBufferList.ToArray());
            }
            else
            {
                RetryOrFail("Camera returned empty image.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing captured frame: {ex.Message}");
            RetryOrFail("Image processing error.");
        }
        finally
        {
            ReleasePhotoCapture();
        }
    }
    
    private void ReleasePhotoCapture()
    {
        if (photoCapture != null)
        {
            photoCapture.StopPhotoModeAsync(OnStoppedPhotoMode);
        }
    }
    
    private void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        photoCapture.Dispose();
        photoCapture = null;
    }
    
    private void RetryOrFail(string errorMessage)
    {
        if (retryCount < maxRetryAttempts)
        {
            retryCount++;
            Debug.LogWarning($"Capture failed. Retry attempt {retryCount}/{maxRetryAttempts}");
            StartPhotoCapture(CaptureMode.Standard);
        }
        else
        {
            HandleCaptureError(errorMessage);
        }
    }
    
    private void ExecuteCallback(byte[] imageData)
    {
        if (currentCallback != null)
        {
            currentCallback(imageData);
        }
        
        isCaptureInProgress = false;
    }
    
    private void ExecuteFaceDetection(byte[] imageData)
    {
        CognitiveServices cognitiveServices = new CognitiveServices();
        StartCoroutine(cognitiveServices.PostToFace(imageData));
    }
    
    private void ExecuteOCR(byte[] imageData)
    {
        CognitiveServices cognitiveServices = new CognitiveServices();
        StartCoroutine(cognitiveServices.PostToOCR(imageData));
    }
    
    private void ExecuteDepthMapping(byte[] imageData)
    {
        EnvironmentAnalyzer environmentAnalyzer = GetComponent<EnvironmentAnalyzer>();
        if (environmentAnalyzer != null)
        {
            environmentAnalyzer.ProcessImageWithDepth(imageData, cameraToWorldMatrix, projectionMatrix);
        }
        else
        {
            Debug.LogError("Environment Analyzer component not found");
            Globals.instance.textToSpeech.StartSpeaking("Environment analysis not available.");
        }
    }
    
    private void HandleCaptureError(string errorMessage)
    {
        Debug.LogError($"Capture error: {errorMessage}");
        Globals.instance.textToSpeech.StartSpeaking("I'm having trouble with the camera. Please try again.");
        isCaptureInProgress = false;
    }
    
    public enum CaptureMode
    {
        Standard,
        HighContrast,
        HDR
    }
}
