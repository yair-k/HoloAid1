using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public class HelpAttribute : PropertyAttribute
{
    public readonly string text;
    public readonly MessageType type;
    public readonly string url;
    public readonly bool collapsible;
    public readonly string title;
    public readonly int fontSize;
    public readonly FontStyle fontStyle;

    // Simple constructor
    public HelpAttribute(string text, MessageType type = MessageType.Info)
    {
        this.text = text;
        this.type = type;
        this.url = string.Empty;
        this.collapsible = false;
        this.title = string.Empty;
        this.fontSize = 11;
        this.fontStyle = FontStyle.Normal;
    }
    
    // Advanced constructor with URL linking
    public HelpAttribute(string text, string url, MessageType type = MessageType.Info)
    {
        this.text = text;
        this.type = type;
        this.url = url;
        this.collapsible = false;
        this.title = string.Empty;
        this.fontSize = 11;
        this.fontStyle = FontStyle.Normal;
    }
    
    // Full featured constructor
    public HelpAttribute(string text, MessageType type, bool collapsible, string title = "", int fontSize = 11, FontStyle fontStyle = FontStyle.Normal)
    {
        this.text = text;
        this.type = type;
        this.url = string.Empty;
        this.collapsible = collapsible;
        this.title = title;
        this.fontSize = fontSize;
        this.fontStyle = fontStyle;
    }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(HelpAttribute))]
public class HelpDrawer : PropertyDrawer
{
    const int paddingHeight = 8;
    const int marginHeight = 2;
    const int linkButtonWidth = 60;
    
    float baseHeight = 0;
    float addedHeight = 0;
    
    private bool isCollapsed = false;
    private GUIStyle helpBoxStyle;
    private GUIStyle linkButtonStyle;
    private GUIStyle titleStyle;

    HelpAttribute helpAttribute { get { return (HelpAttribute)attribute; } }

    RangeAttribute rangeAttribute
    {
        get
        {
            var attributes = fieldInfo.GetCustomAttributes(typeof(RangeAttribute), true);
            return attributes != null && attributes.Length > 0 ? (RangeAttribute)attributes[0] : null;
        }
    }

    MultilineAttribute multilineAttribute
    {
        get
        {
            var attributes = fieldInfo.GetCustomAttributes(typeof(MultilineAttribute), true);
            return attributes != null && attributes.Length > 0 ? (MultilineAttribute)attributes[0] : null;
        }
    }

    public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
    {
        baseHeight = base.GetPropertyHeight(prop, label);
        float minHeight = paddingHeight * 5;

        if (helpAttribute.collapsible && isCollapsed)
            return baseHeight + paddingHeight; // Show only the title bar if collapsed

        var content = new GUIContent(helpAttribute.text);
        var style = GetHelpBoxStyle();

        var height = style.CalcHeight(content, EditorGUIUtility.currentViewWidth - (string.IsNullOrEmpty(helpAttribute.url) ? 0 : linkButtonWidth));
        height += marginHeight * 2;

        if (!string.IsNullOrEmpty(helpAttribute.title))
            height += EditorGUIUtility.singleLineHeight; // Add space for the title

        if (multilineAttribute != null && prop.propertyType == SerializedPropertyType.String)
            addedHeight = 48f;

        return height > minHeight ? height + baseHeight + addedHeight : minHeight + baseHeight + addedHeight;
    }

    public override void OnGUI(Rect position, SerializedProperty prop, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, prop);
        
        var helpPos = position;
        helpPos.height -= baseHeight + marginHeight;

        if (multilineAttribute != null)
            helpPos.height -= addedHeight;
        
        // Handle collapsible help boxes
        if (helpAttribute.collapsible)
        {
            Rect titleRect = helpPos;
            titleRect.height = EditorGUIUtility.singleLineHeight;
            
            // Draw the title bar with collapsible toggle
            string titlePrefix = isCollapsed ? "▶ " : "▼ ";
            string displayTitle = string.IsNullOrEmpty(helpAttribute.title) 
                ? "Help Information" 
                : helpAttribute.title;
            
            if (GUI.Button(titleRect, titlePrefix + displayTitle, GetTitleStyle()))
                isCollapsed = !isCollapsed;
            
            // If collapsed, don't show the help text
            if (isCollapsed)
            {
                DrawPropertyField(position, prop, label);
                EditorGUI.EndProperty();
                return;
            }
            
            // Adjust the position for the actual help content
            helpPos.y += EditorGUIUtility.singleLineHeight;
            helpPos.height -= EditorGUIUtility.singleLineHeight;
        }
        
        // Draw the help box
        if (!string.IsNullOrEmpty(helpAttribute.url))
        {
            // Split the space for help text and URL button
            Rect helpTextRect = helpPos;
            helpTextRect.width -= linkButtonWidth + 2;
            
            Rect linkRect = helpPos;
            linkRect.x = helpTextRect.xMax + 2;
            linkRect.width = linkButtonWidth;
            
            EditorGUI.HelpBox(helpTextRect, helpAttribute.text, helpAttribute.type);
            
            // Draw the link button
            if (GUI.Button(linkRect, "More Info", GetLinkButtonStyle()))
            {
                Application.OpenURL(helpAttribute.url);
            }
        }
        else
        {
            // Draw the full-width help box
            EditorGUI.HelpBox(helpPos, helpAttribute.text, helpAttribute.type);
        }

        position.y += helpPos.height + marginHeight;
        position.height = baseHeight;

        DrawPropertyField(position, prop, label);
        EditorGUI.EndProperty();
    }
    
    private void DrawPropertyField(Rect position, SerializedProperty prop, GUIContent label)
    {
        var range = rangeAttribute;
        var multiline = multilineAttribute;

        if (range != null)
        {
            if (prop.propertyType == SerializedPropertyType.Float)
            {
                EditorGUI.Slider(position, prop, range.min, range.max, label);
            }
            else if (prop.propertyType == SerializedPropertyType.Integer)
            {
                EditorGUI.IntSlider(position, prop, (int)range.min, (int)range.max, label);
            }
            else
            {
                EditorGUI.PropertyField(position, prop, label);
            }
        }
        else if (multiline != null)
        {
            if (prop.propertyType == SerializedPropertyType.String)
            {
                var style = GUI.skin.label;
                var size = style.CalcHeight(label, EditorGUIUtility.currentViewWidth);

                EditorGUI.LabelField(position, label);

                position.y += size;
                position.height += addedHeight - size;

                prop.stringValue = EditorGUI.TextArea(position, prop.stringValue);
            }
            else
            {
                EditorGUI.PropertyField(position, prop, label);
            }
        }
        else
        {
            EditorGUI.PropertyField(position, prop, label);
        }
    }
    
    private GUIStyle GetHelpBoxStyle()
    {
        if (helpBoxStyle == null)
        {
            helpBoxStyle = new GUIStyle(GUI.skin.GetStyle("helpbox"));
            helpBoxStyle.fontSize = helpAttribute.fontSize;
            helpBoxStyle.fontStyle = helpAttribute.fontStyle;
            helpBoxStyle.wordWrap = true;
            helpBoxStyle.richText = true;
            
            // Handle dark mode automatically
            bool isDarkMode = EditorGUIUtility.isProSkin;
            if (isDarkMode)
                helpBoxStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f); // Lighter text for dark mode
        }
        return helpBoxStyle;
    }
    
    private GUIStyle GetLinkButtonStyle()
    {
        if (linkButtonStyle == null)
        {
            linkButtonStyle = new GUIStyle(GUI.skin.button);
            linkButtonStyle.fontSize = 9;
            linkButtonStyle.fixedHeight = EditorGUIUtility.singleLineHeight * 1.2f;
        }
        return linkButtonStyle;
    }
    
    private GUIStyle GetTitleStyle()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.alignment = TextAnchor.MiddleLeft;
            titleStyle.normal.background = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.1f, 0.1f));
        }
        return titleStyle;
    }
    
    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;
        
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }
}
#else
// Define MessageType enum for use outside of the editor
public enum MessageType
{
    None,
    Info,
    Warning,
    Error,
}
#endif