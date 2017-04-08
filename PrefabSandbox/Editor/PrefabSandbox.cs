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

		private static PrefabSandboxData data_;
		private static Scene sandboxScene_;
		private static PrefabSandboxValidator prefabSandboxValidator_;

		private static GameObject sandboxSetupPrefab_;
		private static GameObject SandboxSetupPrefab_ {
			get {
				if (sandboxSetupPrefab_ == null) {
					string sandboxSetupPrefabPath = FindSandboxSetupPrefabPath();
					if (sandboxSetupPrefabPath.IsNullOrEmpty()) {
						return null;
					}

					sandboxSetupPrefab_ = AssetDatabase.LoadAssetAtPath(sandboxSetupPrefabPath, typeof(GameObject)) as GameObject;
				}

				return sandboxSetupPrefab_;
			}
		}

		static PrefabSandbox() {
			EditorApplicationUtil.OnSceneGUIDelegate += OnSceneGUI;
			EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;
		}


		// PRAGMA MARK - Public Interface
		public static bool OpenPrefab(string guid) {
			string assetPath = AssetDatabase.GUIDToAssetPath(guid);
			GameObject prefab = AssetDatabase.LoadAssetAtPath(assetPath, typeof(GameObject)) as GameObject;

			return OpenPrefab(prefab);
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
			if (EditorSceneManager.GetActiveScene() != sandboxScene_) {
				Cleanup();
			}

			bool alreadyEditing = (data_ != null && data_.prefabGuid == guid);
			if (alreadyEditing) {
				Debug.LogError("Can't OpenPrefab in Prefab Sandbox: already editing a prefab!");
				return false;
			}

			Scene oldScene = EditorSceneManager.GetActiveScene();
			string oldScenePath = oldScene.path;

			if (oldScene == sandboxScene_) {
				oldScenePath = data_.oldScenePath;
			} else {
				if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
					return false;
				}
			}

			data_ = new PrefabSandboxData();
			data_.prefabGuid = guid;
			data_.prefabPath = assetPath;
			data_.prefabAsset = prefab;
			data_.oldScenePath = oldScenePath;

			SavePrefabData();
			SetupSandbox();
			return true;
		}


		// PRAGMA MARK - Static Internal
		[UnityEditor.Callbacks.DidReloadScripts]
		private static void OnScriptsReloaded() {
			ReloadPrefabData();
		}

		private const float kSceneButtonHeight = 20.0f;
		private const float kSaveAndExitButtonWidth = 120.0f;
		private const float kRevertButtonWidth = 80.0f;

		private static void OnSceneGUI(SceneView sceneView) {
			if (!IsEditing()) {
				return;
			}

			Handles.BeginGUI();
			Color previousColor = GUI.color;

			// BEGIN SCENE GUI
			GUI.color = Color.green;
			if (GUI.Button(new Rect(sceneView.position.size.x - kSaveAndExitButtonWidth, 0.0f, kSaveAndExitButtonWidth, kSceneButtonHeight), "Save and Exit")) {
				SavePrefabInstance();
				CloseSandboxScene();
			}

			GUI.color = kErrorColor;
			if (GUI.Button(new Rect(sceneView.position.size.x - kRevertButtonWidth, kSceneButtonHeight, kRevertButtonWidth, kSceneButtonHeight), "Revert")) {
				RevertPrefabInstance();
			}
			// END SCENE GUI

			GUI.color = previousColor;
			Handles.EndGUI();
		}

		private static void OnHierarchyWindowItemOnGUI(int instanceId, Rect selectionRect) {
			if (!IsEditing()) {
				return;
			}

			if (Event.current.type != EventType.Repaint) {
				return;
			}

			Color previousBackgroundColor = GUI.backgroundColor;

			GameObject g = EditorUtility.InstanceIDToObject(instanceId) as GameObject;

			GameObject prefabInstance = data_.prefabInstance;
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
			if (sandboxScene_ != EditorSceneManager.GetActiveScene()) {
				return false;
			}

			return data_ != null;
		}

		// PRAGMA MARK - Setup
		private static string FindSandboxSetupPrefabPath() {
			string guid = AssetDatabaseUtil.FindSpecificAsset(kSandboxSetupPrefabName + " t:Prefab", required: false);
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
			sandboxScene_ = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			EditorSceneManager.SetActiveScene(sandboxScene_);

			if (sandboxScene_.isLoaded) {
				ClearAllGameObjectsInSandbox();

				// setup scene with sandbox setup prefab
				if (SandboxSetupPrefab_ == null) {
					Debug.LogWarning("Failed to find a sandbox setup prefab! If you want to customize your sandbox (to add a light, camera, canvas, etc), it is recommended to create a prefab named " + kSandboxSetupPrefabName + "!");
				} else {
					GameObject sandboxSetupObject = GameObject.Instantiate(SandboxSetupPrefab_);
					sandboxSetupObject.transform.localPosition = Vector3.zero;
				}

				if (!CreatePrefabInstance()) {
					CloseSandboxScene();
					return;
				}
			}
		}

		private static void CloseSandboxScene() {
			if (!IsEditing()) {
				Cleanup();
				return;
			}

			if (prefabSandboxValidator_ != null && prefabSandboxValidator_.RefreshAndCheckValiationErrors()) {
				if (EditorUtility.DisplayDialog("Prefab Validation Errors Found!", "Missing references found in prefab instance.", "I'll fix it", "Ignore it")) {
					return;
				}
			}

			Cleanup();
		}

		private static void Cleanup() {
			// Cleanup validator before modifying scene to avoid extra validations
			CleanupValidator();
			ClearAllGameObjectsInSandbox();
			sandboxScene_ = default(Scene);

			if (data_ != null && !data_.oldScenePath.IsNullOrEmpty()) {
				EditorSceneManager.OpenScene(data_.oldScenePath);
			}
			ClearPrefabData();
		}

		private static bool CreatePrefabInstance() {
			if (data_.prefabInstance != null) {
				GameObject.DestroyImmediate(data_.prefabInstance);
			}

			data_.prefabInstance = PrefabUtility.InstantiatePrefab(data_.prefabAsset) as GameObject;
			PrefabUtility.DisconnectPrefabInstance(data_.prefabInstance);
			ReloadValidator();

			// if the prefab is a UI element, child it under a canvas
			if (data_.prefabInstance.GetComponent<RectTransform>() != null) {
				var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
				if (canvas != null) {
					data_.prefabInstance.transform.SetParent(canvas.transform, worldPositionStays: false);
				}
			}

			Selection.activeGameObject = data_.prefabInstance;
			HierarchyUtil.ExpandCurrentSelectedObjectInHierarchy();

			return true;
		}

		private static void SavePrefabInstance() {
			if (data_.prefabInstance == null) {
				Debug.LogWarning("Failed to save prefab instance - instance is null!");
				return;
			}

			PrefabUtility.ReplacePrefab(data_.prefabInstance, data_.prefabAsset, ReplacePrefabOptions.Default);
			PrefabUtility.DisconnectPrefabInstance(data_.prefabInstance);
		}

		private static void RevertPrefabInstance() {
			// NOTE (darren): alias for CreatePrefabInstance because it will recreate if necessary
			CreatePrefabInstance();
		}

		private static void ClearAllGameObjectsInSandbox() {
			if (!sandboxScene_.IsValid()) {
				return;
			}

			foreach (GameObject obj in sandboxScene_.GetRootGameObjects()) {
				GameObject.DestroyImmediate(obj);
			}
		}

		private static void SavePrefabData() {
			EditorPrefs.SetString("PrefabSandbox._data", JsonUtility.ToJson(data_));
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

			sandboxScene_ = EditorSceneManager.GetActiveScene();
			data_ = deserializedData;
			data_.prefabInstance = prefabInstance;

			ReloadValidator();
		}

		private static void ReloadValidator() {
			if (data_ == null) {
				Debug.LogError("Can't reload validator - _data is null!");
				return;
			}

			GameObject prefabInstance = data_.prefabInstance;
			if (prefabInstance == null) {
				Debug.LogError("Can't reload validator - prefabInstance is null!");
				return;
			}

			CleanupValidator();
			prefabSandboxValidator_ = new PrefabSandboxValidator(prefabInstance);
		}

		private static void CleanupValidator() {
			if (prefabSandboxValidator_ != null) {
				prefabSandboxValidator_.Dispose();
				prefabSandboxValidator_ = null;
			}
		}

		private static void ClearPrefabData() {
			data_ = null;
			EditorPrefs.DeleteKey("PrefabSandbox._data");
		}
	}
}
