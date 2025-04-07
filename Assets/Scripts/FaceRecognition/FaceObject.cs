using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FaceObject
{
    public List<Face> faces { get; set; } = new List<Face>();
    public float confidenceThreshold { get; set; } = 0.6f;
    public Vector3 sceneDepthEstimate { get; set; }
    public float proximityFactor { get; set; }
}

public class Face
{
    public string faceId { get; set; }
    public FaceRectangle faceRectangle { get; set; }
    public FaceAttributes faceAttributes { get; set; }
    public EmotionAttributes emotionAttributes { get; set; }
    public SpatialPosition spatialPosition { get; set; }
    public float recognitionConfidence { get; set; }
    
    public string DominantEmotion => emotionAttributes?.EmotionScores
        .OrderByDescending(e => e.Value)
        .FirstOrDefault().Key;
        
    public float EmotionIntensity => emotionAttributes?.EmotionScores
        .OrderByDescending(e => e.Value)
        .FirstOrDefault().Value ?? 0f;
}

public class FaceRectangle
{
    public int top { get; set; }
    public int left { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    
    public Vector2 Center => new Vector2(left + width / 2, top + height / 2);
    public float Area => width * height;
}

public class FaceAttributes
{
    public int gender { get; set; }
    public float age { get; set; }
    public FacialHair facialHair { get; set; }
    public HeadPose headPose { get; set; }
    public float blurLevel { get; set; }
    public float illuminationLevel { get; set; }
    
    public string GenderString => gender == 0 ? "male" : "female";
}

public class EmotionAttributes
{
    public float anger { get; set; }
    public float contempt { get; set; }
    public float disgust { get; set; }
    public float fear { get; set; }
    public float happiness { get; set; }
    public float neutral { get; set; }
    public float sadness { get; set; }
    public float surprise { get; set; }
    
    public Dictionary<string, float> EmotionScores => new Dictionary<string, float>
    {
        { "neutral", neutral },
        { "angry", anger },
        { "argumentative", contempt },
        { "disgusted", disgust },
        { "scared", fear },
        { "happy", happiness },
        { "sad", sadness },
        { "surprised", surprise }
    };
    
    public string GetDescriptiveEmotion(float intensityThreshold = 0.5f)
    {
        var emotion = EmotionScores.OrderByDescending(e => e.Value).First();
        string intensityDescription = emotion.Value < intensityThreshold ? "slightly " : 
                                     (emotion.Value > 0.8f ? "very " : "");
        return $"{intensityDescription}{emotion.Key}";
    }
}

public class HeadPose
{
    public float pitch { get; set; }
    public float roll { get; set; }
    public float yaw { get; set; }
    
    public Quaternion ToQuaternion()
    {
        return Quaternion.Euler(pitch, yaw, roll);
    }
}

public class FacialHair
{
    public float moustache { get; set; }
    public float beard { get; set; }
    public float sideburns { get; set; }
    
    public bool HasFacialHair => moustache > 0.5f || beard > 0.5f || sideburns > 0.5f;
    public string Description => HasFacialHair ? "with facial hair" : "";
}

public class SpatialPosition
{
    public Vector3 position { get; set; }
    public float distanceFromCamera { get; set; }
    public float confidenceScore { get; set; }
    
    public float CalculateAudioIntensity(float baseIntensity = 1.0f, float attenuationFactor = 0.1f)
    {
        return baseIntensity * Mathf.Exp(-attenuationFactor * distanceFromCamera);
    }
}
