using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
// using Cinemachine;
using MikeNspired.XRIStarterKit;
using Unity.Cinemachine;

namespace VR_Pogruzchik.Trailer.Editor
{
    /// <summary>
    /// Editor utility to setup the TrailerScene with all required components.
    /// Run from Window -> VR Pogruzchik -> Setup Trailer Scene
    /// </summary>
    public class SetupTrailerScene
    {
        private const string TrailerScenePath = "Assets/_Core/Scenes/TrailerScene/TrailerScene.unity";
        private const string ForkliftPrefabGuid = "91d53b3ff8e79a14b87c4b6ef3326e15";
        private const string ForkliftAreaPrefabGuid = "3e8313ceea848c145a7dbaf595044d4d";
        private const string PlayerPrefabGuid = "34dd58110285e484ea514de8096e644c";

        [MenuItem("Window/VR Pogruzchik/Setup Trailer Scene")]
        public static void SetupScene()
        {
            // Open the scene
            var scene = EditorSceneManager.OpenScene(TrailerScenePath, OpenSceneMode.Single);
            var rootObjects = scene.GetRootGameObjects();

            // Find existing objects
            GameObject plane = null;
            GameObject player = null;
            GameObject forklift = null;
            GameObject forkliftArea = null;
            GameObject dirLight = null;

            foreach (var obj in rootObjects)
            {
                switch (obj.name)
                {
                    case "Plane": plane = obj; break;
                    case "PLAYERs ": player = obj; break;
                    case "Forklift": forklift = obj; break;
                    case "Fork Lift Area": forkliftArea = obj; break;
                    case "Directional Light": dirLight = obj; break;
                }
            }

            if (!forklift)
            {
                Debug.LogError("Forklift not found in scene!");
                return;
            }

            if (!player)
            {
                Debug.LogError("Player not found in scene!");
                return;
            }

            // Create Trailer Camera parent object
            var trailerRoot = new GameObject("TrailerSetup");
            trailerRoot.transform.SetAsFirstSibling();

            // --- Create TrailerCamera with CinemachineBrain ---
            var trailerCamGO = new GameObject("TrailerCamera");
            trailerCamGO.transform.SetParent(trailerRoot.transform);
            trailerCamGO.transform.position = new Vector3(0, 15, -5);
            trailerCamGO.transform.rotation = Quaternion.Euler(70, 0, 0);
            var trailerCamera = trailerCamGO.AddComponent<Camera>();
            trailerCamera.clearFlags = CameraClearFlags.Skybox;
            trailerCamera.cullingMask = -1;
            trailerCamera.depth = 1;
            var audioListener = trailerCamGO.AddComponent<AudioListener>();
            
            // Add CinemachineBrain
            var brain = trailerCamGO.AddComponent<CinemachineBrain>();
            brain.ShowDebugText = false;
            brain.IgnoreTimeScale = false;
            brain.WorldUpOverride = null;
            
            // Start with camera disabled (TrailerController will enable it)
            trailerCamGO.SetActive(false);

            // --- Create Virtual Cameras ---
            
            // CM_Aerial - Top down view
            var cmAerial = CreateVirtualCamera("CM_Aerial", trailerRoot.transform,
                new Vector3(0, 20, 0), Quaternion.Euler(90, 0, 0));
            cmAerial.GetComponent<CinemachineCamera>().Priority = 10;
            
            // CM_Approach - Flying down to forklift
            var cmApproach = CreateVirtualCamera("CM_Approach", trailerRoot.transform,
                new Vector3(5, 8, -5), Quaternion.Euler(40, -30, 0));
            cmApproach.GetComponent<CinemachineCamera>().Priority = 0;
            
            // CM_PlayerEntry - Side view on player entering forklift
            var cmPlayerEntry = CreateVirtualCamera("CM_PlayerEntry", trailerRoot.transform,
                new Vector3(3, 2, -5), Quaternion.Euler(10, -20, 0));
            cmPlayerEntry.GetComponent<CinemachineCamera>().Priority = 0;
            
            // CM_FirstPerson - Will be attached to player's head in runtime
            var cmFirstPerson = CreateVirtualCamera("CM_FirstPerson", trailerRoot.transform,
                Vector3.zero, Quaternion.identity);
            cmFirstPerson.GetComponent<CinemachineCamera>().Priority = 0;
            
            // CM_ExitWide - Pulling away from forklift
            var cmExitWide = CreateVirtualCamera("CM_ExitWide", trailerRoot.transform,
                new Vector3(-5, 12, -8), Quaternion.Euler(55, 30, 0));
            cmExitWide.GetComponent<CinemachineCamera>().Priority = 0;

            // Disable all virtual cameras initially
            cmAerial.SetActive(false);
            cmApproach.SetActive(false);
            cmPlayerEntry.SetActive(false);
            cmFirstPerson.SetActive(false);
            cmExitWide.SetActive(false);

            // --- Create TrailerController ---
            var controllerGO = new GameObject("TrailerController");
            controllerGO.transform.SetParent(trailerRoot.transform);
            var controller = controllerGO.AddComponent<TrailerController>();

            // Assign references
            SerializedObject serializedController = new SerializedObject(controller);

            serializedController.FindProperty("trailerCamera").objectReferenceValue = trailerCamGO;

            serializedController.FindProperty("cmAerial").objectReferenceValue = cmAerial;
            serializedController.FindProperty("cmApproach").objectReferenceValue = cmApproach;
            serializedController.FindProperty("cmPlayerEntry").objectReferenceValue = cmPlayerEntry;
            serializedController.FindProperty("cmFirstPerson").objectReferenceValue = cmFirstPerson;
            serializedController.FindProperty("cmExitWide").objectReferenceValue = cmExitWide;

            serializedController.FindProperty("playerRig").objectReferenceValue = player.transform;

            // Find forklift components
            var forkliftControls = forklift.GetComponentInChildren<ForkliftControls>();
            var forkliftVehicle = forklift.GetComponentInChildren<ArticulationBodyVehicle>();
            var vehicleTeleport = forklift.GetComponentInChildren<VehicleTeleportPlayer>();

            serializedController.FindProperty("forkliftControls").objectReferenceValue = forkliftControls;
            serializedController.FindProperty("forkliftVehicle").objectReferenceValue = forkliftVehicle;
            serializedController.FindProperty("vehicleTeleport").objectReferenceValue = vehicleTeleport;

            serializedController.ApplyModifiedProperties();

            // --- Create Timeline asset ---
            var timelinePath = "Assets/_Core/Scenes/TrailerScene/TrailerTimeline.playable";
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, timelinePath);
            AssetDatabase.SaveAssets();

            // Assign timeline to controller
            serializedController.Update();
            serializedController.FindProperty("timeline").objectReferenceValue = timeline;
            serializedController.ApplyModifiedProperties();

            // --- Add Recorder ---
            // Create Recorder directory if needed
            var recorderDir = "Assets/_Core/Scenes/TrailerScene/Recordings";
            if (!AssetDatabase.IsValidFolder(recorderDir))
            {
                System.IO.Directory.CreateDirectory(recorderDir);
                AssetDatabase.Refresh();
            }

            // Save scene
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Setup] TrailerScene setup complete! Objects created:");
            Debug.Log($"  - TrailerSetup > TrailerCamera (with CinemachineBrain)");
            Debug.Log($"  - TrailerSetup > CM_Aerial, CM_Approach, CM_PlayerEntry, CM_FirstPerson, CM_ExitWide");
            Debug.Log($"  - TrailerSetup > TrailerController (references assigned)");
            Debug.Log($"  - Timeline asset created at: {timelinePath}");
            Debug.Log($"  - All 5 cameras disabled, ready for TrailerController to enable them");
        }

        private static GameObject CreateVirtualCamera(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.rotation = rotation;
            
            var cmCam = go.AddComponent<CinemachineCamera>();
            cmCam.Priority = 0;
            
            return go;
        }
    }
}