using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DTPrefabSandbox {
	public static class PrefabSandboxConfiguration {
		public static bool OpenAssetPrefabsInPrefabSandbox {
			get { return EditorPrefs.GetBool("PrefabSandboxConfiguration::OpenAssetPrefabsInPrefabSandbox", defaultValue: true); }
			set { EditorPrefs.SetBool("PrefabSandboxConfiguration::OpenAssetPrefabsInPrefabSandbox", value); }
		}

		[PreferenceItem("DTPrefabSandbox")]
		public static void PreferencesGUI() {
			OpenAssetPrefabsInPrefabSandbox = EditorGUILayout.Toggle("Double-Click to Open Prefabs", OpenAssetPrefabsInPrefabSandbox);
		}
	}
}
