using Newtonsoft.Json;
using HoloToolkit.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Audio;

public class PlayVoiceMessage : MonoBehaviour
{
    public static PlayVoiceMessage Instance { get; private set; }

    public TextToSpeech ttsManager;
    
    [Range(0.1f, 2.0f)]
    public float baseVolumeIntensity = 1.0f;
    
    [Range(0.05f, 0.5f)]
    public float attenuationFactor = 0.1f;
    
    public AudioMixer audioMixer;
    public AudioSource spatialAudioSource;
    
    private const string baseEndpoint = "https://westus.api.cognitive.microsoft.com/face/v1.0/";
    private const string personGroupId = "0";

    private Dictionary<string, AudioClip> emotionSoundEffects = new Dictionary<string, AudioClip>();
    private Dictionary<string, PersonProfile> knownPersons = new Dictionary<string, PersonProfile>();
    private Dictionary<string, float> lastDetectionTime = new Dictionary<string, float>();
    
    private float[][] gaussianKernel = null;
    private int kernelSize = 5;
    private float sigma = 0.8f;

    void Awake()
    {
        Instance = this;
        InitializeGaussianKernel();
        LoadEmotionSoundEffects();
    }

    private void InitializeGaussianKernel()
    {
        gaussianKernel = new float[kernelSize][];
        for (int i = 0; i < kernelSize; i++)
        {
            gaussianKernel[i] = new float[kernelSize];
        }
        
        float sum = 0f;
        int center = kernelSize / 2;
        
        for (int x = -center; x <= center; x++)
        {
            for (int y = -center; y <= center; y++)
            {
                int i = x + center;
                int j = y + center;
                
                gaussianKernel[i][j] = Mathf.Exp(-(x * x + y * y) / (2 * sigma * sigma));
                sum += gaussianKernel[i][j];
            }
        }
        
        for (int i = 0; i < kernelSize; i++)
        {
            for (int j = 0; j < kernelSize; j++)
            {
                gaussianKernel[i][j] /= sum;
            }
        }
    }
    
    private void LoadEmotionSoundEffects()
    {
        try
        {
            emotionSoundEffects["happy"] = Resources.Load<AudioClip>("SoundEffects/positive_notification");
            emotionSoundEffects["sad"] = Resources.Load<AudioClip>("SoundEffects/negative_notification");
            emotionSoundEffects["surprised"] = Resources.Load<AudioClip>("SoundEffects/alert_notification");
            emotionSoundEffects["angry"] = Resources.Load<AudioClip>("SoundEffects/warning_notification");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load emotion sound effects: {ex.Message}");
        }
    }
    
    public void DeliverAudioFeedback(FaceObject faceObj)
    {
        if (faceObj == null || faceObj.faces == null)
            return;
            
        int numOfFaces = faceObj.faces.Count;
        
        if (numOfFaces == 0)
        {
            PlaySpeechMessage("No people detected in view.");
            return;
        }

        string baseMessage = BuildMultiFaceDescription(faceObj.faces);
        PlaySpeechMessage(baseMessage);
        
        foreach (Face face in faceObj.faces)
        {
            if (face.spatialPosition != null && face.spatialPosition.confidenceScore > 0.7f)
            {
                ModulateAudioByDistance(face);
                PlayEmotionSoundEffect(face);
            }
            
            if (!string.IsNullOrEmpty(face.faceId) && 
                (!lastDetectionTime.ContainsKey(face.faceId) || 
                 Time.time - lastDetectionTime[face.faceId] > 60f))
            {
                lastDetectionTime[face.faceId] = Time.time;
                StartCoroutine(IdentifyFacesAsync(new List<Face> { face }));
            }
        }
    }

    private string BuildMultiFaceDescription(List<Face> faces)
    {
        StringBuilder messageBuilder = new StringBuilder();
        
        if (faces.Count == 1)
        {
            Face face = faces[0];
            string distanceDescription = GetDistanceDescription(face.spatialPosition?.distanceFromCamera ?? 2f);
            string positionDescription = GetRelativePositionDescription(face.spatialPosition?.position ?? Vector3.forward);
            string emotionIntensityDesc = GetEmotionIntensityDescription(face.EmotionIntensity);
            
            messageBuilder.AppendFormat("I see {0} {1} year old {2} {3} {4}, who appears to be {5}{6}.", 
                distanceDescription,
                Math.Round(face.faceAttributes.age),
                face.faceAttributes.GenderString,
                positionDescription,
                face.faceAttributes.facialHair?.Description ?? "",
                emotionIntensityDesc,
                face.DominantEmotion);
        }
        else
        {
            messageBuilder.AppendFormat("I see {0} people. ", faces.Count);
            
            Dictionary<string, int> emotions = new Dictionary<string, int>();
            Dictionary<int, int> genders = new Dictionary<int, int>();
            
            foreach (Face face in faces)
            {
                string dominantEmotion = face.DominantEmotion;
                int gender = face.faceAttributes.gender;
                
                if (!emotions.ContainsKey(dominantEmotion))
                    emotions[dominantEmotion] = 0;
                emotions[dominantEmotion]++;
                
                if (!genders.ContainsKey(gender))
                    genders[gender] = 0;
                genders[gender]++;
            }
            
            if (genders.ContainsKey(0) && genders.ContainsKey(1))
            {
                messageBuilder.AppendFormat("{0} {1} and {2} {3}. ", 
                    genders[0], genders[0] == 1 ? "man" : "men",
                    genders[1], genders[1] == 1 ? "woman" : "women");
            }
            
            KeyValuePair<string, int> dominantGroupEmotion = 
                emotions.OrderByDescending(e => e.Value).First();
                
            if (dominantGroupEmotion.Value > faces.Count / 2)
            {
                messageBuilder.AppendFormat("Most people appear to be {0}. ", dominantGroupEmotion.Key);
            }
            
            Face closestFace = faces.OrderBy(f => f.spatialPosition?.distanceFromCamera ?? float.MaxValue).First();
            messageBuilder.AppendFormat("The closest person is {0} meters away.", 
                Math.Round(closestFace.spatialPosition?.distanceFromCamera ?? 2f, 1));
        }
        
        return messageBuilder.ToString();
    }

    private string GetDistanceDescription(float distance)
    {
        if (distance <= 1.0f)
            return "a nearby";
        else if (distance <= 3.0f)
            return "a";
        else if (distance <= 5.0f)
            return "a distant";
        else
            return "a far away";
    }
    
    private string GetRelativePositionDescription(Vector3 position)
    {
        Vector3 directionToFace = (position - Camera.main.transform.position).normalized;
        float dot = Vector3.Dot(directionToFace, Camera.main.transform.right);
        
        if (dot > 0.5f)
            return "to your right";
        else if (dot < -0.5f)
            return "to your left";
        else
            return "in front of you";
    }
    
    private string GetEmotionIntensityDescription(float intensity)
    {
        if (intensity < 0.4f)
            return "slightly ";
        else if (intensity < 0.7f)
            return "";
        else
            return "very ";
    }
    
    private void ModulateAudioByDistance(Face face)
    {
        if (spatialAudioSource == null || face.spatialPosition == null)
            return;
            
        float distance = face.spatialPosition.distanceFromCamera;
        float intensity = CalculateAudioIntensity(distance);
        
        if (audioMixer != null)
        {
            float dbValue = 20f * Mathf.Log10(intensity);
            audioMixer.SetFloat("MasterVolume", Mathf.Clamp(dbValue, -80f, 20f));
        }
        
        if (face.spatialPosition.confidenceScore > 0.8f)
        {
            spatialAudioSource.transform.position = face.spatialPosition.position;
            spatialAudioSource.spatialBlend = 1.0f;
        }
        else
        {
            spatialAudioSource.spatialBlend = 0.5f;
        }
    }
    
    private float CalculateAudioIntensity(float distance)
    {
        return baseVolumeIntensity * Mathf.Exp(-attenuationFactor * distance);
    }
    
    private void PlayEmotionSoundEffect(Face face)
    {
        if (spatialAudioSource == null || face == null)
            return;
            
        string emotion = face.DominantEmotion;
        float intensity = face.EmotionIntensity;
        
        if (intensity > 0.6f && emotionSoundEffects.ContainsKey(emotion))
        {
            spatialAudioSource.PlayOneShot(emotionSoundEffects[emotion], intensity);
        }
    }
    
    private void PlaySpeechMessage(string message)
    {
        if (ttsManager != null)
        {
            ttsManager.StartSpeaking(message);
        }
    }
    
    public async Task<List<PersonProfile>> IdentifyFacesAsync(List<Face> facesToIdentify)
    {
        List<PersonProfile> identifiedPersons = new List<PersonProfile>();
        
        try
        {
            FacesToIdentify_RootObject requestData = new FacesToIdentify_RootObject
            {
                personGroupId = personGroupId,
                faceIds = facesToIdentify.Select(f => f.faceId).ToList(),
                maxNumOfCandidatesReturned = 1,
                confidenceThreshold = 0.6
            };
            
            string jsonRequest = JsonConvert.SerializeObject(requestData);
            byte[] bodyData = Encoding.UTF8.GetBytes(jsonRequest);
            
            using (UnityWebRequest request = new UnityWebRequest($"{baseEndpoint}identify", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyData);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Ocp-Apim-Subscription-Key", Constants.MCS_FACEKEY);
                
                await request.SendWebRequest();
                
                if (request.isNetworkError || request.isHttpError)
                {
                    Debug.LogError($"Identify network error: {request.error}");
                    return identifiedPersons;
                }
                
                Candidate_RootObject[] identifyResults = 
                    JsonConvert.DeserializeObject<Candidate_RootObject[]>(request.downloadHandler.text);
                
                foreach (var identifyResult in identifyResults)
                {
                    if (identifyResult.candidates.Count > 0)
                    {
                        Candidate candidate = identifyResult.candidates[0];
                        PersonProfile person = await GetPersonProfileAsync(candidate.personId);
                        
                        if (person != null)
                        {
                            identifiedPersons.Add(person);
                            
                            string announcement = $"I recognize {person.name}";
                            if (!string.IsNullOrEmpty(person.notes))
                                announcement += $", {person.notes}";
                                
                            PlaySpeechMessage(announcement);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error identifying faces: {ex.Message}");
        }
        
        return identifiedPersons;
    }
    
    private async Task<PersonProfile> GetPersonProfileAsync(string personId)
    {
        if (knownPersons.ContainsKey(personId))
            return knownPersons[personId];
            
        try
        {
            using (UnityWebRequest request = UnityWebRequest.Get($"{baseEndpoint}persongroups/{personGroupId}/persons/{personId}"))
            {
                request.SetRequestHeader("Ocp-Apim-Subscription-Key", Constants.MCS_FACEKEY);
                await request.SendWebRequest();
                
                if (request.isNetworkError || request.isHttpError)
                {
                    Debug.LogError($"GetPerson network error: {request.error}");
                    return null;
                }
                
                IdentifiedPerson_RootObject personData = 
                    JsonConvert.DeserializeObject<IdentifiedPerson_RootObject>(request.downloadHandler.text);
                    
                PersonProfile profile = new PersonProfile
                {
                    id = personData.personId,
                    name = personData.name,
                    notes = personData.userData
                };
                
                knownPersons[personId] = profile;
                return profile;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error retrieving person profile: {ex.Message}");
            return null;
        }
    }

    public class FacesToIdentify_RootObject
    {
        public string personGroupId { get; set; }
        public List<string> faceIds { get; set; }
        public int maxNumOfCandidatesReturned { get; set; }
        public double confidenceThreshold { get; set; }
    }

    public class Candidate_RootObject
    {
        public string faceId { get; set; }
        public List<Candidate> candidates { get; set; } = new List<Candidate>();
    }

    public class Candidate
    {
        public string personId { get; set; }
        public double confidence { get; set; }
    }

    public class IdentifiedPerson_RootObject
    {
        public string personId { get; set; }
        public string name { get; set; }
        public string userData { get; set; }
    }
    
    public class PersonProfile
    {
        public string id { get; set; }
        public string name { get; set; }
        public string notes { get; set; }
        public DateTime lastSeen { get; set; } = DateTime.Now;
    }
}
