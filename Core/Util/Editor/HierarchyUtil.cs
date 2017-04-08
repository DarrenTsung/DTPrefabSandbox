using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DTPrefabSandbox {
	public static class HierarchyUtil {
		public enum FoldValue {
			EXPANDED,
			COLLAPSED
		}

		public static bool BoolValue(this FoldValue fv) {
			switch (fv) {
				case FoldValue.EXPANDED:
					return true;
				case FoldValue.COLLAPSED:
				default:
					return false;
			}
		}

		public static void CollapseAllObjectsInHierarchy() {
			SetFoldValueForAllGameObjectsInHiearchy(FoldValue.COLLAPSED);
		}

		public static void ExpandAllObjectsInHierarchy() {
			SetFoldValueForAllGameObjectsInHiearchy(FoldValue.EXPANDED);
		}

		public static void ExpandCurrentSelectedObjectInHierarchy() {
			SetFoldValueForGameObjectInHiearchyRecursive(Selection.activeGameObject, FoldValue.EXPANDED);
		}

		private static void SetFoldValueForAllGameObjectsInHiearchy(FoldValue fv) {
			var toplevelGos = Object.FindObjectsOfType<GameObject>().Where(g => g.transform.parent == null);

			foreach (GameObject g in toplevelGos) {
				SetFoldValueForGameObjectInHiearchyRecursive(g, fv);
			}
		}

		private static void SetFoldValueForGameObjectInHiearchyRecursive(GameObject gameObject, FoldValue fv) {
			var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
			var methodInfo = type.GetMethod("SetExpandedRecursive");

			EditorApplication.ExecuteMenuItem("Window/Hierarchy");
			EditorWindow window = EditorWindow.focusedWindow;

			methodInfo.Invoke(window, new object[] {
				gameObject.GetInstanceID(),
				fv.BoolValue()
				});
		}
	}
}
