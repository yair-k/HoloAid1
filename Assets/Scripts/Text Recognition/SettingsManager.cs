using UnityEngine;
using System.Collections;

public enum UserType
{

    Default,

    AudioOnly,
}

public enum ResolutionSetting
{

    Default,

    High,

    Low,
}

public enum OCRRunSetting
{

    Manual,
}

public enum ApiSetting
{

    Google,

}

public class SettingsManager : MonoBehaviour {
    [Tooltip("Select Mode for Type of User.")]
    public UserType UserSetting;

    [Tooltip("Select Cursor to turn off during audio only mode.")]
    public GameObject CursorObject;

    [Tooltip("Select the resolution setting.")]
    public ResolutionSetting ResolutionLevel;

    [Tooltip("Select interaction setting of OCR.")]
    public OCRRunSetting OCRSetting;

    [Tooltip("Select the OCR API to use.")]
    public ApiSetting ApiType;

    [Tooltip("Maximum Number of icons shown.")]
    public int MaxIcons = 30;

    [Tooltip("Maximum text length (in characters) to read aloud.")]
    public int MaxTextLength = 60;

    void Start () {
        if (UserSetting == UserType.AudioOnly)
        {
            CursorObject.SetActive(false);
        }
    }

    public void SwitchToAudioMode()
    {
        UserSetting = UserType.AudioOnly;
        CursorObject.SetActive(false);
        GetComponent<TextToSpeechManager>().SpeakText("Switched to Audio-only Mode");
    }

    public void SwitchToIconMode()
    {
        UserSetting = UserType.Default;
        CursorObject.SetActive(true);
        GetComponent<TextToSpeechManager>().SpeakText("Switched to Icon Mode");
    }
}