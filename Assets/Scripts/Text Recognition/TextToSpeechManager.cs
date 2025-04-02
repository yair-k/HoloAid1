using HoloToolkit.Unity;
using System;
using UnityEngine;

#if WINDOWS_UWP
using Windows.Foundation;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using System.Linq;
using System.Threading.Tasks;
#endif

public enum TextToSpeechVoice
    {

        Default,

        David,

        Mark,

        Zira,
    }

    public class TextToSpeechManager : IntervalWorkQueue
    {

        [Tooltip("The audio source where speech will be played.")]
        [SerializeField]
        public AudioSource ttsmAudioSource;

        [Tooltip("The voice that will be used to generate speech.")]
        [SerializeField]
        private TextToSpeechVoice voice;

        [Tooltip("Select default value of text-to-speech.")]
        public bool TextToSpeechOn;

        public AudioSource AudioSource
        {
            get
            {
                return (this.ttsmAudioSource);
            }
            set
            {
                this.ttsmAudioSource = value;
            }
        }
        public TextToSpeechVoice Voice
        {
            get
            {
                return (this.voice);
            }
            set
            {
                this.voice = value;
            }
        }

        public void SpeakSsml(string ssml)
        {

            if (string.IsNullOrEmpty(ssml)) { return; }

    #if WINDOWS_UWP
          PlaySpeech(ssml, this.voice, synthesizer.SynthesizeSsmlToStreamAsync);
    #else
            LogSpeech(ssml);
    #endif
        }

        public void SpeakText(string text)
        {

            if (string.IsNullOrEmpty(text)) { return; }

    #if WINDOWS_UWP
          PlaySpeech(text, this.voice, synthesizer.SynthesizeTextToStreamAsync);
    #else
            LogSpeech(text);
    #endif
        }

        void LogSpeech(string text)
        {
            Debug.LogFormat("Speech not supported in editor. \"{0}\"", text);
        }
        public new void Start()
        {
            base.Start();

            try
            {
                if (ttsmAudioSource == null)
                {
                    Debug.LogError("An AudioSource is required and should be assigned to 'Audio Source' in the inspector.");
                }
                else
                {
    #if WINDOWS_UWP
              this.synthesizer = new SpeechSynthesizer();
    #endif
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Could not start Speech Synthesis");
                Debug.LogException(ex);
            }
        }
        protected override void DoWorkItem(object item)
        {

        }
        protected override bool WorkIsInProgress
        {
            get
            {
    #if WINDOWS_UWP
            return (this.ttsmAudioSource.isPlaying);
    #else
                return (false);
    #endif
            }
        }

#if WINDOWS_UWP
        class SpeechEntry
        {
          public string Text { get; set; }
          public TextToSpeechVoice Voice { get; set; }
          public Func<string, IAsyncOperation<SpeechSynthesisStream>> SpeechGenerator { get; set; }
        }
        private SpeechSynthesizer synthesizer;
        private VoiceInformation voiceInfo;

        void PlaySpeech(
          string text,
          TextToSpeechVoice voice,
          Func<string, IAsyncOperation<SpeechSynthesisStream>> speakFunc)
        {
        Debug.Log("TTSM: Speech started.");      

          if (speakFunc == null)
          {
    Debug.Log("TTSM: SpeakFunc Null");      
            throw new ArgumentNullException(nameof(speakFunc));
          }

          if (synthesizer != null)
          {
            base.AddWorkItem(
              new SpeechEntry()
              {
                Text = text,
                Voice = voice,
                SpeechGenerator = speakFunc
              }
            );
          }
          else
          {
            Debug.LogErrorFormat("Speech not initialized. \"{0}\"", text);
          }
        }
        void ChangeVoice(TextToSpeechVoice voice)
        {

          if (voice != TextToSpeechVoice.Default)
          {

            var voiceName = Enum.GetName(typeof(TextToSpeechVoice), voice);

            if ((voiceInfo == null) || (!voiceInfo.DisplayName.Contains(voiceName)))
            {

              voiceInfo = SpeechSynthesizer.AllVoices.Where(v => v.DisplayName.Contains(voiceName)).FirstOrDefault();

              if (voiceInfo != null)
              {
                synthesizer.Voice = voiceInfo;
              }
              else
              {
                Debug.LogErrorFormat("TTS voice {0} could not be found.", voiceName);
              }
            }
          }
        }
#endif 

    public void EnterSpeechMode()
    {
        Debug.Log("TTSM: Speech mode entered.");
        TextToSpeechOn = true;
        this.SpeakText("Entered Speech Mode");
    }

    public void ExitSpeechMode()
    {
        Debug.Log("TTSM: Speech mode exited.");
        TextToSpeechOn = false;
        this.SpeakText("Exited Speech Mode");
    }
}