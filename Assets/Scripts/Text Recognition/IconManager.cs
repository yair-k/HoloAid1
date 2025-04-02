using UnityEngine;
using System;
using SimpleJSON;
using Levenshtein;
using System.Collections.Generic;

public class IconManager: MonoBehaviour {

    [Tooltip("Select the layers raycast should target.")]
    public LayerMask RaycastLayer;

    [Tooltip("Select the icon to show.")]
    public GameObject Icon;

    [Tooltip("Select a parent object for the icons.")]
    public GameObject textBeaconManager;

    public GameObject SelectedIcon { get; set; }

    public Dictionary<int, List<GameObject>> iconDictionary;
    public int NumIcons { get; set; }

    public int defaultTextDistance;

    private const float DefaultIconThickness = 0.015f;
    private const float DefaultIconWidth = 0.08f;
    private const float DefaultIconHeight = 0.08f;
    private const float DefaultIconRadius = 0.15f;
    private const float MinimumIconRadius = 0.12f;
    private const float MaximumIconRadius = 0.25f;
    private const float ScaleIconRadius = 0.9f;
    private const int CombiningThreshold = 31;
    private const int WarningThreshold = 11;
    private const int ValidThreshold = 21;

    private  Camera raycastCamera;
    private float ScaleX;
    private float ScaleY;

    private Boolean NewTextDetected;                                        
    private int numIconsDetectedInOneCall;                                  

    private SettingsManager SettingsManager;
    public bool creatingIcons = false;

    void Start()
    {
        SettingsManager = gameObject.GetComponent<SettingsManager>();
        iconDictionary = new Dictionary<int, List<GameObject>>();           
        NumIcons = 0;
        defaultTextDistance = 5;
    }

    public void CreateIcons(string respJson)
    {
        creatingIcons = true;

        NewTextDetected = false;
        numIconsDetectedInOneCall = 0;

        JSONNode json = JSON.Parse(respJson);
        JSONArray texts = json["responses"][0]["textAnnotations"].AsArray;

        if (gameObject.GetComponent<SettingsManager>().UserSetting == UserType.AudioOnly)
        {
            RunAudioOnlyMode(texts);

            if (SettingsManager.OCRSetting == OCRRunSetting.Manual)                                             
            {

            }

            return;
        }

        ScaleX = (float)Screen.width / (float)CameraManager.cameraResolution.width;
        ScaleY = (float)Screen.height / (float)CameraManager.cameraResolution.height;

        Matrix4x4 cameraToWorldMatrix = GetComponent<CameraManager>().managerCameraToWorldMatrix;

        raycastCamera = GetComponent<CameraManager>().PositionCamera(cameraToWorldMatrix);                      

        Vector3 lastRaycastPoint = Vector3.zero;
        Ray lastRay = new Ray();
        Vector3 lastTopLeft = new Vector3();
        Vector3 lastTopRight = new Vector3();
        Vector3 lastBottomRight = new Vector3();
        Vector3 lastBottomLeft = new Vector3();

        Vector3 combinedTopLeft = new Vector3(float.MaxValue, float.MaxValue);
        Vector3 combinedTopRight = new Vector3(float.MinValue, float.MaxValue);
        Vector3 combinedBottomRight = new Vector3(float.MinValue, float.MinValue);
        Vector3 combinedBottomLeft = new Vector3(float.MaxValue, float.MinValue);

        String runningText = "";                                                                                
        Debug.Log("IM: number of text responses-" + texts.Count);
        foreach (JSONNode text in texts)
        {
            JSONNode vertices = text["boundingPoly"]["vertices"];

            Vector3 currTopLeft = CalcTopLeftVector(vertices);
            Vector3 currTopRight = CalcTopRightVector(vertices);
            Vector3 currBottomRight = CalcBottomRightVector(vertices);
            Vector3 currBottomLeft = CalcBottomLeftVector(vertices);

            Vector3 topLeftScaledAndFlipped = ScaleVector(FlipY(currTopLeft));
            Vector3 topRightScaledAndFlipped = ScaleVector(FlipY(currTopRight));
            Vector3 bottomRightScaledAndFlipped = ScaleVector(FlipY(currBottomRight));
            Vector3 bottomLeftScaledAndFlipped = ScaleVector(FlipY(currBottomLeft));

            Vector3 raycastPoint = (topLeftScaledAndFlipped + topRightScaledAndFlipped + bottomRightScaledAndFlipped + bottomLeftScaledAndFlipped) / 4;
            Ray ray = raycastCamera.ScreenPointToRay(raycastPoint);

            String currText = text["description"];

            if (currText.IndexOf('\n') != -1)                                                           
            {
                continue;
            } else if ((currBottomLeft.y - currTopLeft.y) > (currTopRight.x - currTopLeft.x))           
            {
                continue;
            }

            float minWidth = Math.Min(currTopLeft.x, currBottomLeft.x) - Math.Max(lastTopRight.x, lastBottomRight.x);
            float minHeight = Math.Min(currTopLeft.y, currTopRight.y) - Math.Max(lastBottomLeft.y, lastBottomRight.y);
            float minPixelDistance = Math.Min(Math.Abs(minWidth), Math.Abs(minHeight));

            if (lastRaycastPoint == Vector3.zero || minPixelDistance < CombiningThreshold)                      
            {
                runningText = runningText + " " + currText;

                combinedTopLeft = new Vector3(Math.Min(combinedTopLeft.x, currTopLeft.x), Math.Min(combinedTopLeft.y, currTopLeft.y));
                combinedTopRight = new Vector3(Math.Max(combinedTopRight.x, currTopRight.x), Math.Min(combinedTopRight.y, currTopRight.y));
                combinedBottomRight = new Vector3(Math.Max(combinedBottomRight.x, currBottomRight.x), Math.Max(combinedBottomRight.y, currBottomRight.y));
                combinedBottomLeft = new Vector3(Math.Min(combinedBottomLeft.x, currBottomLeft.x), Math.Max(combinedBottomLeft.y, currBottomLeft.y));                
            }
            else                                                                                                
            {
                RaycastHit centerHit;

                if (runningText.Length > 1)                                                                     
                {

                    if (Physics.Raycast(lastRay, out centerHit, 15.0f, RaycastLayer))                           
                    {
                        PlaceIcons(centerHit, runningText, currTopLeft, currTopRight, currBottomRight, currBottomLeft, combinedTopLeft, combinedTopRight, combinedBottomRight, combinedBottomLeft);
                    } else if (Physics.Raycast(lastRay, out centerHit, 15.0f, LayerMask.GetMask("Plane")))      
                    {
                        PlaceIcons(centerHit, runningText, currTopLeft, currTopRight, currBottomRight, currBottomLeft, combinedTopLeft, combinedTopRight, combinedBottomRight, combinedBottomLeft);
                    } else
                    {
                        Vector3 iconPos = lastRay.origin + lastRay.direction*defaultTextDistance;
                        PlaceIconsManual(iconPos, runningText);
                    }
                }

                combinedTopLeft = new Vector3(float.MaxValue, float.MaxValue);
                combinedTopRight = new Vector3(float.MinValue, float.MaxValue);
                combinedBottomRight = new Vector3(float.MinValue, float.MinValue);
                combinedBottomLeft = new Vector3(float.MaxValue, float.MinValue);
                runningText = currText;
            }

            lastRaycastPoint = raycastPoint;
            lastRay = ray;
            lastTopLeft = currTopLeft;
            lastTopRight = currTopRight;
            lastBottomRight = currBottomRight;
            lastBottomLeft = currBottomLeft;
        }

        RaycastHit hit;

        if (Physics.Raycast(lastRay, out hit, 15.0f, RaycastLayer))
        {
            PlaceIcons(hit, runningText, lastTopLeft, lastTopRight, lastBottomRight, lastBottomLeft, combinedTopLeft, combinedTopRight, combinedBottomRight, combinedBottomLeft);
        }
        else if (Physics.Raycast(lastRay, out hit, 15.0f, LayerMask.GetMask("Plane")))
        {
            PlaceIcons(hit, runningText, lastTopLeft, lastTopRight, lastBottomRight, lastBottomLeft, combinedTopLeft, combinedTopRight, combinedBottomRight, combinedBottomLeft);
        }
        else
        {
            Vector3 iconPos = lastRay.origin + lastRay.direction * defaultTextDistance;
            PlaceIconsManual(iconPos, runningText);
        }

        if (NumIcons > SettingsManager.MaxIcons)
        {
            if (SettingsManager.OCRSetting == OCRRunSetting.Manual)
            {
                GetComponent<TextToSpeechManager>().SpeakText("There are too many icons. Please clear icons before finding new text.");
            } else
            {
                GetComponent<TextToSpeechManager>().SpeakText("There are too many icons. Please clear icons before finding new text.");
            }
        }

        creatingIcons = false;
    }

    Vector3 CalcTopLeftVector(JSONNode node)
    {
        Vector3 vector = new Vector3(node[0]["x"].AsFloat, node[0]["y"].AsFloat, 0);

        return vector;
    }

    Vector3 CalcTopRightVector(JSONNode node)
    {
        Vector3 vector = new Vector3(node[1]["x"].AsFloat, node[1]["y"].AsFloat, 0);

        return vector;
    }

    Vector3 CalcBottomRightVector(JSONNode node)
    {
        Vector3 vector = new Vector3(node[2]["x"].AsFloat, node[2]["y"].AsFloat, 0);

        return vector;
    }

    Vector3 CalcBottomLeftVector(JSONNode node)
    {
        Vector3 vector = new Vector3(node[3]["x"].AsFloat, node[3]["y"].AsFloat, 0);

        return vector;
    }

    Vector3 FlipY(Vector3 v)
    {
        return new Vector3(v.x, (float)CameraManager.cameraResolution.height - v.y, v.z);
    }

    Vector3 ScaleVector(Vector3 vector)
    {
        return new Vector3(vector.x * ScaleX, vector.y * ScaleY, vector.z);
    }

    public void PlaceIcons(RaycastHit centerHit, String runningText, Vector3 topLeft, Vector3 topRight, Vector3 bottomRight, Vector3 bottomLeft, Vector3 combinedTopLeft, Vector3 combinedTopRight, Vector3 combinedBottomRight, Vector3 combinedBottomLeft)
    {
        if (SameIconExists(centerHit.point, runningText))                   
        {
            return;
        }

        GameObject icon = Instantiate(Icon, textBeaconManager.transform);
        icon.transform.position = centerHit.point;
        icon.GetComponent<TextInstanceScript>().beaconText = runningText; 
        icon.name = "Text Beacon: " + runningText;

        icon.transform.rotation = Quaternion.LookRotation(-centerHit.normal);

        int key = LevenshteinDistance.GetLevenshteinKey(runningText);
        List<GameObject> iconList;

        if (iconDictionary.TryGetValue(key, out iconList))
        {
            iconList.Add(icon);
        } else
        {
            iconList = new List<GameObject>();
            iconList.Add(icon);
            iconDictionary.Add(key, iconList);
        }

        NumIcons++;
        numIconsDetectedInOneCall++;
        NewTextDetected = true;

    }

    public void PlaceIconsManual(Vector3 iconPos, String runningText)
    {

        if (SameIconExists(iconPos, runningText))                   
        {
            return;
        }

        GameObject icon;
        icon = Instantiate(Icon, textBeaconManager.transform) as GameObject;
        icon.transform.position = iconPos;
        icon.GetComponent<TextInstanceScript>().beaconText = runningText; 
        icon.name = "Text Beacon: " + runningText;

        int key = LevenshteinDistance.GetLevenshteinKey(runningText);
        List<GameObject> iconList;

        if (iconDictionary.TryGetValue(key, out iconList))
        {
            iconList.Add(icon);
        }
        else
        {
            iconList = new List<GameObject>();
            iconList.Add(icon);
            iconDictionary.Add(key, iconList);
        }

        NumIcons++;
        numIconsDetectedInOneCall++;
        NewTextDetected = true;
    }

    public void ShootText (string sampleText)
    {

        var headPosition = Camera.main.transform.position;
        var gazeDirection = Camera.main.transform.forward;

        RaycastHit hit;

        if (Physics.Raycast(headPosition, gazeDirection, out hit,
            30.0f))
        {
            Debug.Log("Sample text beacon placed. Text: " + sampleText);
            PlaceIconsManual(hit.point, sampleText);
        }
    }

    Boolean SameIconExists(Vector3 point, string text)
    {
        int key = LevenshteinDistance.GetLevenshteinKey(text);
        List<GameObject> iconList;

        if (iconDictionary.TryGetValue(key, out iconList))
        {
            foreach(GameObject go in iconList)
            {
                string goText = "";

                if (LevenshteinDistance.GetLevenshteinDistance(text, goText) < LevenshteinDistance.ToleranceLevel && Vector3.Distance(point, go.transform.position) < 0.135)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void ReadAllIconsInView()
    {
        Camera mainCamera = Camera.main;
        List<GameObject> iconsInView = FindAllIconsInView(mainCamera);

        if (iconsInView.Count == 0)
        {
            GetComponent<TextToSpeechManager>().SpeakText("There are no icons in the scene");
            return;
        } else
        {
            GetComponent<TextToSpeechManager>().SpeakText("There are " + iconsInView.Count + " icons in the scene");
        }

        iconsInView.Sort(delegate (GameObject a, GameObject b)
        {
            float distA = Vector3.Distance(a.transform.position, mainCamera.transform.position);
            float distB = Vector3.Distance(b.transform.position, mainCamera.transform.position);

            if (distA < distB) return -1;
            else return 1;
        });

        int num = 1;
        foreach (var icon in iconsInView)
        {

            num++;
        }
    }

    List<GameObject> FindAllIconsInView(Camera camera)
    {
        List<GameObject> iconsInView = new List<GameObject>();

        var icons = GameObject.FindGameObjectsWithTag("Icon");

        foreach (var icon in icons)
        {
            if (icon.GetComponent<Renderer>().isVisible == true)            
            {
                iconsInView.Add(icon);
            }
        }

        return iconsInView;
    }

    void ChangeCircleIconScale(GameObject icon, Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft)
    {
        float widthSize = GetWorldObjectWidthFromRaycast(topLeft, topRight);
        float heightSize = GetWorldObjectHeightFromRaycast(topLeft, bottomLeft);

        float radius = Math.Max(widthSize, heightSize) * ScaleIconRadius;
        radius = Math.Max(radius, MinimumIconRadius);
        radius = Math.Min(radius, MaximumIconRadius);

        icon.transform.localScale = new Vector3(radius, radius, radius);
    }

    float GetWorldObjectWidthFromRaycast(Vector3 topLeft, Vector3 topRight)
    {
        Ray topLeftRay = raycastCamera.ScreenPointToRay(topLeft);
        Ray topRightRay = raycastCamera.ScreenPointToRay(topRight);

        RaycastHit topLeftHit;
        RaycastHit topRightHit;

        bool foundTopLeft = Physics.Raycast(topLeftRay, out topLeftHit, 15.0f, RaycastLayer);
        bool foundTopRight = Physics.Raycast(topRightRay, out topRightHit, 15.0f, RaycastLayer);
        bool foundCorners = foundTopLeft && foundTopRight;

        if (!foundCorners)
        {
            return -1f;
        }

        float widthSize = Vector3.Distance(topRightHit.point, topLeftHit.point) * 2.0f;
        return widthSize;
    }

    float GetWorldObjectHeightFromRaycast(Vector3 topLeft, Vector3 bottomLeft)
    {
        Ray topLeftRay = raycastCamera.ScreenPointToRay(topLeft);
        Ray bottomLeftRay = raycastCamera.ScreenPointToRay(bottomLeft);

        RaycastHit topLeftHit;
        RaycastHit bottomLeftHit;

        bool foundTopLeft = Physics.Raycast(topLeftRay, out topLeftHit, 15.0f, RaycastLayer);
        bool foundBottomLeft = Physics.Raycast(bottomLeftRay, out bottomLeftHit, 15.0f, RaycastLayer);
        bool foundCorners = foundTopLeft && foundBottomLeft;

        if (!foundCorners)
        {
            return -1f;
        }

        float heightSize = Vector3.Distance(topLeftHit.point, bottomLeftHit.point) * 2.0f;
        return heightSize;
    }

    private void RunAudioOnlyMode(JSONArray texts)
    {
        string entireString = "";

        foreach (JSONNode text in texts)
        {

            String currText = text["description"];

            if (currText.IndexOf('\n') != -1)                                                           
            {
                continue;
            }

            entireString = entireString + " " + currText;
        }

        GetComponent<TextToSpeechManager>().SpeakText(entireString);
    }

}