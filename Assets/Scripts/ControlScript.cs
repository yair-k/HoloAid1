using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HoloToolkit.Unity;
using HoloToolkit.Unity.InputModule;
using UnityEngine.UI;
using UnityEngine.XR.WSA.Input;
using System;

[System.Serializable]
public class ControlScript : MonoBehaviour {

    [SerializeField]
#if UNITY_EDITOR
    [Help("\nHOW TO USE CONTROL SCRIPT\n\n1. Attach relevent scripts to the Script Manager\n\n2. Open Control Script in editor\n\n3. Adjust the Core Commands (Obstacles, LocateText, and ReadText functions) to use the attached scripts\n", UnityEditor.MessageType.Info)]
#endif
    [Tooltip("Necessary for help field above to appear.")]
    float inspectorField = 1440f; 

    #region **INITIALIZATION**

    public GestureRecognizer gestureRec;

    [Tooltip("Spatial Processing object.")]
    public GameObject spatialProcessing;

    [Tooltip("Text Manager object.")]
    public GameObject TextManager;

    [Tooltip("Parent object of spawned text beacons.")]
    public GameObject textBeaconManager;

    [Tooltip("Strings to be used with sample text beacons.")]
    public string[] sampleText;

    [Tooltip("Test object will change color when one of the three core commands are input.")]
    public GameObject testCube;

    [Tooltip("This text will change to copy the Debug output.")]
    public GameObject debugText;

    public bool inCoroutine = true;

    private string output = ""; 

    private bool stop = false;

    private bool readTextRunning = false;

    private string repeatText = "No text to repeat.";
    private GameObject repeatBeacon = null;
    private bool isRepeating = false;
    private Physics physics;
    private bool captureTextRunning = false;

    void Start () {

        gestureRec = new GestureRecognizer();

        gestureRec.Tapped += Tap;

        gestureRec.SetRecognizableGestures(GestureSettings.Tap);

        gestureRec.StartCapturingGestures();
        Debug.Log("Gesture Recognizer Initialized");

        TextManager.GetComponent<TextToSpeechGoogle>().playTextGoogle("Ready to assist.");

    }

    private void Update()
    {
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog; 
    }

    void OnDisable()
    {

        gestureRec.Tapped -= Tap;

        Application.logMessageReceived -= HandleLog;
    }

    #endregion

    #region **GESTURE CONTROLS**
    public void Tap(TappedEventArgs args) 
    {

        if (!GetComponentInParent<ObstacleBeaconScript>().obstacleMode)
        {
            GetComponentInParent<ObstacleBeaconScript>().ObstaclesOn();
        }

        else 
        {
            GetComponentInParent<ObstacleBeaconScript>().ObstaclesOff();
        }

    }

    public void HoldCompleted(HoldCompletedEventArgs args)
    {

        CaptureText();
        Debug.Log("Hold Started event registered");
    }

    public void ManipulationCompleted(ManipulationCompletedEventArgs args)
    {

        ReadText();
        Debug.Log("Manipulation Started event registered");
    }

    #endregion

    /