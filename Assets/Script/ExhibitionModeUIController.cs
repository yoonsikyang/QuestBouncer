using Microsoft.MixedReality.Toolkit.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Wires the scene-authored Exhibition Mode menu buttons at runtime.
/// The finish button is now a fixed scene GameObject.
/// </summary>
public class ExhibitionModeUIController : MonoBehaviour
{
    private const string ExhibitionButtonPath = "MixedRealitySceneContent/Button Parent/Setting Menu/ButtonCollection/Exhibition Mode";
    private const string ExhibitionMenuPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu";
    private const string ManipulationButtonPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu/ButtonCollection/Exhibit Manipulation";
    private const string VelocityButtonPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu/ButtonCollection/Exhibit Velocity";
    private const string SliceButtonPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu/ButtonCollection/Exhibit Slice";
    private const string StreamlineButtonPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu/ButtonCollection/Exhibit Streamline";
    private const string WssButtonPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu/ButtonCollection/Exhibit Wss";
    private const string ResetButtonPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu/ButtonCollection/\uCD08\uAE30\uD654";
    private const string FinishButtonPath = "MixedRealitySceneContent/Button Parent/Exhibition Mode Menu/ButtonCollection/\uCCB4\uD5D8 \uC885\uB8CC";

    private static ExhibitionModeUIController instance;

    private ButtonControllerManager buttonController;
    private Interactable exhibitionModeButton;
    private Interactable manipulationButton;
    private Interactable velocityButton;
    private Interactable sliceButton;
    private Interactable streamlineButton;
    private Interactable wssButton;
    private Interactable resetButton;
    private Interactable finishButton;
    private GameObject exhibitionMenu;
    private TMP_FontAsset exhibitionFont;
    private bool referenceWired;
    private bool actionsWired;

    private UnityAction exhibitionModeAction;
    private UnityAction manipulationAction;
    private UnityAction velocityAction;
    private UnityAction sliceAction;
    private UnityAction streamlineAction;
    private UnityAction wssAction;
    private UnityAction resetAction;
    private UnityAction finishAction;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null || FindObjectOfType<ExhibitionModeUIController>() != null)
        {
            return;
        }

        GameObject root = new GameObject("ExhibitionModeUIController");
        instance = root.AddComponent<ExhibitionModeUIController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        exhibitionModeAction = HandleExhibitionModeClicked;
        manipulationAction = HandleManipulationClicked;
        velocityAction = HandleVelocityClicked;
        sliceAction = HandleSliceClicked;
        streamlineAction = HandleStreamlineClicked;
        wssAction = HandleWssClicked;
        resetAction = HandleResetClicked;
        finishAction = HandleFinishClicked;
    }

    private void Update()
    {
        EnsureReferences();
        TryWire();
        RefreshFinishButtonState();
    }

    private void EnsureReferences()
    {
        if (buttonController == null)
        {
            buttonController = FindObjectOfType<ButtonControllerManager>();
        }

        if (exhibitionFont == null)
        {
            exhibitionFont = ResolveExhibitionFont();
        }

        exhibitionMenu = exhibitionMenu ?? FindGameObjectByPath(ExhibitionMenuPath);
        ApplyFontRecursive(exhibitionMenu);

        exhibitionModeButton = exhibitionModeButton ?? ResolveButton(ExhibitionButtonPath);
        manipulationButton = manipulationButton ?? ResolveButton(ManipulationButtonPath);
        velocityButton = velocityButton ?? ResolveButton(VelocityButtonPath);
        sliceButton = sliceButton ?? ResolveButton(SliceButtonPath);
        streamlineButton = streamlineButton ?? ResolveButton(StreamlineButtonPath);
        wssButton = wssButton ?? ResolveButton(WssButtonPath);
        resetButton = resetButton ?? ResolveButton(ResetButtonPath);
        finishButton = finishButton ?? ResolveButton(FinishButtonPath);
    }

    private void TryWire()
    {
        if (!referenceWired && buttonController != null && exhibitionMenu != null)
        {
            buttonController.exhibitionMenu = exhibitionMenu;
            referenceWired = true;
        }

        if (actionsWired || buttonController == null)
        {
            return;
        }

        WireButton(exhibitionModeButton, exhibitionModeAction);
        WireButton(manipulationButton, manipulationAction);
        WireButton(velocityButton, velocityAction);
        WireButton(sliceButton, sliceAction);
        WireButton(streamlineButton, streamlineAction);
        WireButton(wssButton, wssAction);
        WireButton(resetButton, resetAction);
        WireButton(finishButton, finishAction);

        actionsWired =
            exhibitionModeButton != null &&
            manipulationButton != null &&
            velocityButton != null &&
            sliceButton != null &&
            streamlineButton != null &&
            wssButton != null &&
            resetButton != null &&
            finishButton != null;
    }

    private void RefreshFinishButtonState()
    {
        if (finishButton == null || buttonController == null || exhibitionMenu == null)
        {
            return;
        }

        bool shouldShow =
            buttonController.IsExhibitionModeActive() &&
            buttonController.CanManuallyFinishCurrentExhibitionExperience() &&
            exhibitionMenu.activeInHierarchy;

        if (finishButton.gameObject.activeSelf != shouldShow)
        {
            finishButton.gameObject.SetActive(shouldShow);
        }
    }

    private Interactable ResolveButton(string path)
    {
        GameObject buttonObject = FindGameObjectByPath(path);
        if (buttonObject == null)
        {
            return null;
        }

        ApplyFontRecursive(buttonObject);
        return buttonObject.GetComponent<Interactable>();
    }

    private TMP_FontAsset ResolveExhibitionFont()
    {
        if (buttonController != null && buttonController.exhibitionGuideFont != null)
        {
            return buttonController.exhibitionGuideFont;
        }

        TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (TMP_FontAsset font in fonts)
        {
            if (font != null && font.name.Contains("NotoSansKR"))
            {
                return font;
            }
        }

        return TMP_Settings.defaultFontAsset;
    }

    private void ApplyFontRecursive(GameObject root)
    {
        if (root == null || exhibitionFont == null)
        {
            return;
        }

        TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text label in labels)
        {
            if (label != null)
            {
                label.font = exhibitionFont;
            }
        }
    }

    private void WireButton(Interactable button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClick.RemoveAllListeners();
        button.OnClick.RemoveListener(action);
        button.OnClick.AddListener(action);
    }

    private static GameObject FindGameObjectByPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        GameObject activeObject = GameObject.Find(path);
        if (activeObject != null)
        {
            return activeObject;
        }

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform transform in transforms)
        {
            if (transform == null || !transform.gameObject.scene.IsValid())
            {
                continue;
            }

            if (GetHierarchyPath(transform) == path)
            {
                return transform.gameObject;
            }
        }

        return null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string current = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            current = parent.name + "/" + current;
            parent = parent.parent;
        }

        return current;
    }

    private void HandleExhibitionModeClicked()
    {
        buttonController?.ToggleExhibitionMenu();
    }

    private void HandleManipulationClicked()
    {
        buttonController?.StartExhibitionManipulationMode();
    }

    private void HandleVelocityClicked()
    {
        buttonController?.StartExhibitionVelocityMode();
    }

    private void HandleSliceClicked()
    {
        buttonController?.StartExhibitionSliceMode();
    }

    private void HandleStreamlineClicked()
    {
        buttonController?.StartExhibitionStreamlineMode();
    }

    private void HandleWssClicked()
    {
        buttonController?.StartExhibitionWssMode();
    }

    private void HandleResetClicked()
    {
        buttonController?.ReturnToExhibitionHome();
    }

    private void HandleFinishClicked()
    {
        buttonController?.ShowCurrentExhibitionCompletionPrompt();
    }
}
