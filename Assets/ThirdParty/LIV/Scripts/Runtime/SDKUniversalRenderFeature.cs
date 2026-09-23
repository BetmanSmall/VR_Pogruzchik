#if LIV_UNIVERSAL_RENDER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_4_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LIV.SDK.Unity
{
    public class SDKPass : ScriptableRenderPass
    {
        public CommandBuffer commandBuffer;

#if !UNITY_6000_4_OR_NEWER
        // Unity 6.3 and earlier: URP still supports the Render Graph compatibility path
        // (Render Graph disabled / URP_COMPATIBILITY_MODE), so render passes are executed
        // through ScriptableRenderPass.Execute.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            context.ExecuteCommandBuffer(commandBuffer);
        }
#else
        // Unity 6.4 (URP 17.4) removed the Render Graph compatibility mode together with
        // ScriptableRenderPass.Execute(ScriptableRenderContext, ref RenderingData), so the method can no
        // longer be an override. It is kept as a regular member so that the existing call sites keep
        // compiling, but URP does not call it any more - see RecordRenderGraph below.
        public void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            context.ExecuteCommandBuffer(commandBuffer);
        }

        private static bool _reportedMissingRenderGraphImplementation = false;

        // Called by URP instead of Execute. The LIV render passes are still built on top of
        // ScriptableRenderPass.Execute, so they cannot render anything on Unity 6.4.
        // Report this once instead of letting URP spam its generic
        // "no RecordRenderGraph implementation" warning every frame while capturing.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_reportedMissingRenderGraphImplementation)
                return;

            _reportedMissingRenderGraphImplementation = true;
            Debug.LogError(
                "LIV SDK " + SDKConstants.SDK_VERSION + " does not support Unity 6.4 or newer: Unity 6.4 removed the URP Render Graph compatibility mode and " +
                "ScriptableRenderPass.Execute, which the LIV Universal Render Pipeline integration is built on. " +
                "LIV mixed reality capture will not render on this Unity version. Use Unity 6.3 LTS or a LIV SDK version with Render Graph support.");
        }
#endif
    }

    public class SDKUniversalRenderFeature : ScriptableRendererFeature
    {
        private const string FILE_NAME = "LIVUniversalRenderFeature";
        static List<SDKPass> passes = new List<SDKPass>();
        private bool _logAddRenderPasses = true;

        public static void AddPass(SDKPass pass)
        {
            passes.Add(pass);
        }

        public static void ClearPasses()
        {
            passes.Clear();
        }

        public override void Create()
        {
            _logAddRenderPasses = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            while (passes.Count > 0)
            {
                renderer.EnqueuePass(passes[0]);
                passes.RemoveAt(0);
            }

            if (_logAddRenderPasses)
            {
                Debug.Log("LIV URP Render Feature: Universal Render Pipeline Added Render Passes.");
                _logAddRenderPasses = false;
            }
        }


        private static SDKUniversalRenderFeature _instance;

        public static SDKUniversalRenderFeature instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<SDKUniversalRenderFeature>(FILE_NAME);

                if (_instance == null)
                {
                    _instance = ScriptableObject.CreateInstance<SDKUniversalRenderFeature>();
                    _instance.name = nameof(SDKUniversalRenderFeature);
                }

                return _instance;
            }
        }
    }
}
#endif