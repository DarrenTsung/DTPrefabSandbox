using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using DTPrefabSandbox.Internal;

namespace DTPrefabSandbox {
    [InitializeOnLoad]
    public class PrefabSandbox {
        public static readonly Color kErrorColor = ColorUtil.HexStringToColor("#dc4d4d");

        private const string kSandboxSetupPrefabName = "PrefabSandboxSetupPrefab";

        [Serializable]
        public class PrefabSandboxData {
            public string prefabGuid;
            public string prefabPath;
            public GameObject prefabAsset;
            public GameObject prefabInstance;

            public string oldScenePath;
        }

        private static PrefabSandboxData _data;
        private static Scene _sandboxScene;
        private static PrefabSandboxValidator _prefabSandboxValidator;

        private static GameObject _sandboxSetupPrefab;
        private static GameObject SandboxSetupPrefab {
            get {
                if (PrefabSandbox._sandboxSetupPrefab == null) {
                    string sandboxSetupPrefabPath = PrefabSandbox.FindSandboxSetupPrefabPath();
                    if (sandboxSetupPrefabPath.IsNullOrEmpty()) {
                        return null;
                    }

                    PrefabSandbox._sandboxSetupPrefab = AssetDatabase.LoadAssetAtPath(sandboxSetupPrefabPath, typeof(GameObject)) as GameObject;
                }

                return PrefabSandbox._sandboxSetupPrefab;
            }
        }

        static PrefabSandbox() {
            EditorApplicationUtil.OnSceneGUIDelegate += PrefabSandbox.OnSceneGUI;
            EditorApplication.hierarchyWindowItemOnGUI += PrefabSandbox.OnHierarchyWindowItemOnGUI;
        }


        // PRAGMA MARK - Public Interface
        public static bool OpenPrefab(string guid) {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath(assetPath, typeof(GameObject)) as GameObject;

            return PrefabSandbox.OpenPrefab(prefab);
        }

        public static bool OpenPrefab(GameObject prefab) {
            if (prefab == null) {
                Debug.LogError("Can't OpenPrefab in Prefab Sandbox: Prefab argument is null!");
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(prefab);
            if (assetPath.IsNullOrEmpty()) {
                Debug.LogError("Can't OpenPrefab in Prefab Sandbox: Failed to get AssetPath!");
                return false;
            }

            if (!PathUtil.IsPrefab(assetPath)) {
                Debug.LogError("Can't OpenPrefab in Prefab Sandbox: AssetPath is not a prefab!");
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);

            // NOTE (darren): before checking if already editing
            // check if we're not in the sandbox scene (if so, cleanup)
            if (EditorSceneManager.GetActiveScene() != PrefabSandbox._sandboxScene) {
                PrefabSandbox.Cleanup();
            }

            bool alreadyEditing = (PrefabSandbox._data != null && PrefabSandbox._data.prefabGuid == guid);
            if (alreadyEditing) {
                Debug.LogError("Can't OpenPrefab in Prefab Sandbox: already editing a prefab!");
                return false;
            }

            Scene oldScene = EditorSceneManager.GetActiveScene();
            string oldScenePath = oldScene.path;

            if (oldScene == PrefabSandbox._sandboxScene) {
                oldScenePath = PrefabSandbox._data.oldScenePath;
            } else {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                    return false;
                }
            }

            PrefabSandbox._data = new PrefabSandboxData();
            PrefabSandbox._data.prefabGuid = guid;
            PrefabSandbox._data.prefabPath = assetPath;
            PrefabSandbox._data.prefabAsset = prefab;
            PrefabSandbox._data.oldScenePath = oldScenePath;

            PrefabSandbox.SavePrefabData();
            PrefabSandbox.SetupSandbox();
            return true;
        }


        // PRAGMA MARK - Static Internal
        [UnityEditor.Callbacks.DidReloadScripts]
        static void OnScriptsReloaded() {
            PrefabSandbox.ReloadPrefabData();
        }

        private const float kSceneButtonHeight = 20.0f;
        private const float kSaveAndExitButtonWidth = 120.0f;
        private const float kRevertButtonWidth = 80.0f;

        private static void OnSceneGUI(SceneView sceneView) {
            if (!PrefabSandbox.IsEditing()) {
                return;
            }

            Handles.BeginGUI();
            Color previousColor = GUI.color;

            // BEGIN SCENE GUI
            GUI.color = Color.green;
            if (GUI.Button(new Rect(sceneView.position.size.x - kSaveAndExitButtonWidth, 0.0f, kSaveAndExitButtonWidth, kSceneButtonHeight), "Save and Exit")) {
                PrefabSandbox.SavePrefabInstance();
                PrefabSandbox.CloseSandboxScene();
            }

            GUI.color = kErrorColor;
            if (GUI.Button(new Rect(sceneView.position.size.x - kRevertButtonWidth, kSceneButtonHeight, kRevertButtonWidth, kSceneButtonHeight), "Revert")) {
                PrefabSandbox.RevertPrefabInstance();
            }
            // END SCENE GUI

            GUI.color = previousColor;
            Handles.EndGUI();
        }

        private static void OnHierarchyWindowItemOnGUI(int instanceId, Rect selectionRect) {
            if (!PrefabSandbox.IsEditing()) {
                return;
            }

            if (Event.current.type != EventType.Repaint) {
                return;
            }

            Color previousBackgroundColor = GUI.backgroundColor;

            GameObject g = EditorUtility.InstanceIDToObject(instanceId) as GameObject;

            GameObject prefabInstance = PrefabSandbox._data.prefabInstance;
            bool isPartOfPrefabInstance = g == prefabInstance || g.GetParents().Any(parent => parent == prefabInstance);
            if (isPartOfPrefabInstance) {
                float alpha = EditorGUIUtility.isProSkin ? 1.0f : 0.15f;
                if (UnityEditor.AnimationMode.InAnimationMode()) {
                    GUI.backgroundColor = Color.red.WithAlpha(alpha);
                } else {
                    GUI.backgroundColor = Color.yellow.WithAlpha(alpha);
                }

                GUI.Box(selectionRect, "");
                EditorApplication.RepaintHierarchyWindow();
            }

            GUI.backgroundColor = previousBackgroundColor;
        }

        private static bool IsEditing() {
            if (PrefabSandbox._sandboxScene != EditorSceneManager.GetActiveScene()) {
                return false;
            }

            return PrefabSandbox._data != null;
        }

        // PRAGMA MARK - Setup
        private static string FindSandboxSetupPrefabPath() {
            string guid = AssetDatabaseUtil.FindSpecificAsset(PrefabSandbox.kSandboxSetupPrefabName + " t:Prefab", required: false);
            if (guid.IsNullOrEmpty()) {
                return "";
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (PathUtil.IsPrefab(path)) {
                return path;
            } else {
                Debug.LogError(string.Format("Path for sandbox setup prefab ({0}) is not a prefab", path));
                return "";
            }
        }

        private static void SetupSandbox() {
            PrefabSandbox._sandboxScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EditorSceneManager.SetActiveScene(PrefabSandbox._sandboxScene);

            if (PrefabSandbox._sandboxScene.isLoaded) {
                PrefabSandbox.ClearAllGameObjectsInSandbox();

                // setup scene with sandbox setup prefab
                if (PrefabSandbox.SandboxSetupPrefab == null) {
                    Debug.LogWarning("Failed to find a sandbox setup prefab! If you want to customize your sandbox (to add a light, camera, canvas, etc), it is recommended to create a prefab named " + kSandboxSetupPrefabName + "!");
                } else {
                    GameObject sandboxSetupObject = GameObject.Instantiate(PrefabSandbox.SandboxSetupPrefab);
                    sandboxSetupObject.transform.localPosition = Vector3.zero;
                }

                if (!PrefabSandbox.CreatePrefabInstance()) {
                    PrefabSandbox.CloseSandboxScene();
                    return;
                }
            }
        }

        private static void CloseSandboxScene() {
            if (!PrefabSandbox.IsEditing()) {
                PrefabSandbox.Cleanup();
                return;
            }

            if (PrefabSandbox._prefabSandboxValidator != null && PrefabSandbox._prefabSandboxValidator.RefreshAndCheckValiationErrors()) {
                if (EditorUtility.DisplayDialog("Prefab Validation Errors Found!", "Missing references found in prefab instance.", "I'll fix it", "Ignore it")) {
                    return;
                }
            }

            PrefabSandbox.Cleanup();
        }

        private static void Cleanup() {
            // Cleanup validator before modifying scene to avoid extra validations
            PrefabSandbox.CleanupValidator();
            PrefabSandbox.ClearAllGameObjectsInSandbox();
            PrefabSandbox._sandboxScene = default(Scene);

            if (PrefabSandbox._data != null && !PrefabSandbox._data.oldScenePath.IsNullOrEmpty()) {
                EditorSceneManager.OpenScene(PrefabSandbox._data.oldScenePath);
            }
            PrefabSandbox.ClearPrefabData();
        }

        private static bool CreatePrefabInstance() {
            if (PrefabSandbox._data.prefabInstance != null) {
                GameObject.DestroyImmediate(PrefabSandbox._data.prefabInstance);
            }

            PrefabSandbox._data.prefabInstance = PrefabUtility.InstantiatePrefab(PrefabSandbox._data.prefabAsset) as GameObject;
            PrefabUtility.DisconnectPrefabInstance(PrefabSandbox._data.prefabInstance);
            PrefabSandbox.ReloadValidator();

            // if the prefab is a UI element, child it under a canvas
            if (PrefabSandbox._data.prefabInstance.GetComponent<RectTransform>() != null) {
                var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
                if (canvas != null) {
                    PrefabSandbox._data.prefabInstance.transform.SetParent(canvas.transform, worldPositionStays: false);
                }
            }

            Selection.activeGameObject = PrefabSandbox._data.prefabInstance;
            HierarchyUtil.ExpandCurrentSelectedObjectInHierarchy();

            return true;
        }

        private static void SavePrefabInstance() {
            if (PrefabSandbox._data.prefabInstance == null) {
                Debug.LogWarning("Failed to save prefab instance - instance is null!");
                return;
            }

            PrefabUtility.ReplacePrefab(PrefabSandbox._data.prefabInstance, PrefabSandbox._data.prefabAsset, ReplacePrefabOptions.Default);
            PrefabUtility.DisconnectPrefabInstance(PrefabSandbox._data.prefabInstance);
        }

        private static void RevertPrefabInstance() {
            // NOTE (darren): alias for CreatePrefabInstance because it will recreate if necessary
            CreatePrefabInstance();
        }

        private static void ClearAllGameObjectsInSandbox() {
            if (!PrefabSandbox._sandboxScene.IsValid()) {
                return;
            }

            foreach (GameObject obj in PrefabSandbox._sandboxScene.GetRootGameObjects()) {
                GameObject.DestroyImmediate(obj);
            }
        }

        private static void SavePrefabData() {
            EditorPrefs.SetString("PrefabSandbox._data", JsonUtility.ToJson(PrefabSandbox._data));
        }

        private static void ReloadPrefabData() {
            string serialized = EditorPrefs.GetString("PrefabSandbox._data");
            if (serialized.IsNullOrEmpty()) {
                return;
            }

            var deserializedData = JsonUtility.FromJson<PrefabSandboxData>(serialized);
            if (deserializedData.prefabAsset == null) {
                return;
            }

            var prefabInstance = GameObject.Find(deserializedData.prefabAsset.name);
            if (prefabInstance == null) {
                return;
            }

            PrefabSandbox._sandboxScene = EditorSceneManager.GetActiveScene();
            PrefabSandbox._data = deserializedData;
            PrefabSandbox._data.prefabInstance = prefabInstance;

            PrefabSandbox.ReloadValidator();
        }

        private static void ReloadValidator() {
            if (PrefabSandbox._data == null) {
                Debug.LogError("Can't reload validator - _data is null!");
                return;
            }

            GameObject prefabInstance = PrefabSandbox._data.prefabInstance;
            if (prefabInstance == null) {
                Debug.LogError("Can't reload validator - prefabInstance is null!");
                return;
            }

            PrefabSandbox.CleanupValidator();
            PrefabSandbox._prefabSandboxValidator = new PrefabSandboxValidator(prefabInstance);
        }

        private static void CleanupValidator() {
            if (PrefabSandbox._prefabSandboxValidator != null) {
                PrefabSandbox._prefabSandboxValidator.Dispose();
                PrefabSandbox._prefabSandboxValidator = null;
            }
        }

        private static void ClearPrefabData() {
            PrefabSandbox._data = null;
            EditorPrefs.DeleteKey("PrefabSandbox._data");
        }
    }
}
