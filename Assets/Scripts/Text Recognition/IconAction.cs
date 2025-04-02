using HoloToolkit;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;

public class IconAction : MonoBehaviour
{
    [Tooltip("Object to be triggered on click.")]
    public GameObject TriggeredObject;
    [Tooltip("Text that the icon holds.")]
    public String Text;

    void Start()
    {
        if (TriggeredObject == null)
        {
            TriggeredObject = GameObject.Find("Canvas");
        }
    }

    void PerformTagAlong()
    {

        if (TriggeredObject != null)
        {
            GameObject textBox = TriggeredObject.transform.Find("TextBox").gameObject;

            if (textBox != null)
            {
                Text textObject = textBox.GetComponentInChildren<Text>();
                if (textObject == null)
                {
                    Debug.Log("TextObject is null");
                }
                if (Text != null)
                {
                    textObject.text = Text;
                }

                if (GameObject.Find("Managers").GetComponent<TextToSpeechManager>().TextToSpeechOn)
                {
                    if (Text.Length < GameObject.Find("Managers").GetComponent<SettingsManager>().MaxTextLength)
                    {
                        GameObject.Find("Managers").GetComponent<TextToSpeechManager>().SpeakText(Text);
                    } else
                    {
                        GameObject.Find("Managers").GetComponent<TextToSpeechManager>().SpeakText("Text is too long to read.");
                    }
                }

                textBox.gameObject.SetActive(true);
            }
        }
    }

    void HideIcon()
    {

        gameObject.SetActive(false);
    }
}