using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DTPrefabSandbox.Internal {
	public class ClearMapOnPostProcess : AssetPostprocessor {
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
			PrefabParentLinkManager.ClearCachedMap();
		}
	}

	public static class PrefabParentLinkManager {
		// PRAGMA MARK - Public Interface
		// returns new parent (different gameObject if linked parents exist)
		public static GameObject CreateLinkedParentsForPrefab(GameObject prefab, GameObject previousParent) {
			PrefabParentLink parentLink = ParentLinkMap_.GetValueOrDefault(prefab);
			if (parentLink == null) {
				return previousParent;
			}

			GameObject instantiatedParent = GameObject.Instantiate(parentLink.ParentPrefab, parent: previousParent.transform);

			if (parentLink.Container == null) {
				Debug.LogWarning("No container in parentLink: " + parentLink);
			}

			string containerPath = parentLink.Container.FullName();
			string parentPrefabName = parentLink.ParentPrefab.name;
			if (!containerPath.StartsWith(parentPrefabName)) {
				Debug.LogWarning("Container path does not start with parentPrefabName!");
				return previousParent;
			}

			// remove parent name from path -> ex. containerPath = "LinkedObject/Child" -> "Child"
			containerPath = containerPath.Substring(parentPrefabName.Length + 1);
			Transform container = instantiatedParent.transform.Find(containerPath);
			if (container == null) {
				Debug.LogWarning("Failed to find container in instantiatedParent!");
				return previousParent;
			}

			return CreateLinkedParentsForPrefab(parentLink.ParentPrefab, container.gameObject);
		}

		public static void ClearCachedMap() {
			parentLinkMap_ = null;
		}

		public static void CreateNewLink(GameObject childPrefab, GameObject parentPrefab, GameObject container) {
			if (childPrefab == null || parentPrefab == null || container == null) {
				Debug.LogWarning("Can't create new link with invalid arguments!");
				return;
			}

			PrefabParentLink link = ScriptableObject.CreateInstance<PrefabParentLink>();
			link.Prefab = childPrefab;
			link.ParentPrefab = parentPrefab;
			link.Container = container;

			string assetsBasedDirectoryPath = "Assets/PrefabSandbox/PrefabParentLinks";
			Directory.CreateDirectory(assetsBasedDirectoryPath);

			// if not new asset, save to new asset
			int index = 0;
			string path;
			do {
				path = Path.Combine(assetsBasedDirectoryPath, GetNameForIndex(link, index));
				index++;
			} while (AssetDatabase.LoadAssetAtPath(path, typeof(UnityEngine.Object)) != null);

			string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path);
			AssetDatabase.CreateAsset(link, assetPathAndName);
			AssetDatabase.SaveAssets();
		}


		// PRAGMA MARK - Internal
		private static Dictionary<GameObject, PrefabParentLink> parentLinkMap_ = null;
		private static Dictionary<GameObject, PrefabParentLink> ParentLinkMap_ {
			get { return parentLinkMap_ ?? (parentLinkMap_ = AssetDatabaseUtil.AllAssetsOfType<PrefabParentLink>().ToMapWithKeys(parentLink => parentLink.Prefab)); }
		}

		private static string GetNameForIndex(PrefabParentLink link, int index) {
			if (index <= 0) {
				return link.Prefab.name + "Link.asset";
			}

			return string.Format("{0}Link{1}.asset", link.Prefab.name, index);
		}
	}
}
