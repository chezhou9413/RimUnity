using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimUnity.Editor
{
    internal static class ExportUiPolicy
    {
        //Resolve before material identity/context creation so all passes share one UI mode.
        internal static bool IsMarked(Object asset, ISet<string> paths)
        {
            if (!(asset is Material) && !(asset is Shader)) return false;
            if (paths.Contains(AssetDatabase.GetAssetPath(asset))) return true;
            return asset is Material material && material.shader != null
                && paths.Contains(AssetDatabase.GetAssetPath(material.shader));
        }
    }
}
