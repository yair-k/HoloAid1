using HoloToolkit.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

#if WINDOWS_UWP
using Windows.Foundation;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
#endif

public enum TextToSpeechVoice
{
    Default,
    David,
    Mark,
    Zira,
    Catherine,
    George
}

public enum SpeechPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public class TextToSpeechManager : IntervalWorkQueue
{
    [Header("Core Audio Settings")]
    [SerializeField] private AudioSource ttsmAudioSource;
    [SerializeField] private AudioMixerGroup audioMixerGroup;
    [SerializeField] private TextToSpeechVoice voice;
    [SerializeField] private bool textToSpeechOn = true;
    
    [Header("Speech Parameters")]
    [SerializeField] [Range(0.5f, 2.0f)] private float speechRate = 1.0f;
    [SerializeField] [Range(0.5f, 2.0f)] private float baseVolume = 1.0f;
    [SerializeField] [Range(-12f, 12f)] private float basePitch = 0f;
    [SerializeField] [Range(0.1f, 2.0f)] private float prosodyFactor = 1.0f;
    
    [Header("Environmental Adaptation")]
    [SerializeField] private bool dynamicVolumeAdjustment = true;
    [SerializeField] private bool useReverb = true;
    [SerializeField] private float environmentalAdaptationSpeed = 1.0f;
    [SerializeField] private AnimationCurve volumeResponseCurve;
    
    [Header("Advanced Options")]
    [SerializeField] private bool useEmphasisMarkers = true;
    [SerializeField] private bool useProsodyEnhancement = true;
    [SerializeField] private bool useSpatialPositioning = false;
    [SerializeField] private Transform spatialAnchor;
    [SerializeField] private bool enableAudioEffects = true;
    [SerializeField] private int maxQueueSize = 10;

    private float environmentNoiseFactor = 1.0f;
    private PriorityQueue<AudioClip> speechQueue = new PriorityQueue<AudioClip>();
    private bool isProcessingQueue = false;
    private AudioLowPassFilter lowPassFilter;
    private AudioHighPassFilter highPassFilter;
    private AudioReverbFilter reverbFilter;
    private AudioSource secondaryAudioSource;
    private Dictionary<string, float> acousticParameters = new Dictionary<string, float>();
    private float[] spectrumBuffer;
    
    private const float MIN_AMBIENT_DB = -60f;
    private const float MAX_AMBIENT_DB = 0f;
    private const float AMBIENT_SAMPLING_INTERVAL = 1.0f;
    private const int SPECTRUM_BUFFER_SIZE = 2048;
    private const float MIN_NOISE_THRESHOLD = 0.01f;
    private const float PITCH_VARIATION = 0.1f;
    private const int MAX_SSML_LENGTH = 8000;

    public AudioSource AudioSource => ttsmAudioSource;
    public TextToSpeechVoice Voice { get => voice; set => voice = value; }
    public bool TextToSpeechOn { get => textToSpeechOn; set => textToSpeechOn = value; }
    
    public float SpeechRate 
    {
        get => speechRate;
        set 
        {
            speechRate = Mathf.Clamp(value, 0.5f, 2.0f);
            ApplyVoiceSettings();
        }
    }

    public void SetVolumeScale(float scale)
    {
        baseVolume = Mathf.Clamp(scale, 0.5f, 2.0f);
        UpdateAudioSourceParameters();
    }

    public void SpeakSsml(string ssml, SpeechPriority priority = SpeechPriority.Normal)
    {
        if (string.IsNullOrEmpty(ssml) || !textToSpeechOn) return;
        
        if (ssml.Length > MAX_SSML_LENGTH)
        {
            Debug.LogWarning($"SSML text too long ({ssml.Length} chars). Truncating to {MAX_SSML_LENGTH} chars.");
            ssml = ssml.Substring(0, MAX_SSML_LENGTH);
        }

#if WINDOWS_UWP
        PlaySpeech(ssml, voice, (int)priority, synthesizer.SynthesizeSsmlToStreamAsync);
#else
        LogSpeech(ssml);
#endif
    }

    public void SpeakText(string text, SpeechPriority priority = SpeechPriority.Normal)
    {
        if (string.IsNullOrEmpty(text) || !textToSpeechOn) return;
        
        // Add prosody enhancements for better expressiveness
        if (useProsodyEnhancement)
        {
            text = EnhanceTextWithProsody(text);
        }

#if WINDOWS_UWP
        if (useEmphasisMarkers)
        {
            string ssml = ConvertToEnhancedSsml(text);
            PlaySpeech(ssml, voice, (int)priority, synthesizer.SynthesizeSsmlToStreamAsync);
        }
        else
        {
            PlaySpeech(text, voice, (int)priority, synthesizer.SynthesizeTextToStreamAsync);
        }
#else
        LogSpeech(text);
#endif
    }

    public void SpeakWithPriority(string text)
    {
        if (string.IsNullOrEmpty(text) || !textToSpeechOn) return;
        
        StopCurrentSpeech();
        ClearSpeechQueue();
        SpeakText(text, SpeechPriority.Critical);
    }

    private string EnhanceTextWithProsody(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        // Add subtle variations to improve naturalness
        if (text.EndsWith(".") || text.EndsWith("!") || text.EndsWith("?"))
        {
            return text;
        }
        else
        {
            return text + ".";
        }
    }

    private string ConvertToEnhancedSsml(string text)
    {
        StringBuilder ssmlBuilder = new StringBuilder();
        ssmlBuilder.Append("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">");
        
        // Add prosody settings
        ssmlBuilder.Append($"<prosody rate=\"{speechRate}\" pitch=\"{(basePitch >= 0 ? "+" : "")}{basePitch}Hz\">");
        
        // Process text for emphasis markers and pauses
        string processedText = text
            .Replace("!", "<break time=\"300ms\"/>!")
            .Replace(".", "<break time=\"500ms\"/>.")
            .Replace("?", "<break time=\"400ms\"/>?")
            .Replace(",", "<break time=\"200ms\"/>,");
            
        // Add text with emphasis markers
        ssmlBuilder.Append(processedText);
        ssmlBuilder.Append("</prosody>");
        ssmlBuilder.Append("</speak>");
        
        return ssmlBuilder.ToString();
    }

    void LogSpeech(string text)
    {
        Debug.LogFormat("Speech not supported in editor: \"{0}\"", text);
    }

    public new void Start()
    {
        base.Start();

        try
        {
            InitializeAudioComponents();
            
#if WINDOWS_UWP
            synthesizer = new SpeechSynthesizer();
            ApplyVoiceSettings();
#endif

            if (dynamicVolumeAdjustment)
            {
                spectrumBuffer = new float[SPECTRUM_BUFFER_SIZE];
                StartEnvironmentalAudioSampling();
            }
            
            acousticParameters["roomSize"] = 0.5f;
            acousticParameters["reflectivity"] = 0.5f;
            acousticParameters["reverbDelay"] = 0.1f;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Speech synthesis initialization failed: {ex.Message}");
        }
    }

    private void InitializeAudioComponents()
    {
        if (ttsmAudioSource == null)
        {
            ttsmAudioSource = gameObject.AddComponent<AudioSource>();
            ttsmAudioSource.spatialBlend = useSpatialPositioning ? 1.0f : 0.0f;
            ttsmAudioSource.volume = baseVolume;
            ttsmAudioSource.playOnAwake = false;
            ttsmAudioSource.priority = 0;  // Highest priority
        }
        
        // Add secondary audio source for crossfading and simultaneous playback
        if (secondaryAudioSource == null)
        {
            secondaryAudioSource = gameObject.AddComponent<AudioSource>();
            secondaryAudioSource.spatialBlend = useSpatialPositioning ? 1.0f : 0.0f;
            secondaryAudioSource.volume = 0;
            secondaryAudioSource.playOnAwake = false;
            secondaryAudioSource.priority = 0;
        }
        
        if (audioMixerGroup != null)
        {
            ttsmAudioSource.outputAudioMixerGroup = audioMixerGroup;
            secondaryAudioSource.outputAudioMixerGroup = audioMixerGroup;
        }
        
        if (enableAudioEffects)
        {
            InitializeAudioFilters();
        }
        
        if (volumeResponseCurve == null || volumeResponseCurve.keys.Length == 0)
        {
            volumeResponseCurve = new AnimationCurve(
                new Keyframe(0.0f, 1.0f),
                new Keyframe(0.5f, 1.25f),
                new Keyframe(1.0f, 1.5f)
            );
        }
    }
    
    private void InitializeAudioFilters()
    {
        // Add audio filter components if needed
        if (lowPassFilter == null)
        {
            lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
            lowPassFilter.cutoffFrequency = 22000; // Default to no filtering
            lowPassFilter.lowpassResonanceQ = 1.0f;
        }
        
        if (highPassFilter == null)
        {
            highPassFilter = gameObject.AddComponent<AudioHighPassFilter>();
            highPassFilter.cutoffFrequency = 0;  // Default to no filtering
            highPassFilter.highpassResonanceQ = 1.0f;
        }
        
        if (useReverb && reverbFilter == null)
        {
            reverbFilter = gameObject.AddComponent<AudioReverbFilter>();
            reverbFilter.reverbPreset = AudioReverbPreset.Generic;
            reverbFilter.dryLevel = 0.5f;
            reverbFilter.room = -1000;
            reverbFilter.roomHF = -100;
            reverbFilter.decayTime = 1.5f;
        }
    }

    private void StartEnvironmentalAudioSampling()
    {
        InvokeRepeating("SampleEnvironmentalNoise", 0f, AMBIENT_SAMPLING_INTERVAL);
    }

    private void SampleEnvironmentalNoise()
    {
        try
        {
            AudioListener.GetSpectrumData(spectrumBuffer, 0, FFTWindow.Blackman);
            
            float sum = 0f;
            float peakFrequency = 0f;
            float peakValue = 0f;
            
            for (int i = 0; i < spectrumBuffer.Length; i++)
            {
                float value = spectrumBuffer[i];
                sum += value * value;
                
                // Track dominant frequency
                if (value > peakValue)
                {
                    peakValue = value;
                    peakFrequency = i;
                }
            }
            
            float rms = Mathf.Sqrt(sum / spectrumBuffer.Length);
            if (rms < MIN_NOISE_THRESHOLD) return;
            
            float db = 20f * Mathf.Log10(rms / 0.0001f);
            float normalizedDb = Mathf.InverseLerp(MIN_AMBIENT_DB, MAX_AMBIENT_DB, db);
            
            // Use response curve for non-linear volume adaptation
            environmentNoiseFactor = volumeResponseCurve.Evaluate(normalizedDb);
            
            // Adapt audio filters based on environment
            if (enableAudioEffects)
            {
                AdaptAudioFiltersToEnvironment(normalizedDb, peakFrequency);
            }
            
            UpdateAudioSourceParameters();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Environmental audio sampling error: {ex.Message}");
            CancelInvoke("SampleEnvironmentalNoise");
        }
    }
    
    private void AdaptAudioFiltersToEnvironment(float noiseLevel, float peakFrequency)
    {
        // If high ambient noise, apply low-pass filter to focus on speech frequencies
        if (lowPassFilter != null)
        {
            float targetCutoff = Mathf.Lerp(22000, 5000, noiseLevel * 0.7f);
            lowPassFilter.cutoffFrequency = Mathf.Lerp(lowPassFilter.cutoffFrequency, targetCutoff, Time.deltaTime * environmentalAdaptationSpeed);
        }
        
        // Adapt high-pass filter to reduce low-frequency noise
        if (highPassFilter != null)
        {
            float targetHighPass = Mathf.Lerp(0, 200, noiseLevel * 0.5f);
            highPassFilter.cutoffFrequency = Mathf.Lerp(highPassFilter.cutoffFrequency, targetHighPass, Time.deltaTime * environmentalAdaptationSpeed);
        }
        
        // Adapt reverb based on environment estimation
        if (reverbFilter != null && useReverb)
        {
            // Less reverb in noisy environments to improve clarity
            float targetDecay = Mathf.Lerp(2.0f, 0.8f, noiseLevel);
            reverbFilter.decayTime = Mathf.Lerp(reverbFilter.decayTime, targetDecay, Time.deltaTime * environmentalAdaptationSpeed);
        }
    }
    
    private void UpdateAudioSourceParameters()
    {
        if (ttsmAudioSource != null && ttsmAudioSource.isPlaying)
        {
            ttsmAudioSource.volume = baseVolume * environmentNoiseFactor;
            
            if (useSpatialPositioning && spatialAnchor != null)
            {
                ttsmAudioSource.transform.position = spatialAnchor.position;
            }
        }
    }
    
    protected override void DoWorkItem(object item)
    {
#if WINDOWS_UWP
        if (item is SpeechEntry speechEntry)
        {
            ProcessSpeechEntry(speechEntry);
        }
#endif
    }

#if WINDOWS_UWP
    private async void ProcessSpeechEntry(SpeechEntry entry)
    {
        try
        {
            ChangeVoice(entry.Voice);
            synthesizer.Options.SpeakingRate = speechRate;
            
            if (useProsodyEnhancement)
            {
                // Add subtle pitch variation for more natural speech
                float pitchVariation = UnityEngine.Random.Range(-PITCH_VARIATION, PITCH_VARIATION);
                synthesizer.Options.AudioPitch = basePitch + pitchVariation;
            }
            
            SpeechSynthesisStream stream = await entry.SpeechGenerator(entry.Text);
            AudioClip clip = await SynthesisStreamToAudioClip(stream);
            
            if (clip != null)
            {
                if (entry.Priority >= (int)SpeechPriority.Critical)
                {
                    StopCurrentSpeech();
                    ClearSpeechQueue();
                    PlayAudioClip(clip);
                }
                else if (ttsmAudioSource.isPlaying)
                {
                    speechQueue.Enqueue(clip, entry.Priority);
                    if (!isProcessingQueue)
                    {
                        StartCoroutine(ProcessSpeechQueue());
                    }
                }
                else
                {
                    PlayAudioClip(clip);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Speech synthesis error: {ex.Message}");
        }
    }
    
    private void PlayAudioClip(AudioClip clip)
    {
        ttsmAudioSource.clip = clip;
        ttsmAudioSource.volume = baseVolume * environmentNoiseFactor;
        ttsmAudioSource.pitch = 1.0f;
        ttsmAudioSource.Play();
    }
    
    private System.Collections.IEnumerator ProcessSpeechQueue()
    {
        isProcessingQueue = true;
        
        while (speechQueue.Count > 0)
        {
            yield return new WaitUntil(() => !ttsmAudioSource.isPlaying);
            
            if (speechQueue.Count > 0)
            {
                AudioClip nextClip = speechQueue.Dequeue();
                PlayAudioClip(nextClip);
                
                yield return null;
            }
        }
        
        isProcessingQueue = false;
    }
    
    private async Task<AudioClip> SynthesisStreamToAudioClip(SpeechSynthesisStream stream)
    {
        byte[] buffer = new byte[(int)stream.Size];
        
        using (var inputStream = stream.GetInputStreamAt(0))
        {
            using (var dataReader = new DataReader(inputStream))
            {
                await dataReader.LoadAsync((uint)buffer.Length);
                dataReader.ReadBytes(buffer);
            }
        }
        
        // Extract PCM data from WAV
        int wavHeaderSize = 44;
        int frequency = BitConverter.ToInt32(buffer, 24);
        int channels = BitConverter.ToInt16(buffer, 22);
        int bitsPerSample = BitConverter.ToInt16(buffer, 34);
        int sampleCount = (buffer.Length - wavHeaderSize) / (bitsPerSample / 8) / channels;
        
        float[] audioData = new float[sampleCount * channels];
        int audioIndex = 0;
        
        for (int i = wavHeaderSize; i < buffer.Length; i += (bitsPerSample / 8))
        {
            if (audioIndex >= audioData.Length) break;
            
            if (bitsPerSample == 16)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                audioData[audioIndex++] = sample / 32768f;
            }
            else if (bitsPerSample == 32)
            {
                int sample = BitConverter.ToInt32(buffer, i);
                audioData[audioIndex++] = sample / 2147483648f;
            }
        }
        
        AudioClip audioClip = AudioClip.Create("SpeechClip", sampleCount, channels, frequency, false);
        audioClip.SetData(audioData, 0);
        return audioClip;
    }
    
    private void ApplyVoiceSettings()
    {
        try
        {
            if (synthesizer == null) return;
            
            ChangeVoice(voice);
            synthesizer.Options.SpeakingRate = speechRate;
            
            if (useProsodyEnhancement)
            {
                synthesizer.Options.AudioPitch = basePitch;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to apply voice settings: {ex.Message}");
        }
    }
#endif

    protected override bool WorkIsInProgress
    {
        get
        {
#if WINDOWS_UWP
            return ttsmAudioSource.isPlaying;
#else
            return false;
#endif
        }
    }

#if WINDOWS_UWP
    class SpeechEntry
    {
        public string Text { get; set; }
        public TextToSpeechVoice Voice { get; set; }
        public int Priority { get; set; }
        public Func<string, IAsyncOperation<SpeechSynthesisStream>> SpeechGenerator { get; set; }
    }
    
    private SpeechSynthesizer synthesizer;
    private VoiceInformation voiceInfo;

    void PlaySpeech(string text, TextToSpeechVoice voice, int priority, Func<string, IAsyncOperation<SpeechSynthesisStream>> speakFunc)
    {
        if (speakFunc == null)
        {
            throw new ArgumentNullException(nameof(speakFunc));
        }

        if (synthesizer == null)
        {
            Debug.LogError($"Speech not initialized: \"{text}\"");
            return;
        }
        
        AddWorkItem(new SpeechEntry {
            Text = text,
            Voice = voice,
            Priority = priority,
            SpeechGenerator = speakFunc
        });
    }
    
    void ChangeVoice(TextToSpeechVoice voice)
    {
        if (voice != TextToSpeechVoice.Default)
        {
            string voiceName = Enum.GetName(typeof(TextToSpeechVoice), voice);

            if ((voiceInfo == null) || (!voiceInfo.DisplayName.Contains(voiceName)))
            {
                voiceInfo = SpeechSynthesizer.AllVoices
                    .FirstOrDefault(v => v.DisplayName.Contains(voiceName));

                if (voiceInfo != null)
                {
                    synthesizer.Voice = voiceInfo;
                }
                else
                {
                    Debug.LogWarning($"TTS voice {voiceName} not found. Using default voice.");
                }
            }
        }
    }
#endif 

    public void EnterSpeechMode()
    {
        textToSpeechOn = true;
        SpeakWithPriority("Entered Speech Mode");
    }

    public void ExitSpeechMode()
    {
        textToSpeechOn = false;
        SpeakWithPriority("Exited Speech Mode");
    }
    
    public void StopCurrentSpeech()
    {
        if (ttsmAudioSource != null && ttsmAudioSource.isPlaying)
        {
            ttsmAudioSource.Stop();
        }
    }
    
    public void ClearSpeechQueue()
    {
        speechQueue.Clear();
    }
    
    public bool IsSpeaking()
    {
        return ttsmAudioSource != null && ttsmAudioSource.isPlaying;
    }
    
    public void IncreaseSpeechRate()
    {
        SpeechRate += 0.1f;
    }
    
    public void DecreaseSpeechRate()
    {
        SpeechRate -= 0.1f;
    }
    
    public void SetPitch(float pitchValue)
    {
        basePitch = Mathf.Clamp(pitchValue, -12f, 12f);
        ApplyVoiceSettings();
    }
    
    public void ToggleSpatialAudio(bool enabled)
    {
        useSpatialPositioning = enabled;
        
        if (ttsmAudioSource != null)
        {
            ttsmAudioSource.spatialBlend = useSpatialPositioning ? 1.0f : 0.0f;
        }
        
        if (secondaryAudioSource != null)
        {
            secondaryAudioSource.spatialBlend = useSpatialPositioning ? 1.0f : 0.0f;
        }
    }
    
    public void SetEnvironmentalAdaptationSpeed(float speed)
    {
        environmentalAdaptationSpeed = Mathf.Clamp(speed, 0.1f, 10.0f);
    }
    
    public void SetRoomAcoustics(float size, float reflectivity)
    {
        acousticParameters["roomSize"] = Mathf.Clamp01(size);
        acousticParameters["reflectivity"] = Mathf.Clamp01(reflectivity);
        
        if (reverbFilter != null)
        {
            reverbFilter.decayTime = Mathf.Lerp(0.5f, 5.0f, size);
            reverbFilter.room = Mathf.Lerp(-2000, -500, reflectivity);
        }
    }
    
    private void OnDestroy()
    {
        CancelInvoke();
        StopAllCoroutines();
        
#if WINDOWS_UWP
        if (synthesizer != null)
        {
            synthesizer.Dispose();
            synthesizer = null;
        }
#endif
    }
    
    private class PriorityQueue<T>
    {
        private SortedDictionary<int, Queue<T>> priorityQueue = new SortedDictionary<int, Queue<T>>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        
        public int Count => priorityQueue.Values.Sum(q => q.Count);
        
        public void Enqueue(T item, int priority)
        {
            if (!priorityQueue.TryGetValue(priority, out Queue<T> queue))
            {
                queue = new Queue<T>();
                priorityQueue[priority] = queue;
            }
            
            queue.Enqueue(item);
        }
        
        public T Dequeue()
        {
            if (Count == 0) throw new InvalidOperationException("Queue is empty");
            
            var firstQueue = priorityQueue.First();
            T item = firstQueue.Value.Dequeue();
            
            if (firstQueue.Value.Count == 0)
            {
                priorityQueue.Remove(firstQueue.Key);
            }
            
            return item;
        }
        
        public void Clear()
        {
            priorityQueue.Clear();
        }
    }
}