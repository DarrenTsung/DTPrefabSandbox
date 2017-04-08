using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DTPrefabSandbox {
	public static class PrefabSandboxOnOpenAssetRouter {
		[OnOpenAssetAttribute(1)]
		public static bool OnOpenAsset(int instanceID, int line) {
			var gameObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
			if (gameObject == null) {
				return false;
			}

			return PrefabSandbox.OpenPrefab(gameObject);
		}
	}
}
