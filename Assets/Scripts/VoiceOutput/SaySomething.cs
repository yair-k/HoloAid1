using HoloToolkit.Unity;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;

public class SaySomething : MonoBehaviour
{
    public TextToSpeech textToSpeech;
    
    [Range(0.1f, 3.0f)]
    [Tooltip("Volume scale for voice output")]
    public float volumeScale = 1.0f;
    
    [Range(0.5f, 2.0f)]
    [Tooltip("Pitch factor for voice output")]
    public float pitchFactor = 1.0f;
    
    [Tooltip("If true, will automatically queue speech if already speaking")]
    public bool autoQueueSpeech = true;
    
    private Queue<string> speechQueue = new Queue<string>();
    private bool isSpeaking = false;
    private bool isInitialized = false;
    
    public bool IsReady { get; private set; }
    
    void Awake()
    {
        InitializeVoice();
    }
    
    private async void InitializeVoice()
    {
        try
        {
            // Wait for a frame to allow other systems to initialize
            await Task.Yield();
            IsReady = true;
            isInitialized = true;
            Debug.Log("Voice output system initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize voice system: {ex.Message}");
            IsReady = false;
        }
    }
  
    void Start()
    {
        if (textToSpeech != null)
        {
            SpeakWithSettings("Welcome to HoloAid. Your personal AI powered holographic assistant!");
        }
        else
        {
            Debug.LogError("TextToSpeech component not assigned to SaySomething");
        }
    }

    void Update()
    {
        // Process the speech queue if we're not currently speaking and there are queued messages
        if (!isSpeaking && speechQueue.Count > 0 && textToSpeech != null)
        {
            string nextSpeech = speechQueue.Dequeue();
            SpeakImmediate(nextSpeech);
        }
    }
    
    public void SpeakWithSettings(string message)
    {
        if (!isInitialized)
        {
            Debug.LogWarning("Voice system not yet initialized. Message will be queued.");
        }
        
        if (string.IsNullOrEmpty(message))
        {
            return;
        }
        
        if (isSpeaking && autoQueueSpeech)
        {
            // Queue the speech if we're already speaking
            speechQueue.Enqueue(message);
            Debug.Log($"Queued speech: {message}. Queue size: {speechQueue.Count}");
        }
        else
        {
            SpeakImmediate(message);
        }
    }
    
    private void SpeakImmediate(string message)
    {
        try
        {
            isSpeaking = true;
            
            // Apply settings to the TextToSpeech component if possible
            if (textToSpeech.GetType().GetProperty("Volume") != null)
            {
                textToSpeech.GetType().GetProperty("Volume").SetValue(textToSpeech, volumeScale);
            }
            
            if (textToSpeech.GetType().GetProperty("Pitch") != null)
            {
                textToSpeech.GetType().GetProperty("Pitch").SetValue(textToSpeech, pitchFactor);
            }
            
            textToSpeech.StartSpeaking(message);
            StartCoroutine(MonitorSpeechCompletion());
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error during speech output: {ex.Message}");
            isSpeaking = false;
        }
    }
    
    private IEnumerator MonitorSpeechCompletion()
    {
        // Wait for a reasonable time for speech to complete
        // This is approximate since we don't have direct completion callbacks
        if (textToSpeech != null)
        {
            // Estimate speech duration (approx 10 characters per second)
            string currentSpeech = textToSpeech.GetType().GetField("speechText")?.GetValue(textToSpeech) as string;
            float duration = (currentSpeech?.Length ?? 0) * 0.1f;
            duration = Mathf.Max(duration, 1.5f); // Minimum duration
            
            yield return new WaitForSeconds(duration);
        }
        
        isSpeaking = false;
    }
    
    public void ClearSpeechQueue()
    {
        speechQueue.Clear();
    }
}
