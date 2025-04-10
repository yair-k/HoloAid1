﻿using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Web;
using WWUtils.Audio;
using UnityEngine.Networking;
using System.Text;

public class TextToSpeechGoogle : MonoBehaviour
{

    public AudioSource audioSourceFinal;
    public double speakingRate = 1;
    [HideInInspector]
    public float clipLength = 1;

    struct ClipData
    {
        public int samples;
    }

    const int HEADER_SIZE = 44;

    private int minFreq;
    private int maxFreq;

    private bool micConnected = false;

    private AudioSource goAudioSource;

    string url = "https://texttospeech.googleapis.com/v1beta1/text:synthesize?&key=";

    void Start()
    {

    }

    public void playTextGoogle(String mainText)
    {

        var header = new Dictionary<string, string>() {
            { "Content-Type", "application/json" }
        };

        string json = "{ \"input\": {\"text\":\" " + mainText + "\"},\"voice\": {\"languageCode\":\"en-US\"}, \"audioConfig\": {\"audioEncoding\":\"LINEAR16\",\"speakingRate\":" + speakingRate + "}}";

        var data = Encoding.UTF8.GetBytes(json);
        WWW www = new WWW(url, data, header);

        while (!www.isDone)
        {
            continue;
        }

        string temp = (string)www.text;

        string[] words = temp.Split('"');
        string decodeThis = words[3];
        byte[] decodedBytes = Convert.FromBase64String(decodeThis);
        WAV wav = new WAV(decodedBytes);

        AudioClip audioClip = AudioClip.Create("testSound", wav.SampleCount, 1, wav.Frequency, false);
        audioClip.SetData(wav.LeftChannel, 0);
        audioSourceFinal.clip = audioClip;
        audioSourceFinal.Play();

        clipLength = audioClip.length;

    }

    public void increaseSpeechRate()
    {
        speakingRate += .25;
    }

    public void decreaseSpeechRate()
    {
        speakingRate -= .25;
    }
}
