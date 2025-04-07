using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObstacleAudio : MonoBehaviour
{
    public float minPulseFrequency = 1f / 5f;
    public float maxPulseFrequency = 8f;
    public AudioSource audioSource;
    public float cameraBoxSize = 2f;
    public float maxPitch = 1.0f;
    public float minPitch = 0.5f;
    public float baseIntensity = 1.0f;
    public float attenuationFactor = 0.5f;
    public AnimationCurve distanceAttenuationCurve;

    private Camera _camera;
    private float _lastPlayTime;
    private float _currentFrequency;
    private Vector3 _lastHeadPosition;
    private Quaternion _lastHeadRotation;
    private float _currentDistanceToObstacle;
    private const float MIN_DISTANCE_THRESHOLD = 0.1f;
    private const float MAX_DISTANCE_THRESHOLD = 10.0f;
    private const float AUDIO_UPDATE_INTERVAL = 0.05f;

    private void Awake()
    {
        if (distanceAttenuationCurve == null || distanceAttenuationCurve.keys.Length == 0)
        {
            distanceAttenuationCurve = new AnimationCurve();
            distanceAttenuationCurve.AddKey(0f, 1f);
            distanceAttenuationCurve.AddKey(1f, 0.5f);
            distanceAttenuationCurve.AddKey(5f, 0.1f);
            distanceAttenuationCurve.AddKey(10f, 0f);
        }
    }

    void Start()
    {
        _camera = Camera.main;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1.0f;
            audioSource.rolloffMode = AudioRolloffMode.Custom;
            audioSource.dopplerLevel = 0.0f;
            audioSource.spread = 15.0f;
            audioSource.minDistance = MIN_DISTANCE_THRESHOLD;
            audioSource.maxDistance = MAX_DISTANCE_THRESHOLD;
        }
        
        _lastHeadPosition = _camera.transform.position;
        _lastHeadRotation = _camera.transform.rotation;
        StartCoroutine(UpdateAudioParameters());
    }

    IEnumerator UpdateAudioParameters()
    {
        while (true)
        {
            UpdateAudioFeedback();
            yield return new WaitForSeconds(AUDIO_UPDATE_INTERVAL);
        }
    }

    void Update()
    {
        _currentDistanceToObstacle = Vector3.Distance(transform.position, _camera.transform.position);
        Vector3 headMovement = _camera.transform.position - _lastHeadPosition;
        float headVelocity = headMovement.magnitude / Time.deltaTime;

        float dynamicPlayInterval = Mathf.Lerp(
            1.0f / maxPulseFrequency, 
            1.0f / minPulseFrequency, 
            Mathf.InverseLerp(MIN_DISTANCE_THRESHOLD, MAX_DISTANCE_THRESHOLD, _currentDistanceToObstacle)
        );

        if (Time.time - _lastPlayTime > dynamicPlayInterval && _currentDistanceToObstacle < MAX_DISTANCE_THRESHOLD)
        {
            if (!audioSource.isPlaying || headVelocity > 0.5f)
            {
                audioSource.pitch = CalculatePitch();
                audioSource.volume = CalculateVolume(_currentDistanceToObstacle);
                audioSource.Play();
                _lastPlayTime = Time.time;
            }
        }

        _lastHeadPosition = _camera.transform.position;
        _lastHeadRotation = _camera.transform.rotation;
    }

    float CalculatePitch()
    {
        float heightDifference = transform.position.y - _camera.transform.position.y;
        
        if (heightDifference >= cameraBoxSize * 0.25f)
        {
            return maxPitch;
        }
        else if (heightDifference <= cameraBoxSize * (-0.75f))
        {
            return minPitch;
        }
        else
        {
            return minPitch + (maxPitch - minPitch) * (heightDifference + 0.75f * cameraBoxSize) / cameraBoxSize;
        }
    }

    float CalculateVolume(float distance)
    {
        return baseIntensity * distanceAttenuationCurve.Evaluate(distance);
    }

    void UpdateAudioFeedback()
    {
        if (audioSource != null)
        {
            float obstacleImportance = CalculateObstacleImportance();
            audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, GenerateAttenuationCurve(obstacleImportance));
        }
    }
    
    float CalculateObstacleImportance()
    {
        Vector3 toObstacle = transform.position - _camera.transform.position;
        float angleToForward = Vector3.Angle(toObstacle, _camera.transform.forward);
        float normalizedAngle = angleToForward / 180f;
        
        float directionalImportance = 1.0f - Mathf.Clamp01(normalizedAngle);
        float distanceImportance = Mathf.Clamp01(1.0f - (_currentDistanceToObstacle / MAX_DISTANCE_THRESHOLD));
        
        return directionalImportance * distanceImportance;
    }
    
    AnimationCurve GenerateAttenuationCurve(float importance)
    {
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0f, 1f);
        curve.AddKey(importance * 2f, 0.5f);
        curve.AddKey(importance * 5f, 0.1f);
        curve.AddKey(10f, 0f);
        return curve;
    }
}