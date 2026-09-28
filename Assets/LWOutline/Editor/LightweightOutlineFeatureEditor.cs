#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LightweightOutlineFeature))]
public class LightweightOutlineFeatureEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);

        if (GUILayout.Button("Refresh Profiles", GUILayout.Height(24f)))
        {
            ((LightweightOutlineFeature)target).RefreshProfiles();
            SceneView.RepaintAll();
        }

        EditorGUILayout.HelpBox(
            "Profile data is cached once during Create()." 
            + "Changes made in the Editor are reflected automatically;" 
            +"if you modify profiles during a build or at runtime,"
            +" +use this button (or call RefreshProfiles() from a script).",
            MessageType.Info);
    }
}
#endif
