#if DT_COMMAND_PALETTE
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

using DTCommandPalette;

namespace DTPrefabSandbox.Internal {
	public static class PrefabParentLinkCommands {
		[MethodCommand]
		public static void CreatePrefabParentLink() {
			GameObject selected = Selection.activeGameObject;
			if (selected == null) {
				Debug.LogWarning("Can't create prefab parent link because no gameObject selected!");
				return;
			}

			selected = PrefabUtility.GetPrefabParent(selected) as GameObject;

			GameObject prefabRoot = PrefabUtility.FindPrefabRoot(selected);
			if (prefabRoot == null) {
				Debug.LogWarning("Selected gameObject is not part of a prefab, can't create prefab parent link!");
				return;
			}

			CommandPaletteWindow.SelectPrefab("Set Child Prefab", (childPrefab) => {
				PrefabParentLinkManager.CreateNewLink(childPrefab, prefabRoot, selected);
			});
		}
	}
}
#endif
