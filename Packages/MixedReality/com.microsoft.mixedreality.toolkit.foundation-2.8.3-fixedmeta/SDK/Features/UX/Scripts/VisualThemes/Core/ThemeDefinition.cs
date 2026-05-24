// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.MixedReality.Toolkit.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace Microsoft.MixedReality.Toolkit.UI
{
    /// <summary>
    /// Defines configuration properties and settings to use when initializing a class extending InteractableThemeBase
    /// </summary>
    [System.Serializable]
    public struct ThemeDefinition : ISerializationCallbackReceiver
    {
        private const string CompatTag = "[MRTK Compat] ThemeDefinition";

        /// <summary>
        /// Defines the type of Theme to associate with this definition. Type must be a class that extends InteractableThemeBase
        /// </summary>
        public Type ThemeType
        {
            get
            {
                if (Type == null)
                {
                    Type = ResolveThemeType(ClassName, AssemblyQualifiedName);
                    if (Type != null)
                    {
                        ClassName = Type.Name;
                        AssemblyQualifiedName = Type.AssemblyQualifiedName;
                    }
                }

                return Type;
            }
            set
            {
                if (!value.IsSubclassOf(typeof(InteractableThemeBase)))
                {
                    Debug.LogWarning($"Cannot assign type {value} that does not extend {typeof(InteractableThemeBase)} to ThemeDefinition");
                    return;
                }

                if (Type != value)
                {
                    Type = value;
                    ClassName = Type.Name;
                    AssemblyQualifiedName = Type.AssemblyQualifiedName;
                }
            }
        }

        // Unity cannot serialize System.Type, thus must save AssemblyQualifiedName
        // Field here for Runtime use
        [NonSerialized]
        private Type Type;

        [FormerlySerializedAs("Name")]
        [SerializeField]
        private string ClassName;

        [SerializeField]
        private string AssemblyQualifiedName;

        [FormerlySerializedAs("Properties")]
        [FormerlySerializedAs("StateProperties")]
        [SerializeField]
        private List<ThemeStateProperty> stateProperties;
        /// <summary>
        /// List of properties with values defined per state index (Example list of colors for different states)
        /// </summary>
        public List<ThemeStateProperty> StateProperties
        {
            get { return stateProperties; }
            set { stateProperties = value; }
        }

        [FormerlySerializedAs("CustomSettings")]
        [FormerlySerializedAs("CustomProperties")]
        [SerializeField]
        private List<ThemeProperty> customProperties;
        /// <summary>
        /// List of single-value properties defined for the entire Theme engine regardless of the current state
        /// </summary>
        public List<ThemeProperty> CustomProperties
        {
            get { return customProperties; }
            set { customProperties = value; }
        }

        [FormerlySerializedAs("Easing")]
        [SerializeField]
        private Easing easing;
        /// <summary>
        /// Object to configure easing between values. Type of Theme Engine, as defined by the ThemeType property, must have IsEasingSupported set to true
        /// </summary>
        public Easing Easing
        {
            get { return easing; }
            set { easing = value; }
        }

        /// <summary>
        /// Utility function to generate the default ThemeDefinition configuration for the provided type of Theme engine
        /// </summary>
        /// <typeparam name="T">type of Theme Engine to build default configuration for</typeparam>
        /// <returns>Default ThemeDefinition configuration for the provided them type</returns>
        public static ThemeDefinition? GetDefaultThemeDefinition<T>() where T : InteractableThemeBase
        {
            return GetDefaultThemeDefinition(typeof(T));
        }

        /// <summary>
        /// Utility function to generate the default ThemeDefinition configuration for the provided type of Theme engine
        /// </summary>
        /// <param name="themeType">type of Theme Engine to build default configuration for</param>
        /// <returns>Default ThemeDefinition configuration for the provided them type</returns>
        public static ThemeDefinition? GetDefaultThemeDefinition(Type themeType)
        {
            var theme = InteractableThemeBase.CreateTheme(themeType);
            if (theme != null)
            {
                return theme.GetDefaultThemeDefinition();
            }

            return null;
        }

        // Legacy migration helper for assets that contain ClassName but missing or invalid
        // AssemblyQualifiedName. Recovery is intentionally strict:
        // - explicit assembly-qualified type must resolve and be an InteractableThemeBase
        // - fallback by class name only is accepted only when unique
        // - ambiguous/missing cases return null so the theme remains unbound and safe.
        private static Type ResolveThemeType(string className, string assemblyQualifiedName)
        {
            if (!string.IsNullOrEmpty(assemblyQualifiedName))
            {
                var explicitType = Type.GetType(assemblyQualifiedName);
                if (explicitType == null)
                {
                    Debug.LogWarning($"{CompatTag} AssemblyQualifiedName '{assemblyQualifiedName}' could not be resolved for ClassName '{className}'.");
                }
                else if (!explicitType.IsSubclassOf(typeof(InteractableThemeBase)))
                {
                    Debug.LogWarning($"{CompatTag} Resolved type '{explicitType.FullName}' is not an InteractableThemeBase for ClassName '{className}'.");
                }
                else
                {
                    return explicitType;
                }
            }

            if (string.IsNullOrEmpty(className))
            {
                return null;
            }

            var matches = TypeCacheUtility.GetSubClasses<InteractableThemeBase>()
                .Where(t => t != null && t.Name == className)
                .ToArray();

            if (matches.Length == 1)
            {
                Debug.LogWarning($"{CompatTag} Restored legacy theme binding for ClassName '{className}' using class-name-only recovery.");
                return matches[0];
            }

            if (matches.Length == 0)
            {
                Debug.LogError($"{CompatTag} No InteractableThemeBase found for ClassName '{className}'.");
                return null;
            }

            Debug.LogError($"{CompatTag} Multiple InteractableThemeBase matches found for ClassName '{className}'. Manual fix required.");
            return null;
        }

        #region ISerializationCallbackReceiver implementation

        /// <inheritdoc/>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            // Backward compatibility at runtime in case some custom properties have been added in code after first serialization
            ThemeDefinition defaultDefinition = GetDefaultThemeDefinition(ThemeType).Value;

            if (defaultDefinition.CustomProperties.Count > CustomProperties.Count)
            {
                foreach (ThemeProperty prop in defaultDefinition.CustomProperties)
                {
                    if (!CustomProperties.Exists(p => p.Name == prop.Name))
                    {
                        CustomProperties.Add(new ThemeProperty()
                        {
                            Name = prop.Name,
                            Tooltip = prop.Tooltip,
                            Type = prop.Type,
                            Value = prop.Value,
                        });
                    }
                }
            }
        }

        /// <inheritdoc/>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
        }

        #endregion
    }
}
