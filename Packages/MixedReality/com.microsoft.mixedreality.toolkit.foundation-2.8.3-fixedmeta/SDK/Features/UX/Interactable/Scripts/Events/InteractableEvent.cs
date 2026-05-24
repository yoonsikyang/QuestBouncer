// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.MixedReality.Toolkit.Utilities;
using Microsoft.MixedReality.Toolkit.Utilities.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Microsoft.MixedReality.Toolkit.UI
{
    /// <summary>
    /// Event base class for events attached to Interactables.
    /// </summary>
    [System.Serializable]
    public class InteractableEvent
    {
        private const string CompatTag = "[MRTK Compat] InteractableEvent";

        /// <summary>
        /// Base Event used to initialize EventReceiver class
        /// </summary>
        public UnityEvent Event = new UnityEvent();

        /// <summary>
        /// ReceiverBase instantiation for this InteractableEvent. Used at runtime by Interactable class
        /// </summary>
        [NonSerialized]
        public ReceiverBase Receiver;

        /// <summary>
        /// Defines the type of Receiver to associate. Type must be a class that extends ReceiverBase
        /// </summary>
        public Type ReceiverType
        {
            get
            {
                if (receiverType == null)
                {
                    if (string.IsNullOrEmpty(AssemblyQualifiedName))
                    {
                        return null;
                    }

                    receiverType = Type.GetType(AssemblyQualifiedName);
                }

                return receiverType;
            }
            set
            {
                if (!value.IsSubclassOf(typeof(ReceiverBase)))
                {
                    Debug.LogWarning($"Cannot assign type {value} that does not extend {typeof(ReceiverBase)} to ThemeDefinition");
                    return;
                }

                if (receiverType != value)
                {
                    receiverType = value;
                    ClassName = receiverType.Name;
                    AssemblyQualifiedName = receiverType.AssemblyQualifiedName;
                }
            }
        }

        // Unity cannot serialize System.Type, thus must save AssemblyQualifiedName
        // Field here for Runtime use
        [NonSerialized]
        private Type receiverType;

        [SerializeField]
        private string ClassName;

        [SerializeField]
        private string AssemblyQualifiedName;

        [SerializeField]
        private List<InspectorPropertySetting> Settings = new List<InspectorPropertySetting>();

        /// <summary>
        /// Create the event and setup the values from the inspector. If the asset is invalid,
        /// returns null.
        /// </summary>
        public static ReceiverBase CreateReceiver(InteractableEvent iEvent)
        {
            if (string.IsNullOrEmpty(iEvent.ClassName))
            {
                // If the class name of this event is empty, the asset is invalid and loading types will throw errors. Return null.
                return null;
            }

            var resolvedType = ResolveReceiverType(iEvent);
            if (resolvedType == null)
            {
                Debug.LogError($"{CompatTag} Cannot resolve receiver for ClassName '{iEvent.ClassName}' and AssemblyQualifiedName '{iEvent.AssemblyQualifiedName}'. Asset is treated as unbound.");
                return null;
            }

            iEvent.ReceiverType = resolvedType;
            ReceiverBase newEvent = (ReceiverBase)Activator.CreateInstance(iEvent.ReceiverType, iEvent.Event);
            InspectorGenericFields<ReceiverBase>.LoadSettings(newEvent, iEvent.Settings);

            return newEvent;
        }

        // Legacy migration helper for assets that contain ClassName but missing or invalid
        // AssemblyQualifiedName. Recovery is intentionally strict:
        // - explicit assembly-qualified type must resolve and be a ReceiverBase
        // - fallback by class name only is accepted only when unique
        // - ambiguous/missing cases return null so the event remains unbound and safe.
        private static Type ResolveReceiverType(InteractableEvent iEvent)
        {
            if (!string.IsNullOrEmpty(iEvent.AssemblyQualifiedName))
            {
                var explicitType = Type.GetType(iEvent.AssemblyQualifiedName);
                if (explicitType == null)
                {
                    Debug.LogWarning($"{CompatTag} AssemblyQualifiedName '{iEvent.AssemblyQualifiedName}' could not be resolved for ClassName '{iEvent.ClassName}'.");
                }
                else if (!explicitType.IsSubclassOf(typeof(ReceiverBase)))
                {
                    Debug.LogWarning($"{CompatTag} Resolved type '{explicitType.FullName}' is not a ReceiverBase for ClassName '{iEvent.ClassName}'.");
                }
                else
                {
                    return explicitType;
                }
            }

            if (string.IsNullOrEmpty(iEvent.ClassName))
            {
                return null;
            }

            var matches = TypeCacheUtility.GetSubClasses<ReceiverBase>()
                .Where(t => t != null && t.Name == iEvent.ClassName)
                .ToArray();

            if (matches.Length == 1)
            {
                Debug.LogWarning($"{CompatTag} Restored legacy receiver binding for ClassName '{iEvent.ClassName}' using class-name-only recovery.");
                return matches[0];
            }

            if (matches.Length == 0)
            {
                Debug.LogError($"{CompatTag} No ReceiverBase found for ClassName '{iEvent.ClassName}'.");
                return null;
            }

            Debug.LogError($"{CompatTag} Multiple ReceiverBase matches found for ClassName '{iEvent.ClassName}'. Manual fix required.");
            return null;
        }
    }
}
