// Based upon https://www.rageagainstthepixel.com/mrtk-dictation-input/

using UnityEngine;
using HoloToolkit.Unity.InputModule;
using UnityEngine.UI;
using UnityEngine.Windows.Speech;
using System;
using System.Collections;
using System.Threading.Tasks;

public class SpeechToText : MonoBehaviour, IDictationHandler
{
  [SerializeField]
  [Range(0.1f, 5f)]
  [Tooltip("The time length in seconds before dictation recognizer session ends due to lack of audio input in case there was no audio heard in the current session.")]
  private float initialSilenceTimeout = 5f;
  
  [SerializeField]
  [Range(5f, 60f)]
  [Tooltip("The time length in seconds before dictation recognizer session ends due to lack of audio input.")]
  private float autoSilenceTimeout = 20f;
  
  [SerializeField]
  [Range(1, 60)]
  [Tooltip("Length in seconds for the manager to listen.")]
  public int recordingTime = 60;
  
  [Tooltip("If true, will automatically try to restart recognition on errors")]
  public bool autoRetryOnError = true;
  
  [Range(1, 5)]
  [Tooltip("Maximum number of retry attempts when errors occur")]
  public int maxRetryAttempts = 3;
  
  private string lastOutput;
  private string speechToTextOutput = string.Empty;
  public string SpeechToTextOutput { get { return speechToTextOutput; } }
  private bool isRecording;
  private int retryCount = 0;
  private bool isInitialized = false;

  public GameObject listener;
  public Text uiText;
  
  public delegate void SpeechRecognizedHandler(string recognizedText);
  public event SpeechRecognizedHandler OnSpeechRecognized;
  
  public bool IsReady { get; private set; }

  private void Awake()
  {
    InitializeSpeechSystem();
  }
  
  private async void InitializeSpeechSystem()
  {
    try
    {
      // Wait for a frame to allow other systems to initialize
      await Task.Yield();
      IsReady = true;
      isInitialized = true;
      Debug.Log("Speech to text system initialized successfully");
    }
    catch (Exception ex)
    {
      Debug.LogError($"Failed to initialize speech system: {ex.Message}");
      IsReady = false;
    }
  }

  public void ToggleRecording()
  {
    if (!isInitialized)
    {
      Debug.LogWarning("Speech system not yet initialized. Please try again in a moment.");
      return;
    }
    
    if (isRecording)
    {
      StopListening();
    }
    else
    {
      StartListening();
    }
  }

  public void StartListening()
  {
    if (!isInitialized)
    {
      Debug.LogWarning("Speech system not yet initialized. Please try again.");
      return;
    }
    
    try
    {
      PhraseRecognitionSystem.Shutdown();
      isRecording = true;
      retryCount = 0;
      Debug.Log("Starting dictation recording");
      StartCoroutine(DictationInputManager.StartRecording(listener, initialSilenceTimeout, autoSilenceTimeout, recordingTime));
    }
    catch (Exception ex)
    {
      Debug.LogError($"Error starting speech recognition: {ex.Message}");
      HandleRecognitionError("Failed to start speech recognition");
    }
  }

  public void StopListening()
  {
    try
    {
      isRecording = false;
      Debug.Log("Stopping dictation recording");
      StartCoroutine(DictationInputManager.StopRecording());
      PhraseRecognitionSystem.Restart();
    }
    catch (Exception ex)
    {
      Debug.LogError($"Error stopping speech recognition: {ex.Message}");
    }
  }

  void IDictationHandler.OnDictationHypothesis(DictationEventData eventData)
  {
    speechToTextOutput = eventData.DictationResult;
  }
  
  void IDictationHandler.OnDictationResult(DictationEventData eventData)
  {
    speechToTextOutput = eventData.DictationResult;
    OnSpeechRecognized?.Invoke(speechToTextOutput);
  }
  
  void IDictationHandler.OnDictationComplete(DictationEventData eventData)
  {
    speechToTextOutput = eventData.DictationResult;
    OnSpeechRecognized?.Invoke(speechToTextOutput);
  }
  
  void IDictationHandler.OnDictationError(DictationEventData eventData)
  {
    isRecording = false;
    speechToTextOutput = eventData.DictationResult;
    Debug.LogError($"Dictation error: {eventData.DictationResult}");
    StartCoroutine(DictationInputManager.StopRecording());

    HandleRecognitionError(eventData.DictationResult);
  }

  private void HandleRecognitionError(string errorMessage)
  {
    if (autoRetryOnError && retryCount < maxRetryAttempts)
    {
      retryCount++;
      Debug.LogWarning($"Recognition failed. Retry attempt {retryCount}/{maxRetryAttempts}");
      StartListening();
    }
    else
    {
      PhraseRecognitionSystem.Restart();
      if (Globals.instance != null && Globals.instance.textToSpeech != null)
      {
        Globals.instance.textToSpeech.StartSpeaking("Speech recognition encountered an issue. Please try again.");
      }
    }
  }

  private void Update()
  {
    if (!string.IsNullOrEmpty(speechToTextOutput) && !lastOutput.Equals(speechToTextOutput))
    {
      Debug.Log($"Speech recognized: {speechToTextOutput}");
      lastOutput = speechToTextOutput;

      if (uiText != null)
      {
        uiText.text = speechToTextOutput;
      }
    }
  }
}