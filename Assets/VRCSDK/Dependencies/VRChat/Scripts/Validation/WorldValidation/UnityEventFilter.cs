using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Video;
using VRC.SDK3.Components;
#if UDON
using VRC.Udon;
#endif
#if !VRC_CLIENT && UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;

#endif

namespace VRC.Core
{
    public class UnityEventFilter
    {
        // These types are will always be prohibited even if they are derived from an allowed type. 
        private static readonly HashSet<Type> _prohibitedUIEventTargetTypes = new HashSet<Type>
        {
            #if VRC_CLIENT
            typeof(RenderHeads.Media.AVProVideo.MediaPlayer),
            #endif
            typeof(VRCUrlInputField),
            typeof(VideoPlayer)
        };

        private static readonly Lazy<Dictionary<Type, UnityEventTargetMethodAccessFilter>> _allowedUnityEventTargetTypes =
            new Lazy<Dictionary<Type, UnityEventTargetMethodAccessFilter>>(GetRuntimeUnityEventTargetAccessFilterDictionary);

        private static Dictionary<Type, UnityEventTargetMethodAccessFilter> AllowedUnityEventTargetTypes => _allowedUnityEventTargetTypes.Value;

        private static readonly Lazy<int> _debugLevel = new Lazy<int>(InitializeLogging);
        private static int DebugLevel => _debugLevel.Value;

        // Builds a HashSet of allowed types, and their derived types, and removes explicitly prohibited types. 
        private static Dictionary<Type, UnityEventTargetMethodAccessFilter> GetRuntimeUnityEventTargetAccessFilterDictionary()
        {
            Dictionary<Type, UnityEventTargetMethodAccessFilter> accessFilterDictionary = new Dictionary<Type, UnityEventTargetMethodAccessFilter>(_initialTargetAccessFilters);
            AddDerivedTypes(accessFilterDictionary);
            RemoveProhibitedTypes(accessFilterDictionary);

            #if VERBOSE_EVENT_SANITIZATION_LOGGING
            StringBuilder stringBuilder = new StringBuilder();
            foreach(KeyValuePair<Type, UnityEventTargetMethodAccessFilter> entry in accessFilterDictionary)
            {
                stringBuilder.AppendLine(entry.Key.FullName);
                UnityEventTargetMethodAccessFilter targetMethodAccessFilter = entry.Value;
                foreach(string targetMethod in targetMethodAccessFilter.GetTargetMethodNames())
                {
                    stringBuilder.AppendLine($"    {targetMethod}");
                }

                stringBuilder.AppendLine();
            }

            VerboseLog(stringBuilder.ToString());
            #endif

            return accessFilterDictionary;
        }

        #if !VRC_CLIENT && UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod]
        private static void SetupPlayMode()
        {
            EditorApplication.playModeStateChanged += RunFilteringOnPlayModeEntry;
        }

        private static void RunFilteringOnPlayModeEntry(PlayModeStateChange playModeStateChange)
        {
            switch(playModeStateChange)
            {
                case PlayModeStateChange.EnteredPlayMode:
                {
                    for(int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                    {
                        Scene currentScene = SceneManager.GetSceneAt(sceneIndex);
                        List<GameObject> rootGameObjects = new List<GameObject>();
                        currentScene.GetRootGameObjects(rootGameObjects);

                        FilterUIEvents(rootGameObjects);
                    }

                    break;
                }
                case PlayModeStateChange.EnteredEditMode:
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                {
                    return;
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(playModeStateChange), playModeStateChange, null);
                }
            }
        }
        #endif

        private static int InitializeLogging()
        {
            int hashCode = typeof(UnityEventFilter).GetHashCode();
            Logger.DescribeDebugLevel(hashCode, "UnityEventFilter", Logger.Color.red);
            Logger.AddDebugLevel(hashCode);
            return hashCode;
        }

        [PublicAPI]
        public static void FilterUIEvents(GameObject gameObject)
        {
            List<UIBehaviour> uiBehaviours = new List<UIBehaviour>();
            gameObject.GetComponentsInChildren(true, uiBehaviours);

            FilterUIBehaviourEvents(uiBehaviours);
        }

        [PublicAPI]
        public static void FilterUIEvents(List<GameObject> gameObjects)
        {
            HashSet<UIBehaviour> uiBehaviours = new HashSet<UIBehaviour>();
            List<UIBehaviour> uiBehavioursWorkingList = new List<UIBehaviour>();
            foreach(GameObject gameObject in gameObjects)
            {
                gameObject.GetComponentsInChildren(true, uiBehavioursWorkingList);
                uiBehaviours.UnionWith(uiBehavioursWorkingList);
            }

            FilterUIBehaviourEvents(uiBehaviours);
        }

        private static void FilterUIBehaviourEvents(IEnumerable<UIBehaviour> uiBehaviours)
        {
            Dictionary<Type, List<UIBehaviour>> uiBehavioursByType = new Dictionary<Type, List<UIBehaviour>>();
            foreach(UIBehaviour uiBehaviour in uiBehaviours)
            {
                if(uiBehaviour == null)
                {
                    continue;
                }

                Type uiBehaviourType = uiBehaviour.GetType();
                if(!uiBehavioursByType.TryGetValue(uiBehaviourType, out List<UIBehaviour> uiBehavioursOfType))
                {
                    uiBehavioursByType.Add(uiBehaviourType, new List<UIBehaviour> {uiBehaviour});
                    continue;
                }

                uiBehavioursOfType.Add(uiBehaviour);
            }

            foreach(KeyValuePair<Type, List<UIBehaviour>> uiBehavioursOfTypeKvp in uiBehavioursByType)
            {
                Type uiBehaviourType = uiBehavioursOfTypeKvp.Key;
                FieldInfo[] fieldInfos = uiBehaviourType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                List<FieldInfo> unityEventFieldInfos = new List<FieldInfo>();
                foreach(FieldInfo fieldInfo in fieldInfos)
                {
                    if(typeof(UnityEventBase).IsAssignableFrom(fieldInfo.FieldType))
                    {
                        unityEventFieldInfos.Add(fieldInfo);
                    }
                }

                if(unityEventFieldInfos.Count <= 0)
                {
                    continue;
                }

                FieldInfo persistentCallsGroupFieldInfo = typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic);
                if(persistentCallsGroupFieldInfo == null)
                {
                    VerboseLog($"Could not find 'm_PersistentCalls' on UnityEventBase.");
                    return;
                }

                foreach(UIBehaviour uiBehaviour in uiBehavioursOfTypeKvp.Value)
                {
                    VerboseLog($"Checking '{uiBehaviour.name} for UI Events.", uiBehaviour);
                    foreach(FieldInfo unityEventFieldInfo in unityEventFieldInfos)
                    {
                        VerboseLog($"Checking field '{unityEventFieldInfo.Name}' on '{uiBehaviour.name}.", uiBehaviour);
                        UnityEventBase unityEventBase = unityEventFieldInfo.GetValue(uiBehaviour) as UnityEventBase;
                        if(unityEventBase == null)
                        {
                            VerboseLog($"Null '{unityEventFieldInfo.Name}' UnityEvent on {uiBehaviour.name}.", uiBehaviour);
                            continue;
                        }

                        int numEventListeners = unityEventBase.GetPersistentEventCount();
                        VerboseLog($"There are '{numEventListeners}' on event '{unityEventFieldInfo.Name}' on '{uiBehaviour.name}.", uiBehaviour);
                        for(int index = 0; index < numEventListeners; index++)
                        {
                            string persistentMethodName = unityEventBase.GetPersistentMethodName(index);

                            UnityEngine.Object persistentTarget = unityEventBase.GetPersistentTarget(index);
                            if(persistentTarget == null)
                            {
                                VerboseLog($"The target for listener '{index}' on event '{unityEventFieldInfo.Name}' on '{uiBehaviour.name} is null.", uiBehaviour);
                                continue;
                            }

                            if(IsTargetPermitted(persistentTarget, persistentMethodName))
                            {
                                VerboseLog(
                                    $"Allowing event '{unityEventFieldInfo.Name}' on '{uiBehaviour.name}' to call '{persistentMethodName}' on target '{persistentTarget.name}'.",
                                    uiBehaviour);

                                continue;
                            }

                            LogRemoval(
                                $"Events on '{uiBehaviour.name}' were removed because one of them targeted a prohibited type '{persistentTarget.GetType().Name}', method '{persistentMethodName}' or object '{persistentTarget.name}'.",
                                uiBehaviour);

                            unityEventFieldInfo.SetValue(uiBehaviour, Activator.CreateInstance(unityEventBase.GetType()));
                            break;
                        }
                    }
                }
            }
        }

        [Conditional("VERBOSE_EVENT_SANITIZATION_LOGGING")]
        private static void VerboseLog(string message, UnityEngine.Object target = null)
        {
            Logger.LogWarning(message, DebugLevel, target);
        }

        private static void LogRemoval(string message, UnityEngine.Object target = null)
        {
            Logger.LogWarning(message, DebugLevel, target);
        }

        private static bool IsTargetPermitted(UnityEngine.Object target, string targetMethod)
        {
            // Block anything blacklisted by Udon to prevent UnityEvents from being used to bypass the blacklist.
            // NOTE: This will only block events targeting objects that are blacklisted before the UnityEventSanitizer is run.
            //       If objects are added to the blacklist after scene loading has finished it will be necessary to re-run the UnityEventSanitizer.
            #if UDON
            if(UdonManager.Instance.IsBlacklisted(target))
            {
                return false;
            }
            #endif

            Type persistentTargetType = target.GetType();
            if(!AllowedUnityEventTargetTypes.TryGetValue(persistentTargetType, out UnityEventTargetMethodAccessFilter accessFilter))
            {
                return false;
            }

            return accessFilter.IsTargetMethodAllowed(targetMethod);
        }

        // Adds types derived from whitelisted types.
        private static void AddDerivedTypes(Dictionary<Type, UnityEventTargetMethodAccessFilter> accessFilterDictionary)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach(Assembly assembly in assemblies)
            {
                foreach(Type type in assembly.GetTypes())
                {
                    if(accessFilterDictionary.ContainsKey(type))
                    {
                        continue;
                    }

                    if(!typeof(Component).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    Type currentType = type;
                    while(currentType != typeof(object) && currentType != null)
                    {
                        if(accessFilterDictionary.TryGetValue(currentType, out UnityEventTargetMethodAccessFilter accessFilter))
                        {
                            accessFilterDictionary.Add(type, accessFilter);
                            break;
                        }

                        currentType = currentType.BaseType;
                    }
                }
            }
        }

        // Removes prohibited types and types derived from them.
        private static void RemoveProhibitedTypes(Dictionary<Type, UnityEventTargetMethodAccessFilter> accessFilterDictionary)
        {
            foreach(Type prohibitedType in _prohibitedUIEventTargetTypes)
            {
                foreach(Type accessFilterType in accessFilterDictionary.Keys.ToArray())
                {
                    if(prohibitedType.IsAssignableFrom(accessFilterType))
                    {
                        accessFilterDictionary.Remove(accessFilterType);
                    }
                }
            }
        }

        private static readonly List<string> _defaultDeniedUnityEventTargetMethods = new List<string>
        {
            nameof(Component.BroadcastMessage),
            nameof(Component.CompareTag),
            nameof(Component.GetComponent),
            nameof(Component.BroadcastMessage),
            nameof(Component.CompareTag),
            nameof(Component.GetComponent),
            nameof(Component.GetComponentInChildren),
            nameof(Component.GetComponentInParent),
            nameof(Component.GetComponents),
            nameof(Component.GetComponentsInChildren),
            nameof(Component.GetComponentsInParent),
            nameof(Component.SendMessage),
            nameof(Component.SendMessageUpwards),
            nameof(Component.GetInstanceID),
            nameof(Component.ToString),
            nameof(Component.Destroy),
            nameof(Component.DestroyImmediate),
            nameof(UnityEngine.Object.DontDestroyOnLoad),
            nameof(Component.FindObjectOfType),
            nameof(Component.FindObjectsOfType),
            nameof(Component.Instantiate),
            nameof(MonoBehaviour.CancelInvoke),
            nameof(MonoBehaviour.Invoke),
            nameof(MonoBehaviour.InvokeRepeating),
            nameof(MonoBehaviour.IsInvoking),
            nameof(MonoBehaviour.StartCoroutine),
            nameof(MonoBehaviour.StopAllCoroutines),
            nameof(MonoBehaviour.StopCoroutine),

            // Unity Magic Methods
            "FixedUpdate",
            "LateUpdate",
            "OnAnimatorIK",
            "OnAnimatorMove",
            "OnApplicationFocus",
            "OnApplicationPause",
            "OnApplicationQuit",
            "OnAudioFilterRead",
            "OnBecameInvisible",
            "OnBecameVisible",
            "OnCollisionEnter",
            "OnCollisionEnter2D",
            "OnCollisionExit",
            "OnCollisionExit2D",
            "OnCollisionStay",
            "OnCollisionStay2D",
            "OnConnectedToServer",
            "OnControllerColliderHit",
            "OnDisconnectedFromServer",
            "OnDrawGizmos",
            "OnDrawGizmosSelected",
            "OnFailedToConnect",
            "OnFailedToConnectToMasterServer",
            "OnGUI",
            "OnJointBreak",
            "OnJointBreak2D",
            "OnMasterServerEvent",
            "OnMouseDown",
            "OnMouseDrag",
            "OnMouseEnter",
            "OnMouseExit",
            "OnMouseOver",
            "OnMouseUp",
            "OnMouseUpAsButton",
            "OnNetworkInstantiate",
            "OnParticleCollision",
            "OnParticleSystemStopped",
            "OnParticleTrigger",
            "OnPlayerConnected",
            "OnPlayerDisconnected",
            "OnPostRender",
            "OnPreCull",
            "OnPreRender",
            "OnRenderImage",
            "OnRenderObject",
            "OnSerializeNetworkView",
            "OnServerInitialized",
            "OnTransformChildrenChanged",
            "OnTransformParentChanged",
            "OnTriggerEnter",
            "OnTriggerEnter2D",
            "OnTriggerExit",
            "OnTriggerExit2D",
            "OnTriggerStay",
            "OnTriggerStay2D",
            "OnWillRenderObject",
            "Update",

            // UI Behaviour Methods
            nameof(UIBehaviour.IsActive),
            nameof(UIBehaviour.IsDestroyed),

            // Protected UIBehaviour Methods
            "OnCanvasGroupChanged",
            "UIBehaviour.OnCanvasHierarchyChanged",
            "OnDidApplyAnimationProperties",
            "OnRectTransformDimensionsChange",
        };

        private static readonly List<string> _defaultDeniedUnityEventTargetProperties = new List<string>
        {
            nameof(MonoBehaviour.isActiveAndEnabled),
            nameof(Component.gameObject),
            nameof(Component.tag),
            nameof(Component.transform),
            nameof(MonoBehaviour.useGUILayout),
            nameof(UnityEngine.Object.hideFlags),
        };

        private static readonly Dictionary<Type, UnityEventTargetMethodAccessFilter> _initialTargetAccessFilters = new Dictionary<Type, UnityEventTargetMethodAccessFilter>
        {
            // {
            //     typeof(GameObject), new AllowedMethodFilter(
            //         new List<string>
            //         {
            //             nameof(GameObject.SetActive)
            //         },
            //         new List<string>())
            // },
            // {
            //     typeof(Transform), new AllowedMethodFilter(
            //         new List<string>
            //         {
            //             nameof(Transform.LookAt),
            //             nameof(Transform.SetAsFirstSibling),
            //             nameof(Transform.SetAsLastSibling),
            //             nameof(Transform.SetParent),
            //             nameof(Transform.SetSiblingIndex)
            //         },
            //         new List<string>
            //         {
            //             nameof(Transform.parent),
            //         })
            // },
            // {
            //     typeof(RectTransform), new AllowedMethodFilter(
            //         new List<string>
            //         {
            //             nameof(RectTransform.SetAsFirstSibling),
            //             nameof(RectTransform.SetAsLastSibling),
            //             nameof(RectTransform.SetParent),
            //             nameof(RectTransform.SetSiblingIndex)
            //         },
            //         new List<string>
            //         {
            //             nameof(RectTransform.parent)
            //         })
            // },
            // {
            //     typeof(AudioSource),
            //     new AllowedMethodFilter(
            //         new List<string>
            //         {
            //             nameof(AudioSource.Pause),
            //             nameof(AudioSource.Play),
            //             nameof(AudioSource.PlayDelayed),
            //             nameof(AudioSource.PlayOneShot),
            //             nameof(AudioSource.Stop),
            //             nameof(AudioSource.UnPause)
            //         },
            //         new List<string>
            //         {
            //             nameof(AudioSource.bypassEffects),
            //             nameof(AudioSource.bypassListenerEffects),
            //             nameof(AudioSource.bypassReverbZones),
            //             nameof(AudioSource.clip),
            //             nameof(AudioSource.dopplerLevel),
            //             nameof(AudioSource.enabled),
            //             nameof(AudioSource.loop),
            //             nameof(AudioSource.maxDistance),
            //             nameof(AudioSource.rolloffMode),
            //             nameof(AudioSource.minDistance),
            //             nameof(AudioSource.mute),
            //             nameof(AudioSource.pitch),
            //             nameof(AudioSource.playOnAwake),
            //             nameof(AudioSource.priority),
            //             nameof(AudioSource.spatialize),
            //             nameof(AudioSource.spread),
            //             nameof(AudioSource.time),
            //             nameof(AudioSource.volume)
            //         }
            //     )
            // },
            // {
            //     typeof(AudioDistortionFilter), new AllowedMethodFilter(
            //         new List<string>(),
            //         new List<string>
            //         {
            //             nameof(AudioDistortionFilter.distortionLevel),
            //             nameof(AudioDistortionFilter.enabled)
            //         })
            // },
            // {
            //     typeof(AudioEchoFilter), new AllowedMethodFilter(
            //         new List<string>(),
            //         new List<string>
            //         {
            //             nameof(AudioEchoFilter.decayRatio),
            //             nameof(AudioEchoFilter.delay),
            //             nameof(AudioEchoFilter.dryMix),
            //             nameof(AudioEchoFilter.enabled),
            //             nameof(AudioEchoFilter.wetMix)
            //         })
            // },
            // {
            //     typeof(AudioHighPassFilter), new AllowedMethodFilter(
            //         new List<string>(),
            //         new List<string>
            //         {
            //             nameof(AudioHighPassFilter.cutoffFrequency),
            //             nameof(AudioHighPassFilter.enabled),
            //             nameof(AudioHighPassFilter.highpassResonanceQ)
            //         })
            // },
            // {
            //     typeof(AudioLowPassFilter), new AllowedMethodFilter(
            //         new List<string>(),
            //         new List<string>
            //         {
            //             nameof(AudioLowPassFilter.cutoffFrequency),
            //             nameof(AudioLowPassFilter.enabled),
            //             nameof(AudioLowPassFilter.lowpassResonanceQ)
            //         })
            // },
            // {
            //     typeof(AudioReverbFilter), new AllowedMethodFilter(
            //         new List<string>(),
            //         new List<string>
            //         {
            //             nameof(AudioReverbFilter.decayHFRatio),
            //             nameof(AudioReverbFilter.decayTime),
            //             nameof(AudioReverbFilter.density),
            //             nameof(AudioReverbFilter.diffusion),
            //             nameof(AudioReverbFilter.dryLevel),
            //             nameof(AudioReverbFilter.enabled),
            //             nameof(AudioReverbFilter.hfReference),
            //             nameof(AudioReverbFilter.reflectionsDelay),
            //             nameof(AudioReverbFilter.reflectionsLevel),
            //             nameof(AudioReverbFilter.reverbDelay),
            //             nameof(AudioReverbFilter.reverbLevel),
            //             nameof(AudioReverbFilter.room),
            //             nameof(AudioReverbFilter.roomHF),
            //             nameof(AudioReverbFilter.roomLF)
            //         })
            // },
            // {
            //     typeof(AudioReverbZone), new AllowedMethodFilter(
            //         new List<string>(),
            //         new List<string>
            //         {
            //             nameof(AudioReverbZone.decayHFRatio),
            //             nameof(AudioReverbZone.decayTime),
            //             nameof(AudioReverbZone.density),
            //             nameof(AudioReverbZone.diffusion),
            //             nameof(AudioReverbZone.enabled),
            //             nameof(AudioReverbZone.HFReference),
            //             nameof(AudioReverbZone.LFReference),
            //             nameof(AudioReverbZone.maxDistance),
            //             nameof(AudioReverbZone.minDistance),
            //             nameof(AudioReverbZone.reflections),
            //             nameof(AudioReverbZone.reflectionsDelay),
            //             nameof(AudioReverbZone.room),
            //             nameof(AudioReverbZone.roomHF),
            //             nameof(AudioReverbZone.roomLF)
            //         })
            // },
            // {
            //     typeof(UIBehaviour), new DeniedMethodFilter(_defaultDeniedUnityEventTargetMethods, _defaultDeniedUnityEventTargetProperties)
            // },
            #if UDON
            {
                typeof(UdonBehaviour), new AllowedMethodFilter(
                    new List<string>
                    {
                        nameof(UdonBehaviour.RunProgram),
                        nameof(UdonBehaviour.SendCustomEvent),
                        nameof(UdonBehaviour.Interact)
                    },
                    new List<string>())
            },
            #endif
                // {
                //     typeof(MeshRenderer), new AllowedMethodFilter(
                //         new List<string>(),
                //         new List<string>
                //         {
                //             nameof(MeshRenderer.shadowCastingMode),
                //             nameof(MeshRenderer.enabled),
                //             nameof(MeshRenderer.probeAnchor),
                //             nameof(MeshRenderer.material),
                //             nameof(MeshRenderer.probeAnchor),
                //             nameof(MeshRenderer.receiveShadows),
                //             nameof(MeshRenderer.sharedMaterial),
                //             nameof(MeshRenderer.lightProbeUsage)
                //         })
                // },
                // {
                //     typeof(Collider), new AllowedMethodFilter(
                //         new List<string>(),
                //         new List<string>
                //         {
                //             nameof(Collider.enabled),
                //             nameof(Collider.isTrigger),
                //             nameof(Collider.material)
                //         })
                // },
                // {
                //     typeof(SkinnedMeshRenderer), new AllowedMethodFilter(
                //         new List<string>
                //         {
                //             nameof(SkinnedMeshRenderer.BakeMesh)
                //         },
                //         new List<string>
                //         {
                //             nameof(SkinnedMeshRenderer.allowOcclusionWhenDynamic),
                //             nameof(SkinnedMeshRenderer.shadowCastingMode),
                //             nameof(SkinnedMeshRenderer.enabled),
                //             nameof(SkinnedMeshRenderer.lightProbeProxyVolumeOverride),
                //             nameof(SkinnedMeshRenderer.material),
                //             nameof(SkinnedMeshRenderer.motionVectorGenerationMode),
                //             nameof(SkinnedMeshRenderer.probeAnchor),
                //             nameof(SkinnedMeshRenderer.receiveShadows),
                //             nameof(SkinnedMeshRenderer.rootBone),
                //             nameof(SkinnedMeshRenderer.sharedMaterial),
                //             nameof(SkinnedMeshRenderer.sharedMesh),
                //             nameof(SkinnedMeshRenderer.skinnedMotionVectors),
                //             nameof(SkinnedMeshRenderer.updateWhenOffscreen),
                //             nameof(SkinnedMeshRenderer.lightProbeUsage)
                //         })
                // },
                // {
                //     typeof(Light), new AllowedMethodFilter(
                //         new List<string>
                //         {
                //             nameof(Light.Reset)
                //         },
                //         new List<string>
                //         {
                //             nameof(Light.bounceIntensity),
                //             nameof(Light.colorTemperature),
                //             nameof(Light.cookie),
                //             nameof(Light.enabled),
                //             nameof(Light.intensity),
                //             nameof(Light.range),
                //             nameof(Light.shadowBias),
                //             nameof(Light.shadowNearPlane),
                //             nameof(Light.shadowNormalBias),
                //             nameof(Light.shadowStrength),
                //             nameof(Light.spotAngle)
                //         })
                // },
                // {
                //     typeof(ParticleSystem), new AllowedMethodFilter(
                //         new List<string>
                //         {
                //             nameof(ParticleSystem.Clear),
                //             nameof(ParticleSystem.Emit),
                //             nameof(ParticleSystem.Pause),
                //             nameof(ParticleSystem.Pause),
                //             nameof(ParticleSystem.Play),
                //             nameof(ParticleSystem.Simulate),
                //             nameof(ParticleSystem.Stop),
                //             nameof(ParticleSystem.Stop),
                //             nameof(ParticleSystem.TriggerSubEmitter)
                //         },
                //         new List<string>
                //         {
                //             nameof(ParticleSystem.time),
                //             nameof(ParticleSystem.useAutoRandomSeed)
                //         })
                // },
                // {
                //     typeof(ParticleSystemForceField), new AllowedMethodFilter(
                //         new List<string>(),
                //         new List<string>
                //         {
                //             nameof(ParticleSystemForceField.endRange),
                //             nameof(ParticleSystemForceField.gravityFocus),
                //             nameof(ParticleSystemForceField.length),
                //             nameof(ParticleSystemForceField.multiplyDragByParticleSize),
                //             nameof(ParticleSystemForceField.multiplyDragByParticleVelocity),
                //             nameof(ParticleSystemForceField.startRange),
                //             nameof(ParticleSystemForceField.vectorField)
                //         })
                // },
                // {
                //     typeof(ReflectionProbe), new AllowedMethodFilter(
                //         new List<string>
                //         {
                //             nameof(ReflectionProbe.Reset)
                //         },
                //         new List<string>
                //         {
                //             nameof(ReflectionProbe.blendDistance),
                //             nameof(ReflectionProbe.boxProjection),
                //             nameof(ReflectionProbe.customBakedTexture),
                //             nameof(ReflectionProbe.enabled),
                //             nameof(ReflectionProbe.farClipPlane),
                //             nameof(ReflectionProbe.hdr),
                //             nameof(ReflectionProbe.importance),
                //             nameof(ReflectionProbe.intensity),
                //             nameof(ReflectionProbe.nearClipPlane),
                //             nameof(ReflectionProbe.realtimeTexture),
                //             nameof(ReflectionProbe.resolution),
                //             nameof(ReflectionProbe.shadowDistance)
                //         })
                // },
                // {
                //     typeof(Projector), new AllowedMethodFilter(
                //         new List<string>(),
                //         new List<string>
                //         {
                //             nameof(Projector.aspectRatio),
                //             nameof(Projector.enabled),
                //             nameof(Projector.nearClipPlane),
                //             nameof(Projector.farClipPlane),
                //             nameof(Projector.fieldOfView),
                //             nameof(Projector.orthographic),
                //             nameof(Projector.orthographicSize),
                //             nameof(Projector.material)
                //         })
                // },
                // {
                //     typeof(LineRenderer), new AllowedMethodFilter(
                //         new List<string>(),
                //         new List<string>
                //         {
                //             nameof(LineRenderer.allowOcclusionWhenDynamic),
                //             nameof(LineRenderer.shadowCastingMode),
                //             nameof(LineRenderer.enabled),
                //             nameof(LineRenderer.endWidth),
                //             nameof(LineRenderer.loop),
                //             nameof(LineRenderer.material),
                //             nameof(LineRenderer.motionVectorGenerationMode),
                //             nameof(LineRenderer.numCapVertices),
                //             nameof(LineRenderer.numCornerVertices),
                //             nameof(LineRenderer.probeAnchor),
                //             nameof(LineRenderer.receiveShadows),
                //             nameof(LineRenderer.shadowBias),
                //             nameof(LineRenderer.sharedMaterial),
                //             nameof(LineRenderer.startWidth),
                //             nameof(LineRenderer.lightProbeUsage),
                //             nameof(LineRenderer.useWorldSpace),
                //             nameof(LineRenderer.widthMultiplier)
                //         })
                // },
                // {
                //     typeof(TrailRenderer), new AllowedMethodFilter(
                //         new List<string>
                //         {
                //             nameof(TrailRenderer.Clear)
                //         },
                //         new List<string>
                //         {
                //             nameof(TrailRenderer.allowOcclusionWhenDynamic),
                //             nameof(TrailRenderer.autodestruct),
                //             nameof(TrailRenderer.shadowCastingMode),
                //             nameof(TrailRenderer.enabled),
                //             nameof(TrailRenderer.emitting),
                //             nameof(TrailRenderer.endWidth),
                //             nameof(TrailRenderer.material),
                //             nameof(TrailRenderer.motionVectorGenerationMode),
                //             nameof(TrailRenderer.numCapVertices),
                //             nameof(TrailRenderer.numCornerVertices),
                //             nameof(TrailRenderer.probeAnchor),
                //             nameof(TrailRenderer.receiveShadows),
                //             nameof(TrailRenderer.shadowBias),
                //             nameof(TrailRenderer.sharedMaterial),
                //             nameof(TrailRenderer.startWidth),
                //             nameof(TrailRenderer.lightProbeUsage),
                //             nameof(TrailRenderer.widthMultiplier)
                //         })
                // },
                // {
                //     typeof(Animator), new AllowedMethodFilter(
                //         new List<string>
                //         {
                //             nameof(Animator.Play),
                //             nameof(Animator.PlayInFixedTime),
                //             nameof(Animator.Rebind),
                //             nameof(Animator.SetBool),
                //             nameof(Animator.SetFloat),
                //             nameof(Animator.SetInteger),
                //             nameof(Animator.SetTrigger),
                //             nameof(Animator.ResetTrigger)
                //         },
                //         new List<string>
                //         {
                //             nameof(Animator.speed)
                //         })
                // }
        };

        private abstract class UnityEventTargetMethodAccessFilter
        {
            [PublicAPI]
            public abstract bool IsTargetMethodAllowed(string targetMethodName);

            [PublicAPI]
            public abstract List<string> GetTargetMethodNames();
        }

        private sealed class AllowedMethodFilter : UnityEventTargetMethodAccessFilter
        {
            private readonly HashSet<string> _allowedTargets;

            [PublicAPI]
            public AllowedMethodFilter(List<string> allowedTargetMethodNames, List<string> allowedTargetPropertyNames)
            {
                _allowedTargets = new HashSet<string>();
                _allowedTargets.UnionWith(allowedTargetMethodNames);
                foreach(string allowedTargetProperty in allowedTargetPropertyNames)
                {
                    _allowedTargets.Add($"get_{allowedTargetProperty}");
                    _allowedTargets.Add($"set_{allowedTargetProperty}");
                }
            }

            public override bool IsTargetMethodAllowed(string targetMethodName)
            {
                return _allowedTargets.Contains(targetMethodName);
            }

            public override List<string> GetTargetMethodNames()
            {
                return _allowedTargets.ToList();
            }
        }

        private sealed class DeniedMethodFilter : UnityEventTargetMethodAccessFilter
        {
            private readonly HashSet<string> _deniedTargets;

            [PublicAPI]
            public DeniedMethodFilter(List<string> deniedTargetMethodsNames, List<string> deniedTargetPropertyNames)
            {
                _deniedTargets = new HashSet<string>();
                _deniedTargets.UnionWith(_deniedTargets);
                foreach(string deniedTargetProperty in deniedTargetPropertyNames)
                {
                    _deniedTargets.Add($"get_{deniedTargetProperty}");
                    _deniedTargets.Add($"set_{deniedTargetProperty}");
                }
            }

            public override bool IsTargetMethodAllowed(string targetMethodName)
            {
                return !_deniedTargets.Contains(targetMethodName);
            }

            public override List<string> GetTargetMethodNames()
            {
                return _deniedTargets.ToList();
            }
        }
    }
}

