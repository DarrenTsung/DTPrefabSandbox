using System;
using UnityEditor;
using UnityEngine;

namespace DTPrefabSandbox {
    public static class AssetDatabaseUtil {
        public static T LoadAssetAtPath<T>(string assetPath) where T : class {
            return AssetDatabase.LoadAssetAtPath(assetPath, typeof(T)) as T;
        }

        public static string FindSpecificAsset(string findAssetsInput, bool required = true) {
            string[] guids = AssetDatabase.FindAssets(findAssetsInput);

            if (guids.Length <= 0) {
                if (required) Debug.LogError(string.Format("FindSpecificAsset: Can't find anything matching ({0}) anywhere in the project", findAssetsInput));
                return "";
            }

            if (guids.Length > 2) {
                if (required) Debug.LogError(string.Format("FindSpecificAsset: More than one file found for ({0}) in the project!", findAssetsInput));
                return "";
            }

            return guids[0];
        }
    }
}
