using System.Collections;
using Microsoft.MixedReality.Toolkit.UI;
using UnityEngine;

/// <summary>
/// MRTK Interactable instances in this scene can enter play mode with a null StateManager,
/// which breaks button clicks. Refresh them once the scene is up so their state machines rebuild.
/// </summary>
public class MrtkInteractableBootstrapFix : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<MrtkInteractableBootstrapFix>() != null)
        {
            return;
        }

        GameObject root = new GameObject("MrtkInteractableBootstrapFix");
        DontDestroyOnLoad(root);
        root.AddComponent<MrtkInteractableBootstrapFix>();
    }

    private IEnumerator Start()
    {
        yield return null;
        RefreshAll();

        yield return new WaitForSeconds(0.5f);
        RefreshAll();

        yield return new WaitForSeconds(1.0f);
        RefreshAll();
    }

    private static void RefreshAll()
    {
        Interactable[] interactables = Resources.FindObjectsOfTypeAll<Interactable>();
        int refreshedCount = 0;

        foreach (Interactable interactable in interactables)
        {
            if (interactable == null || !interactable.gameObject.scene.IsValid())
            {
                continue;
            }

            try
            {
                interactable.RefreshSetup();
                refreshedCount++;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MRTK Fix] Failed to refresh {interactable.name}: {ex.Message}");
            }
        }

        Debug.Log($"[MRTK Fix] Refreshed {refreshedCount} interactables.");
    }
}
