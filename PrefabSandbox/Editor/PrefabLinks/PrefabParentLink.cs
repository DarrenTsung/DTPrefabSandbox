using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DTPrefabSandbox.Internal {
	public class PrefabParentLink : ScriptableObject {
		[Header("Outlets")]
		public GameObject Prefab;

		[Space]
		public GameObject ParentPrefab;
		public GameObject Container;
	}
}
