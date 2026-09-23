#if UNITY_6000_0_OR_NEWER
#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LIV.SDK.EditorSupport
{

	[InitializeOnLoad]
	public static class RenderGraphWarning
	{
		static RenderGraphWarning()
		{
			//Somehow it seems that we're still loaded every time we enter play mode
			//EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

			CheckRenderGraph();
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			if (PlayModeStateChange.EnteredPlayMode == state)
			{
				CheckRenderGraph();
			}
		}

		private static void CheckRenderGraph()
		{
#if LIV_UNIVERSAL_RENDER
#if UNITY_6000_4_OR_NEWER
			// Unity 6.4 (URP 17.4) removed the Render Graph compatibility mode and with it
			// ScriptableRenderPass.Execute, which the LIV Universal Render Pipeline integration is built on.
			// No project setting can bring that back, so point at the actual fix instead of Compatibility Mode.
			Debug.LogError(
				"LIV SDK " + LIV.SDK.Unity.SDKConstants.SDK_VERSION + " does not support Unity 6.4 or newer: Unity 6.4 removed the URP Render Graph compatibility mode and " +
				"ScriptableRenderPass.Execute, which the LIV Universal Render Pipeline integration is built on. " +
				"LIV mixed reality capture will not render on this Unity version. Use Unity 6.3 LTS or a LIV SDK version with Render Graph support.");
#else
			var settings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
			bool has_render_graph = settings != null;
			bool has_compat_mode = has_render_graph && settings.enableRenderCompatibilityMode;

			if (has_render_graph && !has_compat_mode)
				Debug.LogError(
					"When using the LIV SDK with Unity Editor Version >= 6000.0, LIV currently require enabling Compatibility Mode in RenderGraph settings.\nPlease enable Compatibility Mode, see <a href=https://mrc-docs.liv.tv/sdk-for-unity/universal-render-pipeline>the LIV SDK Documentation</a>");
#endif
#endif
		}
	}
}

#endif
#endif
