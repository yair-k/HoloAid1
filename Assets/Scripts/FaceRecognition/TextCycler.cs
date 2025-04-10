﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TextCycler : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    public TextMesh text;
    public List<string> strings;
    public float displayTimePerItem = 4f;

    float t = 0;
    bool empty = true;
    void Update()
    {
        t += Time.deltaTime;
        if (empty || t >= displayTimePerItem)
        {
            t = 0;

            if (strings.Count > 0) {
                text.text = strings[0];
                strings.RemoveAt(0);
                empty = false;
            } else {
                text.text = "";
                empty = true;
            }
        }
    }
}
