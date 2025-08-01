#if INCLUDE_UNITY_XR_HANDS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.ProviderImplementation;

namespace UnityEngine.XR.VisionOS
{
    using SessionProvider = VisionOSSessionSubsystem.VisionOSSessionProvider;

    class VisionOSHandProvider : XRHandSubsystemProvider, IVisionOSProvider
    {
        // TODO: Add HandTracking feature to future versions of AR Foundation
        // Use Body3D as a proxy for now
        const Feature k_HandFeatureProxy = Feature.Body3D;
        internal const string handSubsystemId = "VisionOS-Hands";
        static readonly Quaternion k_LeftHandAlignment = Quaternion.AngleAxis(-90f, Vector3.up) * Quaternion.AngleAxis(180f, Vector3.right);
        static readonly Quaternion k_RightHandAlignment = Quaternion.AngleAxis(-90f, Vector3.up);

        XRHandSubsystem.UpdateSuccessFlags m_LastSuccessFlags = XRHandSubsystem.UpdateSuccessFlags.None;

        public AR_Authorization_Type RequiredAuthorizationType => NativeApi.HandTracking.ar_hand_tracking_provider_get_required_authorization_type();
        public bool IsSupported => NativeApi.HandTracking.ar_hand_tracking_provider_is_supported();
        public bool ShouldBeActive => running;
        public IntPtr CurrentProvider { get; private set; } = IntPtr.Zero;

        IntPtr m_LeftHandAnchor = IntPtr.Zero;
        IntPtr m_RightHandAnchor = IntPtr.Zero;

        readonly Dictionary<Handedness, bool> m_HandTrackingStates = new();

        IntPtr m_ARSession = IntPtr.Zero;
        AR_Data_Provider_State m_NativeProviderState = AR_Data_Provider_State.Stopped;

        public override void Start()
        {
            VisionOSProviderRegistration.RegisterProvider(k_HandFeatureProxy, this);
        }

        public override void Stop()
        {
            // Do not call TryStopNativeSession in Subsystem Stop callback. This will be handled by SessionSubsystem
            VisionOSProviderRegistration.UnregisterProvider(k_HandFeatureProxy, this);
        }

        public override void Destroy()
        {
            // Try to stop the native session in case TryStop hasn't been called yet.
            if (!TryStopNativeSession())
                ResetNativePointers(); // Clear things out in case TryStopNativeSession didn't do its job previously
        }

        void ResetNativePointers()
        {
            m_ARSession = IntPtr.Zero;
            CurrentProvider = IntPtr.Zero;
            m_LeftHandAnchor = IntPtr.Zero;
            m_RightHandAnchor = IntPtr.Zero;
        }

        public bool TryStartNativeSession(Feature features)
        {
            if (!IsSupported)
            {
                Debug.LogWarning("Hand tracking provider is not supported");
                return false;
            }

            // Early-out if provider is already running
            if (m_ARSession != IntPtr.Zero)
                return true;

            var handTrackingConfiguration = NativeApi.HandTracking.ar_hand_tracking_configuration_create();
            CurrentProvider = NativeApi.HandTracking.ar_hand_tracking_provider_create(handTrackingConfiguration);
            if (CurrentProvider == IntPtr.Zero)
            {
                Debug.LogWarning("Failed to create hand tracking provider.");
                return false;
            }

            Debug.Log("Starting hand tracking provider.");
            m_ARSession = SessionProvider.StartProviderSession(CurrentProvider);
            return true;
        }

        public bool TryStopNativeSession()
        {
            // Early-out if provider has not been started
            if (m_ARSession == IntPtr.Zero)
                return false;

            Debug.Log("Stopping hand tracking provider.");
            NativeApi.Session.ar_session_stop(m_ARSession);

            ResetNativePointers();
            return true;
        }

        public void SetNativeProviderState(AR_Data_Provider_State newState)
        {
            m_NativeProviderState = newState;
        }

        public override void GetHandLayout(NativeArray<bool> handJointsInLayout)
        {
            // All joints except palm are supported
            for (var i = 0; i < handJointsInLayout.Length; i++)
            {
                handJointsInLayout[i] = (XRHandJointID)i != XRHandJointID.Palm;
            }
        }

        /// <inheritdoc/>
        public override XRHandSubsystem.UpdateSuccessFlags TryUpdateHands(
            XRHandSubsystem.UpdateType updateType,
            ref Pose leftHandRootPose,
            NativeArray<XRHandJoint> leftHandJoints,
            ref Pose rightHandRootPose,
            NativeArray<XRHandJoint> rightHandJoints)
        {
            if (CurrentProvider == IntPtr.Zero)
                return XRHandSubsystem.UpdateSuccessFlags.None;

            if (m_NativeProviderState != AR_Data_Provider_State.Running)
                return XRHandSubsystem.UpdateSuccessFlags.None;

            if (m_LeftHandAnchor == IntPtr.Zero)
                m_LeftHandAnchor = NativeApi.HandTracking.ar_hand_anchor_create();

            if (m_RightHandAnchor == IntPtr.Zero)
                m_RightHandAnchor = NativeApi.HandTracking.ar_hand_anchor_create();

            // TODO: How do we handle backward compatibility with visionOS 1.0?
            var timestamp = NativeApi.HandTracking.GetLatestHandTrackingTiming();
            var status = NativeApi.HandTracking.ar_hand_tracking_provider_query_anchors_at_timestamp(CurrentProvider, timestamp, m_LeftHandAnchor, m_RightHandAnchor);
            if (status == AR_Hand_Anchor_Query_Status.Failure)
                return XRHandSubsystem.UpdateSuccessFlags.None;

            m_LastSuccessFlags = XRHandSubsystem.UpdateSuccessFlags.None;
            GetHandData(ref leftHandRootPose, ref m_LastSuccessFlags, leftHandJoints, m_LeftHandAnchor, Handedness.Left);
            GetHandData(ref rightHandRootPose, ref m_LastSuccessFlags, rightHandJoints, m_RightHandAnchor, Handedness.Right);
            return m_LastSuccessFlags;
        }

        void GetHandData(ref Pose rootPose, ref XRHandSubsystem.UpdateSuccessFlags successFlags, NativeArray<XRHandJoint> jointArray, IntPtr handAnchor, Handedness handedness)
        {
            var isTracked = NativeApi.Anchor.ar_trackable_anchor_is_tracked(handAnchor);
            if (!isTracked)
            {
                // If TryGetValue returns false, that means we never tracked this hand. `wasTracked` will be false, which is the correct behavior
                m_HandTrackingStates.TryGetValue(handedness, out var wasTracked);
                m_HandTrackingStates[handedness] = false;
                if (wasTracked)
                    ClearHandTrackingStates(jointArray, handedness);

                return;
            }

            m_HandTrackingStates[handedness] = true;

            var worldTransform = NativeApi.Anchor.ar_anchor_get_origin_from_anchor_transform(handAnchor);
            var convertedMatrix = NativeApi_Types.UnityVisionOS_impl_simd_float4x4_to_float_array(worldTransform);
            var worldMatrix = Marshal.PtrToStructure<FloatArrayToMatrix4x4>(convertedMatrix);
            var wristPosition = worldMatrix.GetPosition();
            var wristRotation = worldMatrix.GetRotation();

            rootPose = new Pose(wristPosition, AlignRotation(wristRotation, handedness));
            successFlags |= handedness == Handedness.Left
                ? XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose
                : XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose;

            var handSkeleton = NativeApi.HandTracking.ar_hand_anchor_get_hand_skeleton(handAnchor);
            var endID = (XRHandJointID)((int)XRHandJointID.EndMarker + VisionOSHandExtensions.NumVisionOSJoints);
            for (var jointID = XRHandJointID.BeginMarker; jointID < endID; jointID++)
            {
                var index = jointID.ToIndex();
                var pose = Pose.identity;
                XRHandJointTrackingState trackingState;
                // use wrist Pose for palm pose
                if (jointID == XRHandJointID.Palm)
                {
                    trackingState = XRHandJointTrackingState.Pose;
                    jointArray[index] = CreateJoint(handedness, trackingState, jointID, pose);
                    var appleTrackingStates = VisionOSHandExtensions.GetVisionOSTrackingStates(handedness);
                    appleTrackingStates[index] = true;
                    var appleRotations = VisionOSHandExtensions.GetVisionOSRotations(handedness);
                    appleRotations[index] = wristRotation;                    
                    pose = rootPose;
                }
                else
                {
                    var jointName = GetJointNameForJointID(jointID);
                    var joint = NativeApi.HandSkeleton.ar_hand_skeleton_get_joint_named(handSkeleton, jointName);
                    var jointIsTracked = NativeApi.SkeletonJoint.ar_skeleton_joint_is_tracked(joint);
                    var appleTrackingStates = VisionOSHandExtensions.GetVisionOSTrackingStates(handedness);
                    appleTrackingStates[index] = jointIsTracked;

                    // Always report pose is tracked as long as the hand anchor is tracked. Estimated poses are provided for
                    // joints hidden from view, even though jointIsTracked will be false
                    trackingState = XRHandJointTrackingState.Pose;

                    var jointTransformPtr =
                        NativeApi.SkeletonJoint.ar_skeleton_joint_get_anchor_from_joint_transform(joint);
                    convertedMatrix =
                        NativeApi_Types.UnityVisionOS_impl_simd_float4x4_to_float_array(jointTransformPtr);
                    var jointMatrix = Marshal.PtrToStructure<FloatArrayToMatrix4x4>(convertedMatrix);
                    var jointPosition = wristPosition + wristRotation * jointMatrix.GetPosition();
                    var jointRotation = wristRotation * jointMatrix.GetRotation();
                    pose = new Pose(jointPosition, AlignRotation(jointRotation, handedness));

                    var appleRotations = VisionOSHandExtensions.GetVisionOSRotations(handedness);
                    appleRotations[index] = jointRotation;
                }

                successFlags |= handedness == Handedness.Left
                    ? XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints
                    : XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;

                var createdJoint = CreateJoint(handedness, trackingState, jointID, pose);
                if (jointID < XRHandJointID.EndMarker)
                {
                    jointArray[index] = createdJoint;
                }
                else
                {
                    var visionOSHand = handedness == Handedness.Left
                        ? VisionOSHandExtensions.leftHand
                        : VisionOSHandExtensions.rightHand;
                    visionOSHand.SetJoint(createdJoint);
                }
            }
        }

        static void ClearHandTrackingStates(NativeArray<XRHandJoint> jointArray, Handedness handedness)
        {
            var endID = (XRHandJointID)((int)XRHandJointID.EndMarker + VisionOSHandExtensions.NumVisionOSJoints);
            for (var jointID = XRHandJointID.BeginMarker; jointID < endID; jointID++)
            {
                var index = jointID.ToIndex();
                var trackingState = XRHandJointTrackingState.None;
                // if (jointID == XRHandJointID.Palm)
                //     trackingState = XRHandJointTrackingState.WillNeverBeValid;

                jointArray[index] = CreateJoint(handedness, trackingState, jointID, Pose.identity);
            }
        }

        static Quaternion AlignRotation(Quaternion rotation, Handedness handedness)
        {
            return handedness == Handedness.Left
                ? rotation * k_LeftHandAlignment
                : rotation * k_RightHandAlignment;
        }

        static XRHandJoint CreateJoint(Handedness handedness, XRHandJointTrackingState trackingState, XRHandJointID id, Pose pose)
        {
#if INCLUDE_UNITY_XR_HANDS_1_1
            return XRHandProviderUtility.CreateJoint(handedness, trackingState, id, pose);
#else
            return XRHandProviderUtility.CreateJoint(trackingState, id, pose);
#endif
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void Register()
        {
            var handsSubsystemCinfo = new XRHandSubsystemDescriptor.Cinfo
            {
                id = handSubsystemId,
                providerType = typeof(VisionOSHandProvider)
            };

            XRHandSubsystemDescriptor.Register(handsSubsystemCinfo);
        }

        static AR_Skeleton_Joint_Name GetJointNameForJointID(XRHandJointID jointID)
        {
            switch (jointID)
            {
                case XRHandJointID.Invalid:
                    throw new ArgumentException("Cannot map invalid joint ID to Joint Name");
                case XRHandJointID.Wrist:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_wrist;
                case XRHandJointID.Palm:
                    throw new ArgumentException("VisionOS does not support the palm joint");
                case XRHandJointID.ThumbMetacarpal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_thumb_knuckle;
                case XRHandJointID.ThumbProximal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_thumb_intermediate_base;
                case XRHandJointID.ThumbDistal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_thumb_intermediate_tip;
                case XRHandJointID.ThumbTip:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_thumb_tip;
                case XRHandJointID.IndexMetacarpal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_index_finger_metacarpal;
                case XRHandJointID.IndexProximal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_index_finger_knuckle;
                case XRHandJointID.IndexIntermediate:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_index_finger_intermediate_base;
                case XRHandJointID.IndexDistal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_index_finger_intermediate_tip;
                case XRHandJointID.IndexTip:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_index_finger_tip;
                case XRHandJointID.MiddleMetacarpal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_middle_finger_metacarpal;
                case XRHandJointID.MiddleProximal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_middle_finger_knuckle;
                case XRHandJointID.MiddleIntermediate:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_middle_finger_intermediate_base;
                case XRHandJointID.MiddleDistal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_middle_finger_intermediate_tip;
                case XRHandJointID.MiddleTip:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_middle_finger_tip;
                case XRHandJointID.RingMetacarpal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_ring_finger_metacarpal;
                case XRHandJointID.RingProximal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_ring_finger_knuckle;
                case XRHandJointID.RingIntermediate:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_ring_finger_intermediate_base;
                case XRHandJointID.RingDistal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_ring_finger_intermediate_tip;
                case XRHandJointID.RingTip:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_ring_finger_tip;
                case XRHandJointID.LittleMetacarpal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_little_finger_metacarpal;
                case XRHandJointID.LittleProximal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_little_finger_knuckle;
                case XRHandJointID.LittleIntermediate:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_little_finger_intermediate_base;
                case XRHandJointID.LittleDistal:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_little_finger_intermediate_tip;
                case XRHandJointID.LittleTip:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_little_finger_tip;
                case (XRHandJointID)VisionOSHandJointID.ForearmWrist:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_forearm_wrist;
                case (XRHandJointID)VisionOSHandJointID.ForearmArm:
                    return AR_Skeleton_Joint_Name.ar_hand_skeleton_joint_name_forearm_arm;
                default:
                    throw new ArgumentOutOfRangeException(nameof(jointID), jointID, null);
            }
        }
    }
}
#endif
