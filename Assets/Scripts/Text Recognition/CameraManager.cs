using UnityEngine;
using UnityEngine.XR.WSA.WebCam;
using System.Linq;
using System;

public class CameraManager : MonoBehaviour
{

    public static Resolution cameraResolution;
    public static Camera raycastCamera;
    public PhotoCapture photoCaptureObject = null;
    public Matrix4x4 managerCameraToWorldMatrix;
    [HideInInspector]
    public Vector3 oldHoloPos;
    [HideInInspector]
    public Quaternion oldHoloRot;

    public GameObject newCameraPrefab;

    [Tooltip("What will play the camera sound when a picture is taken.")]
    public AudioSource cameraAudioSource;

    [Tooltip("What sound will play when a picture is taken.")]
    public AudioClip cameraSound;

    private Boolean detecting { get; set; }                                  

    public string image { get; set; }                                       
    private SettingsManager SettingsManager;

    public void Start()
    {

        SettingsManager = gameObject.GetComponent<SettingsManager>();

        detecting = false;

        var cameraResolutions = PhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height);           

        if (SettingsManager.ResolutionLevel == ResolutionSetting.High)
        {
            cameraResolution = cameraResolutions.First();                   
        }
        else
        {
            foreach (Resolution r in cameraResolutions)
            {
                if (r.width == 1280 || r.height == 720)
                {
                    cameraResolution = r;
                    break;
                }
            }
        }

    }

    public void BeginManualPhotoMode ()
    {
        oldHoloPos = new Vector3(0f, 0f, 0f);
        oldHoloRot = Quaternion.identity;
        Debug.Log("CM: Beginning manual photo mode.");
        SettingsManager = gameObject.GetComponent<SettingsManager>();

        detecting = false;

        var cameraResolutions = PhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height);           

        if (SettingsManager.ResolutionLevel == ResolutionSetting.High)
        {
            cameraResolution = cameraResolutions.First();                   
        }
        else
        {
            foreach (Resolution r in cameraResolutions)
            {
                if (r.width == 1280 || r.height == 720)
                {
                    cameraResolution = r;
                    break;
                }
            }
        }

        if (detecting == true)
        {
            return;
        }

        detecting = true;

        GameObject newCam = GameObject.FindGameObjectWithTag("MainCamera");

        oldHoloPos = newCam.transform.position;

        oldHoloRot = newCam.transform.rotation;

        PhotoCapture.CreateAsync(false, OnPhotoCaptureCreated);
    }

    void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        Debug.Log("CM: OPCC: Photo capture");
        photoCaptureObject = captureObject;

        CameraParameters c = new CameraParameters();
        c.hologramOpacity = 0.0f;
        c.cameraResolutionWidth = cameraResolution.width;
        c.cameraResolutionHeight = cameraResolution.height;
        c.pixelFormat = CapturePixelFormat.BGRA32;

        captureObject.StartPhotoModeAsync(c, OnPhotoModeStarted);
    }

    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        if (result.success)
        {
            BeginOCRProcess();
        }
        else
        {
            StopPhotoMode();
        }
    }

    public void BeginOCRProcess()
    {
        TakePhoto();

    }

    private void TakePhoto()
    {
        Debug.Log("CM: TakePhoto activated.");

        managerCameraToWorldMatrix = GameObject.FindGameObjectWithTag("MainCamera").GetComponent<Camera>().cameraToWorldMatrix;

        photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);

        this.gameObject.transform.GetComponent<TextToSpeechGoogle>().playTextGoogle("Capturing image. Analyzing...");
        Debug.Log("CM: Sound effect played. TakePhoto Async activated.");
    }

    public Matrix4x4 getManagerCamera()
    {
        return managerCameraToWorldMatrix;
    }

    private void OnCapturedPhotoToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame photoCaptureFrame)
    {
        Debug.Log("CM: OnCapturedPhotoToMemory called.");
        if (result.success)
        {

            Texture2D targetTexture = new Texture2D(cameraResolution.width, cameraResolution.height);                               
            photoCaptureFrame.UploadImageDataToTexture(targetTexture);                                                              
            byte[] imageBytes = targetTexture.EncodeToPNG();

            StartCoroutine(GetComponent<TextReco>().GoogleRequest(imageBytes));                                                   

            if (SettingsManager.OCRSetting == OCRRunSetting.Manual)                                                                 
            {
                GetComponent<CameraManager>().StopPhotoMode();
            }
        }
    }

    public void StopPhotoMode()
    {
        photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);

    }

    void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        photoCaptureObject.Dispose();
        photoCaptureObject = null;
    }

    public Camera PositionCamera(Matrix4x4 cameraToWorldMatrix)
    {
        Vector3 position = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);
        Quaternion rotation = Quaternion.LookRotation(-cameraToWorldMatrix.GetColumn(2), cameraToWorldMatrix.GetColumn(1));
        Camera newCamera = Instantiate(newCameraPrefab).GetComponent<Camera>() ;
        newCamera.transform.position = oldHoloPos;
        newCamera.transform.rotation = oldHoloRot;

        return newCamera;
    }
}