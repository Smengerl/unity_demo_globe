using UnityEditor;
using UnityEngine;

namespace Tycoon.Demo.EditorTools
{
    /// <summary>
    /// If scripts get recompiled while the game is playing, the demo's runtime objects lose their state and would throw every frame.
    /// Play mode is stopped instead, with a hint in the console.
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeGuard
    {
        const string InPlayKey = "Tycoon.InPlayMode";

        static PlayModeGuard()
        {
            // the flag is only true between EnteredPlayMode and leaving it, so a reload that sees it set happened while playing
            if (SessionState.GetBool(InPlayKey, false))
            {
                SessionState.SetBool(InPlayKey, false);
                EditorApplication.delayCall += () =>
                {
                    if (!EditorApplication.isPlaying) return;
                    Debug.LogWarning("Scripts were recompiled during play, so play mode was stopped. Please start again.");
                    EditorApplication.isPlaying = false;
                };
            }

            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) SessionState.SetBool(InPlayKey, true);
                else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(InPlayKey, false);
            };
        }
    }
}
