using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

using DTPrefabSandbox.Internal;

using DTValidator;
using DTValidator.ValidationErrors;

namespace DTPrefabSandbox {
	public class PrefabSandboxValidator : IDisposable {
		// PRAGMA MARK - Public Interface
		public void Dispose() {
			objectToValidate_ = null;
			cachedValidationErrors_ = null;

			EditorApplicationUtil.OnSceneGUIDelegate -= OnSceneGUI;
			EditorApplicationUtil.SceneDirtied -= RefreshValidationErrors;
			EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyWindowItemOnGUI;
		}

		public PrefabSandboxValidator(GameObject objectToValidate) {
			objectToValidate_ = objectToValidate;
			RefreshValidationErrors();

			EditorApplicationUtil.OnSceneGUIDelegate += OnSceneGUI;
			EditorApplicationUtil.SceneDirtied += RefreshValidationErrors;
			EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;
		}

		public bool RefreshAndCheckValiationErrors() {
			RefreshValidationErrors();
			return !cachedValidationErrors_.IsNullOrEmpty();
		}


		// PRAGMA MARK - Internal
		private const float kErrorHeight = 20.0f;
		private const float kErrorSpacingHeight = 2.0f;
		private const float kErrorWidth = 275.0f;
		private const float kLinkWidth = 40.0f;
		private const float kLinkPadding = 3.0f;

		private static GUIStyle kButtonStyle_ = null;
		private static GUIStyle kButtonStyle {
			get {
				// NOTE (darren): sometimes the textures can get dealloc
				// and appear as nothing - we recreate them here
				if (kButtonStyle_ != null && kButtonStyle_.normal.background == null) {
					kButtonStyle_ = null;
				}

				if (kButtonStyle_ == null) {
					kButtonStyle_ = new GUIStyle(GUI.skin.GetStyle("Button"));
					kButtonStyle_.alignment = TextAnchor.MiddleRight;
					kButtonStyle_.padding.right = (int)(kLinkWidth + (2.0f * kLinkPadding) + 2);
					kButtonStyle_.padding.top = 3;
					kButtonStyle_.normal.background = Texture2DUtil.GetCached1x1TextureWithColor(Color.black.WithAlpha(0.5f));
					kButtonStyle_.active.background = Texture2DUtil.GetCached1x1TextureWithColor(Color.black.WithAlpha(0.3f));
				}
				return kButtonStyle_;
			}
		}

		private const float kErrorIconPadding = 3.0f;

		private static Texture2D kErrorIconTexture_ = null;
		private static Texture2D kErrorIconTexture {
			get {
				if (kErrorIconTexture_ == null) {
					string prefabSandboxPath = ScriptableObjectEditorUtil.PathForScriptableObjectType<PrefabSandboxMarker>();
					kErrorIconTexture_ = AssetDatabaseUtil.LoadAssetAtPath<Texture2D>(prefabSandboxPath + "/Icons/ErrorIcon.png");// ?? new Texture2D(0, 0);
				}
				return kErrorIconTexture_;
			}
		}

		private GameObject objectToValidate_;
		private IList<IValidationError> cachedValidationErrors_;
		private HashSet<GameObject> objectsWithErrors_ = new HashSet<GameObject>();

		private void OnSceneGUI(SceneView sceneView) {
			if (cachedValidationErrors_ == null) {
				return;
			}

			Handles.BeginGUI();

			// BEGIN SCENE GUI
			float yPosition = 0.0f;
			foreach (IValidationError error in cachedValidationErrors_) {
				var componentError = error as ComponentValidationError;

				// NOTE (darren): it's possible that OnSceneGUI gets called after
				// the prefab is destroyed - don't do anything in that case
				if (componentError == null) {
					continue;
				}

				var linkRect = new Rect(kErrorWidth - kLinkWidth - kLinkPadding, yPosition + kLinkPadding, kLinkWidth, kErrorHeight - kLinkPadding);
				if (GUI.Button(linkRect, "Link")) {
					LinkValidationError(componentError);
				}

				var oldContentColor = GUI.contentColor;
				GUI.contentColor = PrefabSandbox.kErrorColor;

				var rect = new Rect(0.0f, yPosition, kErrorWidth, kErrorHeight);
				var errorDescription = string.Format("{0}->{1}.{2}", componentError.Component.gameObject.name, error.ObjectType.Name, error.MemberInfo.Name);

				if (GUI.Button(rect, errorDescription, kButtonStyle)) {
					Selection.activeGameObject = componentError.Component.gameObject;
				}

				GUI.contentColor = oldContentColor;

				if (GUI.Button(linkRect, "Link")) {
					// empty (no-action) button for the visual look
					// NOTE (darren): it appears the order in which GUI.button is
					// called determines the ordering for which button consumes the touch
					// but also the order is used to render :)
				}

				yPosition += kErrorHeight + kErrorSpacingHeight;
			}
			// END SCENE GUI
			Handles.EndGUI();
		}

		private void OnHierarchyWindowItemOnGUI(int instanceId, Rect selectionRect) {
			if (Event.current.type != EventType.Repaint) {
				return;
			}

			GameObject g = EditorUtility.InstanceIDToObject(instanceId) as GameObject;

			bool gameObjectHasError = objectsWithErrors_.Contains(g);
			if (gameObjectHasError) {
				float edgeLength = selectionRect.height - (2.0f * kErrorIconPadding);
				Rect errorIconRect = new Rect(selectionRect.x + selectionRect.width - kErrorIconPadding - edgeLength, selectionRect.y + kErrorIconPadding, edgeLength, edgeLength);
				GUI.DrawTexture(errorIconRect, kErrorIconTexture);
				EditorApplication.RepaintHierarchyWindow();
			}
		}

		private void RefreshValidationErrors() {
			cachedValidationErrors_ = Validator.Validate(objectToValidate_);

			objectsWithErrors_.Clear();
			if (cachedValidationErrors_ != null) {
				foreach (ComponentValidationError componentError in cachedValidationErrors_.Where(e => e is ComponentValidationError)) {
					objectsWithErrors_.Add(componentError.Component.gameObject);
				}
			}
		}

		private void LinkValidationError(ComponentValidationError componentError) {
			var selectedGameObject = Selection.activeGameObject;
			if (selectedGameObject == null) {
				Debug.LogWarning("Cannot link when no selected game object!");
				return;
			}

			var fieldInfo = componentError.MemberInfo as FieldInfo;
			if (fieldInfo != null) {
				var fieldType = fieldInfo.FieldType;
				if (fieldType == typeof(UnityEngine.GameObject)) {
					fieldInfo.SetValue(componentError.Component, selectedGameObject);
				} else if (typeof(UnityEngine.Component).IsAssignableFrom(fieldType)) {
					var linkedComponent = selectedGameObject.GetComponent(fieldType);
					if (linkedComponent == null) {
						Debug.LogWarning("LinkValidationError: Failed to find component of type: " + fieldType.Name + " on selected game object, cannot link!");
						return;
					}

					fieldInfo.SetValue(componentError.Component, linkedComponent);
				} else {
					Debug.LogWarning("LinkValidationError: Field is of unhandled type: " + fieldType.Name + ", cannot link!");
					return;
				}

				SceneView.RepaintAll();
				EditorUtility.SetDirty(componentError.Component);
				RefreshValidationErrors();
			}
		}
	}
}
