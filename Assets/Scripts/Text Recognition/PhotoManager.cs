using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.XR.WSA.WebCam;

public class PhotoManager : MonoBehaviour
{

    public bool ShowHolograms = true;

    public bool AutoStart = true;

    private PhotoCapture capture;

    private bool isReady = false;

    private string currentImagePath;

    private string pictureFolderPath;

    private void Start()
    {

        if (AutoStart)
            StartCamera();

#if NETFX_CORE
        GetPicturesFolderAsync();
#endif

    }

    public void StartCamera()
    {
        if (isReady)
        {
            Debug.Log("Camera is already running.");
            return;
        }
        Debug.Log("PhotoManger createAsync");
        PhotoCapture.CreateAsync(ShowHolograms, OnPhotoCaptureCreated);
        Debug.Log("After Async");
    }

    public void TakePhoto()
    {
        Debug.Log("takePhoto");
        if (isReady)
        {
            string file = string.Format(@"Image_{0:yyyy-MM-dd_hh-mm-ss-tt}.jpg", DateTime.Now);
            currentImagePath = System.IO.Path.Combine(Application.persistentDataPath, file);
            capture.TakePhotoAsync(currentImagePath, PhotoCaptureFileOutputFormat.JPG, OnCapturedPhotoToDisk);
        }
        else
        {
            Debug.LogWarning("The camera is not yet ready.");
        }
    }

    public void StopCamera()
    {
        if (isReady)
        {
            capture.StopPhotoModeAsync(OnPhotoModeStopped);
        }
    }

#if NETFX_CORE

    private async void GetPicturesFolderAsync() 
    {
        Windows.Storage.StorageLibrary picturesStorage = await Windows.Storage.StorageLibrary.GetLibraryAsync(Windows.Storage.KnownLibraryId.Pictures);
        pictureFolderPath = picturesStorage.SaveFolder.Path;
    }

#endif

    private void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        Debug.Log("OnPhotoCaptureCreated");
        capture = captureObject;

        Resolution resolution = PhotoCapture.SupportedResolutions.OrderByDescending(res => res.width * res.height).First();

        CameraParameters c = new CameraParameters(WebCamMode.PhotoMode);
        c.hologramOpacity = 1.0f;
        c.cameraResolutionWidth = resolution.width;
        c.cameraResolutionHeight = resolution.height;
        c.pixelFormat = CapturePixelFormat.BGRA32;
        Debug.Log("before startAsync");
        capture.StartPhotoModeAsync(c, OnPhotoModeStarted);
    }

    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        Debug.Log("onPhotoModeStarted");
        isReady = result.success;

    }

    private void OnCapturedPhotoToDisk(PhotoCapture.PhotoCaptureResult result)
    {
        if (result.success)
        {

#if NETFX_CORE
            try 
            {
                if(pictureFolderPath != null)
                {
                    System.IO.File.Move(currentImagePath, System.IO.Path.Combine(pictureFolderPath, "Camera Roll", System.IO.Path.GetFileName(currentImagePath)));

                }
                else 
                {

                }
            } 
            catch(Exception e) 
            {

                Debug.Log(e.Message);
            }
#else

            Debug.Log("Saved image at " + currentImagePath);
#endif

        }
        else
        {

            Debug.LogError(string.Format("Failed to save photo to disk ({0})", result.hResult));
        }
    }

    private void OnPhotoModeStopped(PhotoCapture.PhotoCaptureResult result)
    {
        capture.Dispose();
        capture = null;
        isReady = false;

    }

}