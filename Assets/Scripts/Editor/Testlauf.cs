using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ocean.EditorWerkzeuge
{
    /// <summary>
    /// Einstiege für den Start ohne Oberfläche (Unity -batchmode -executeMethod): Welt aufbauen und Szene speichern.
    /// </summary>
    public static class Testlauf
    {
        public static void WeltAufbauenUndSpeichern()
        {
            var szene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            WeltAufbau.Aufbauen();
            EditorSceneManager.SaveScene(szene);
            AssetDatabase.SaveAssets();
            Debug.Log("Ocean-Testlauf: Welt aufgebaut und gespeichert.");
        }
    }
}
