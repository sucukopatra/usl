using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CustomUIComponent), true)]
public class CustomUIComponentEditor: Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CustomUIComponent customUIComponent = (CustomUIComponent)target;
        if (GUILayout.Button("Configure Now"))
        {
            customUIComponent.Init();
        }
    }
}
