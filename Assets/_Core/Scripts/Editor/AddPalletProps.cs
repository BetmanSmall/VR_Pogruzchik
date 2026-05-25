using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VR_Pogruzchik.Trailer.Editor
{
    /// <summary>
    /// Adds pallets and boxes to TrailerScene for cinematic shots.
    /// Run from Window -> VR Pogruzchik -> Add Pallets & Boxes
    /// </summary>
    public class AddPalletProps
    {
        private const string TrailerScenePath = "Assets/_Core/Scenes/TrailerScene/TrailerScene.unity";
        private const string PalletPrefabPath = "Assets/XRI Starter Kit/Assets/MiniGames/Forklift/Official Unity Assets/Warehouse/Prefabs/Props/Wood_Pallet_01a_snaps011.prefab";
        private const string BoxPrefabPath = "Assets/XRI Starter Kit/Assets/MiniGames/Forklift/Official Unity Assets/Warehouse/Prefabs/Props/Box_Pallet_02a_snaps011.prefab";
        private const string ShelfPrefabPath = "Assets/XRI Starter Kit/Assets/MiniGames/Forklift/Official Unity Assets/Warehouse/Prefabs/Props/Shelf_01a_snaps011.prefab";

        [MenuItem("Window/VR Pogruzchik/Add Pallets & Boxes")]
        public static void AddPallets()
        {
            var scene = EditorSceneManager.OpenScene(TrailerScenePath, OpenSceneMode.Single);

            // Load prefabs
            var palletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PalletPrefabPath);
            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath);
            var shelfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShelfPrefabPath);

            if (!palletPrefab || !boxPrefab)
            {
                Debug.LogError("Prefabs not found! Check paths.");
                return;
            }

            // Find the Forklift to place objects around it
            GameObject forklift = GameObject.Find("Forklift");
            Transform forkliftTransform = forklift ? forklift.transform : null;
            Vector3 basePos = forkliftTransform ? forkliftTransform.position : Vector3.zero;

            // Container for organized props
            var propsContainer = new GameObject("Trailer_Props");
            
            // --- Pallets in front-right area ---
            CreatePrefab(palletPrefab, "Pallet_FrontRight_1", propsContainer.transform, 
                basePos + new Vector3(3f, 0f, 1.5f), Quaternion.identity);
            CreatePrefab(boxPrefab, "Box_OnPallet_01", propsContainer.transform, 
                basePos + new Vector3(3f, 0.5f, 1.5f), Quaternion.identity);
            
            CreatePrefab(palletPrefab, "Pallet_FrontRight_2", propsContainer.transform, 
                basePos + new Vector3(5f, 0f, 1.5f), Quaternion.identity);
            CreatePrefab(boxPrefab, "Box_OnPallet_02", propsContainer.transform, 
                basePos + new Vector3(5f, 0.5f, 1.5f), Quaternion.identity);

            // --- Pallets in front-left area ---
            CreatePrefab(palletPrefab, "Pallet_FrontLeft_1", propsContainer.transform, 
                basePos + new Vector3(-3f, 0f, 1.5f), Quaternion.Euler(0, 90, 0));
            CreatePrefab(boxPrefab, "Box_OnPallet_03", propsContainer.transform, 
                basePos + new Vector3(-3f, 0.5f, 1.5f), Quaternion.identity);

            // --- Some boxes scattered around ---
            CreatePrefab(boxPrefab, "Box_Ground_1", propsContainer.transform, 
                basePos + new Vector3(-4f, 0f, 0f), Quaternion.Euler(0, 45, 0));
            CreatePrefab(boxPrefab, "Box_Ground_2", propsContainer.transform, 
                basePos + new Vector3(4f, 0f, -1f), Quaternion.Euler(0, -30, 0));

            // --- Shelves in the back for environment ---
            if (shelfPrefab)
            {
                CreatePrefab(shelfPrefab, "Shelf_Back_1", propsContainer.transform, 
                    basePos + new Vector3(-5f, 0f, -4f), Quaternion.identity);
                CreatePrefab(shelfPrefab, "Shelf_Back_2", propsContainer.transform, 
                    basePos + new Vector3(5f, 0f, -4f), Quaternion.Euler(0, 180, 0));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Add Pallets] Props added to TrailerScene at pos {basePos}");
        }

        private static GameObject CreatePrefab(GameObject prefab, string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.rotation = rotation;
            return go;
        }
    }
}