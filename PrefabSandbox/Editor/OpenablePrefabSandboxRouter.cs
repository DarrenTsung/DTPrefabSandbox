using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using DTCommandPalette;

namespace DTPrefabSandbox {
    [InitializeOnLoad]
    public class PrefabSandboxCommandRouter {
        static PrefabSandboxCommandRouter() {
            PrefabAssetCommand.OnPrefabGUIDExecuted = PrefabSandboxCommandRouter.OpenSandboxWithGuid;
        }


        // PRAGMA MARK - Static Internal
        private static void OpenSandboxWithGuid(string guid) {
            PrefabSandbox.OpenPrefab(guid);
        }
    }
}
