using UnityEditor;

[CustomEditor(typeof(Interactable))]
public class InteractableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty prop = serializedObject.GetIterator();
        prop.NextVisible(true);

        while (prop.NextVisible(false))
        {
            if (prop.name == "promptAnchor" || prop.name == "promptWorldOffset")
                continue;

            EditorGUILayout.PropertyField(prop);

            if (prop.name == "promptDisplayMode" &&
                prop.enumValueIndex == (int)InteractionPromptDisplayMode.AboveTarget)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("promptAnchor"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("promptWorldOffset"));
                EditorGUI.indentLevel--;
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
