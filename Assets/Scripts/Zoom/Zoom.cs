// Starts the default camera and assigns the texture to the current renderer
using UnityEngine;
using System.Collections;
using System;
using System.Threading.Tasks;

public class Zoom : MonoBehaviour {
  WebCamTexture webcamTexture;
  private bool isInitializing = false;
  
  [Range(1, 10)]
  [Tooltip("Zoom magnification factor")]
  public float zoomFactor = 2.0f;
  
  [Tooltip("The desired frame rate for the webcam")]
  public int desiredFrameRate = 30;
  
  [Tooltip("The desired resolution width for the webcam")]
  public int desiredWidth = 1280;
  
  [Tooltip("The desired resolution height for the webcam")]
  public int desiredHeight = 720;
  
  [Tooltip("Automatically enhance image brightness")]
  public bool enhanceBrightness = false;
  
  [Range(0.5f, 2.0f)]
  [Tooltip("Brightness enhancement factor")]
  public float brightnessEnhancement = 1.2f;
  
  private bool _isReady = false;
  public bool IsReady { get { return _isReady && webcamTexture != null && webcamTexture.isPlaying; } }
  
  private Renderer _renderer;

  void Awake()
  {
    _renderer = GetComponent<Renderer>();
    if (_renderer == null)
    {
      Debug.LogError("Zoom requires a Renderer component");
    }
  }

  void Start() {
    Debug.Log("Initiating Zoom");
    InitializeWebcam();
  }
  
  private async void InitializeWebcam()
  {
    if (isInitializing) return;
    
    isInitializing = true;
    _isReady = false;
    
    try
    {
      // Wait for a frame to allow other systems to initialize
      await Task.Yield();
      
      WebCamDevice[] devices = WebCamTexture.devices;
      if (devices.Length == 0)
      {
        Debug.LogError("No webcam devices found");
        isInitializing = false;
        return;
      }
      
      // Find the rear camera if available
      WebCamDevice selectedDevice = devices[0];
      for (int i = 0; i < devices.Length; i++)
      {
        if (!devices[i].isFrontFacing)
        {
          selectedDevice = devices[i];
          break;
        }
      }
      
      Debug.Log($"Selected camera: {selectedDevice.name}");
      
      // Create texture with desired settings
      webcamTexture = new WebCamTexture(
        selectedDevice.name, 
        desiredWidth, 
        desiredHeight, 
        desiredFrameRate
      );
      
      if (_renderer != null)
      {
        _renderer.material.mainTexture = webcamTexture;
      }
      
      webcamTexture.Play();
      
      // Wait to ensure camera is started
      int attemptCount = 0;
      while (!webcamTexture.isPlaying && attemptCount < 10)
      {
        await Task.Delay(100);
        attemptCount++;
      }
      
      if (!webcamTexture.isPlaying)
      {
        Debug.LogError("Failed to start webcam texture after multiple attempts");
        isInitializing = false;
        return;
      }
      
      _isReady = true;
      isInitializing = false;
      Debug.Log("Zoom camera initialized successfully");
    }
    catch (Exception ex)
    {
      Debug.LogError($"Error initializing webcam: {ex.Message}");
      isInitializing = false;
    }
  }

  // zoom in
  void OnEnable() {
    Debug.Log("Enabling Zoom");

    if (webcamTexture != null && !webcamTexture.isPlaying)
    {
      webcamTexture.Play();
      StartCoroutine(WaitForCameraReady());
    }
  }
  
  private IEnumerator WaitForCameraReady()
  {
    int attempts = 0;
    while (!webcamTexture.isPlaying && attempts < 10)
    {
      yield return new WaitForSeconds(0.1f);
      attempts++;
    }
    
    if (webcamTexture.isPlaying)
    {
      _isReady = true;
      Debug.Log("Camera is ready");
    }
    else
    {
      Debug.LogWarning("Camera failed to start after multiple attempts");
    }
  }

  // stop
  void OnDisable() {
    Debug.Log("Disabling Zoom");

    if (webcamTexture != null && webcamTexture.isPlaying)
    {
      webcamTexture.Stop();
    }
    _isReady = false;
  }

  public void Toggle() {
    this.gameObject.SetActive(!this.gameObject.activeSelf);
  }
  
  void Update()
  {
    // Apply any runtime adjustments to the camera if needed
    if (webcamTexture != null && webcamTexture.isPlaying && _renderer != null)
    {
      // You could apply shader parameters here for zoom effects
      if (_renderer.material.HasProperty("_ZoomFactor"))
      {
        _renderer.material.SetFloat("_ZoomFactor", zoomFactor);
      }
      
      if (enhanceBrightness && _renderer.material.HasProperty("_Brightness"))
      {
        _renderer.material.SetFloat("_Brightness", brightnessEnhancement);
      }
    }
  }

  public byte[] GetImageData() {
    if (webcamTexture == null || !webcamTexture.isPlaying) {
      Debug.LogError("Trying to get Image while Webcamtexture is not running!");
      return null;
    }

    try {
      Texture2D snap = new Texture2D(webcamTexture.width, webcamTexture.height);
      snap.SetPixels(webcamTexture.GetPixels());
      snap.Apply();
      return ImageConversion.EncodeToPNG(snap);
    }
    catch (Exception ex) {
      Debug.LogError($"Error capturing image data: {ex.Message}");
      return null;
    }
  }
  
  public byte[] GetEnhancedImageData(PhotoCaptureHandler.CaptureMode mode)
  {
    if (webcamTexture == null || !webcamTexture.isPlaying) {
      Debug.LogError("Trying to get enhanced image while Webcamtexture is not running!");
      return null;
    }
    
    try {
      Texture2D snap = new Texture2D(webcamTexture.width, webcamTexture.height);
      snap.SetPixels(webcamTexture.GetPixels());
      
      // Apply enhancements based on capture mode
      Color[] pixels = snap.GetPixels();
      
      switch (mode)
      {
        case PhotoCaptureHandler.CaptureMode.HighContrast:
          // Enhance contrast for OCR
          EnhanceContrast(pixels);
          break;
        case PhotoCaptureHandler.CaptureMode.HDR:
          // Enhance dynamic range
          EnhanceHDR(pixels);
          break;
        default:
          // Standard enhancement
          EnhanceBrightness(pixels);
          break;
      }
      
      snap.SetPixels(pixels);
      snap.Apply();
      return ImageConversion.EncodeToPNG(snap);
    }
    catch (Exception ex) {
      Debug.LogError($"Error enhancing image: {ex.Message}");
      return GetImageData(); // Fallback to regular image
    }
  }
  
  private void EnhanceContrast(Color[] pixels)
  {
    if (pixels == null || pixels.Length == 0) return;
    
    // Simple contrast enhancement
    float contrastFactor = 1.5f;
    for (int i = 0; i < pixels.Length; i++)
    {
      Color c = pixels[i];
      float luminance = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
      
      // Adjust contrast
      float adjusted = (luminance - 0.5f) * contrastFactor + 0.5f;
      adjusted = Mathf.Clamp01(adjusted);
      
      if (luminance > 0.5f)
      {
        // Brighten bright areas
        pixels[i] = Color.Lerp(c, Color.white, 0.2f);
      }
      else
      {
        // Darken dark areas
        pixels[i] = Color.Lerp(c, Color.black, 0.2f);
      }
    }
  }
  
  private void EnhanceHDR(Color[] pixels)
  {
    if (pixels == null || pixels.Length == 0) return;
    
    // Simple HDR-like enhancement
    for (int i = 0; i < pixels.Length; i++)
    {
      Color c = pixels[i];
      // Increase dynamic range
      pixels[i] = new Color(
        Mathf.Pow(c.r, 0.8f),
        Mathf.Pow(c.g, 0.8f),
        Mathf.Pow(c.b, 0.8f),
        c.a
      );
    }
  }
  
  private void EnhanceBrightness(Color[] pixels)
  {
    if (pixels == null || pixels.Length == 0) return;
    
    for (int i = 0; i < pixels.Length; i++)
    {
      Color c = pixels[i];
      pixels[i] = new Color(
        Mathf.Clamp01(c.r * brightnessEnhancement),
        Mathf.Clamp01(c.g * brightnessEnhancement),
        Mathf.Clamp01(c.b * brightnessEnhancement),
        c.a
      );
    }
  }
}
