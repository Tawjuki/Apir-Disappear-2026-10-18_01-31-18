using System;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.TaguTaguan
{
    /// <summary>
    /// Identifies the transform that the local Tagu-Taguan camera may follow.
    /// Ownership is evaluated locally and is never sent over the network.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class TaguTaguanCameraTarget : NetworkBehaviour
    {
        [SerializeField] private Transform followPoint;
        [SerializeField] private bool allowOfflinePreview;

        public static event Action<TaguTaguanCameraTarget> LocalTargetAvailable;
        public static event Action<TaguTaguanCameraTarget> LocalTargetUnavailable;

        public Transform FollowPoint => followPoint != null ? followPoint : transform;

        public bool IsLocalTarget
        {
            get
            {
                if (IsSpawned)
                {
                    return IsOwner;
                }

                NetworkManager manager = NetworkManager.Singleton;
                return allowOfflinePreview && (manager == null || !manager.IsListening);
            }
        }

        private void Start()
        {
            if (IsLocalTarget)
            {
                LocalTargetAvailable?.Invoke(this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsOwner)
            {
                LocalTargetAvailable?.Invoke(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                LocalTargetUnavailable?.Invoke(this);
            }

            base.OnNetworkDespawn();
        }

        public override void OnGainedOwnership()
        {
            base.OnGainedOwnership();
            LocalTargetAvailable?.Invoke(this);
        }

        public override void OnLostOwnership()
        {
            LocalTargetUnavailable?.Invoke(this);
            base.OnLostOwnership();
        }

        public override void OnDestroy()
        {
            if (IsLocalTarget)
            {
                LocalTargetUnavailable?.Invoke(this);
            }

            base.OnDestroy();
        }
    }
}
