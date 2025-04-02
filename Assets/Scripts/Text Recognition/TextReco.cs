using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;
using System;
using System.IO;

public class TextReco : MonoBehaviour {

    public IEnumerator GoogleRequest(byte[] image)
    {
        Debug.Log("TR: Google Request submitted.");

        string base64Image = Convert.ToBase64String(image);
        Debug.Log("TR: base64 image sent.");

        DownloadHandler download = new DownloadHandlerBuffer();
        Debug.Log("TR: Download Handler set.");

        string json = "{\"requests\": [{\"image\": {\"content\": \"" + base64Image + "\"},\"features\": [{\"type\": \"TEXT_DETECTION\",\"maxResults\": 1}]}]}";
        byte[] content = Encoding.UTF8.GetBytes(json);
        Debug.Log("TR: JSON string created.");

        string url = "https://vision.googleapis.com/v1/images:annotate?key=";

        var header = new Dictionary<string, string>() {
            { "Content-Type", "application/json" }
        };

        var data = Encoding.UTF8.GetBytes(json);

        WWW www = new WWW(url, data, header);

        yield return www;
        Debug.Log("TR: www returned.");

        if (www.error == null)
        {
            string respJson = www.text;
            Debug.Log("TR response JSON received.");

            if (respJson.Contains("textAnnotations") && !respJson.Contains("error"))
            {
                Debug.Log("TR: Text detected.");
                GetComponent<IconManager>().CreateIcons(respJson);
            }

            else if (!respJson.Contains("textAnnotations"))
            {
                {
                    Debug.Log("No text found");
                    this.transform.GetComponent<TextToSpeechGoogle>().playTextGoogle("No text found.");
                }
            }

            else Debug.Log("TR: Strange json response.");
        }

        else
        {
            Debug.Log("TR: www error found: " + www.error);
            if (www.text.Length <= 480)
            {
                Debug.Log("TR: www.text: " + www.text);
            }
            else
            {
                Debug.Log("TR: www.text length = " + www.text.Length);
            }

        }

    }
}