using System;
using Unity.Netcode;

namespace ApirApir.AugmentedReality
{
    public interface INetcodeARPlayerResolver
    {
        bool TryGetPlayerId(ulong clientId, out string persistentPlayerId);
    }

    /// <summary>
    /// Optional session transport for registry commands. The injected registry remains the
    /// authority and may be hosted independently from this temporary multiplayer session.
    /// Every RPC uses only Netcode-supported scalar values plus its RPC parameter structure.
    /// </summary>
    public sealed class ARCardNetworkAdapter : NetworkBehaviour
    {
        private const int MaximumIncomingIdLength = CardRegistrationService.MaximumIdentifierLength;
        private CardRegistrationService registrations;
        private INetcodeARPlayerResolver playerResolver;

        public event Action<string, ARCardOperationResult> RemoteOperationCompleted;

        public void Initialize(
            CardRegistrationService registrationService,
            INetcodeARPlayerResolver resolver)
        {
            registrations = registrationService ?? throw new ArgumentNullException(nameof(registrationService));
            playerResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RegisterCardServerRpc(
            string requestId,
            string assembledCardId,
            string trackingTargetId,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryResolveRequest(sender, requestId, out string playerId) ||
                !IsBounded(assembledCardId) || !IsOptionalBounded(trackingTargetId))
            {
                SendResult(sender, requestId, ARCardOperationResult.Rejected(ARCardFailure.InvalidRequest));
                return;
            }

            ARCardOperationResult result = registrations.Register(
                playerId,
                new ARCardRegistrationRequest(requestId, assembledCardId, trackingTargetId));
            SendResult(sender, requestId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RevokeCardServerRpc(
            string requestId,
            string cardId,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryResolveRequest(sender, requestId, out string playerId) || !IsBounded(cardId))
            {
                SendResult(sender, requestId, ARCardOperationResult.Rejected(ARCardFailure.InvalidRequest));
                return;
            }

            // The persistent player ID is resolved from the authenticated connection. No owner
            // ID or administrator flag is accepted from the client.
            SendResult(sender, requestId, registrations.Revoke(playerId, cardId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void GetCardServerRpc(
            string requestId,
            string cardId,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryResolveRequest(sender, requestId, out _) || !IsBounded(cardId))
            {
                SendResult(sender, requestId, ARCardOperationResult.Rejected(ARCardFailure.InvalidRequest));
                return;
            }

            // Viewing another player's card is permitted, but validation never changes ownership.
            SendResult(sender, requestId, registrations.Validate(cardId));
        }

        private bool TryResolveRequest(ulong sender, string requestId, out string playerId)
        {
            playerId = string.Empty;
            return IsServer && registrations != null && playerResolver != null &&
                   IsBounded(requestId) &&
                   playerResolver.TryGetPlayerId(sender, out playerId) &&
                   IsBounded(playerId);
        }

        private void SendResult(ulong clientId, string requestId, ARCardOperationResult result)
        {
            RegisteredARCard card = result == null ? null : result.Card;
            ARCardConfigurationSnapshot configuration = card == null ? null : card.Configuration;
            RpcParams rpcParams = RpcTarget.Single(clientId, RpcTargetUse.Temp);

            PublishCardResultClientRpc(
                requestId ?? string.Empty,
                result != null && result.Succeeded,
                (byte)(result == null ? ARCardFailure.RegistryUnavailable : result.Failure),
                card == null ? string.Empty : card.CardId,
                card == null ? string.Empty : card.OwnerPlayerId,
                card == null ? string.Empty : card.OwnerPlayerTag,
                card == null ? 0 : card.CardNumber,
                card == null ? string.Empty : card.SourceAssembledCardId,
                card == null ? (byte)0 : (byte)card.Status,
                card == null ? string.Empty : card.TrackingTargetId,
                card == null ? 0L : card.RegisteredUnixMilliseconds,
                card == null ? 0L : card.RevokedUnixMilliseconds,
                configuration == null ? string.Empty : configuration.HeadDefinitionId,
                configuration == null ? string.Empty : configuration.HeadVisualAssetId,
                configuration == null ? string.Empty : configuration.BodyDefinitionId,
                configuration == null ? string.Empty : configuration.BodyVisualAssetId,
                configuration == null ? string.Empty : configuration.LegsDefinitionId,
                configuration == null ? string.Empty : configuration.LegsVisualAssetId,
                configuration == null ? string.Empty : configuration.CosmeticId,
                rpcParams);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PublishCardResultClientRpc(
            string requestId,
            bool succeeded,
            byte failure,
            string cardId,
            string ownerPlayerId,
            string ownerPlayerTag,
            int cardNumber,
            string sourceAssembledCardId,
            byte status,
            string trackingTargetId,
            long registeredUnixMilliseconds,
            long revokedUnixMilliseconds,
            string headDefinitionId,
            string headVisualAssetId,
            string bodyDefinitionId,
            string bodyVisualAssetId,
            string legsDefinitionId,
            string legsVisualAssetId,
            string cosmeticId,
            RpcParams rpcParams = default)
        {
            if (!IsBounded(requestId) || requestId.Length == 0)
            {
                return;
            }

            RegisteredARCard card = null;
            if (IsBounded(cardId) && cardId.Length > 0 && IsBounded(ownerPlayerId) &&
                IsBounded(sourceAssembledCardId) && IsOptionalBounded(trackingTargetId) &&
                ownerPlayerTag != null && ownerPlayerTag.Length <= CardRegistrationService.MaximumPlayerTagLength &&
                cardNumber > 0 && Enum.IsDefined(typeof(ARCardStatus), status))
            {
                var configuration = new ARCardConfigurationSnapshot(
                    headDefinitionId, headVisualAssetId,
                    bodyDefinitionId, bodyVisualAssetId,
                    legsDefinitionId, legsVisualAssetId,
                    cosmeticId);
                if (configuration.HasAnyPart && configuration.HasConsistentVisualReferences &&
                    configuration.IsWithinIdentifierLength(MaximumIncomingIdLength))
                {
                    card = new RegisteredARCard(
                        cardId, ownerPlayerId, ownerPlayerTag, cardNumber,
                        sourceAssembledCardId, configuration, (ARCardStatus)status,
                        trackingTargetId, registeredUnixMilliseconds, revokedUnixMilliseconds);
                }
            }

            ARCardFailure parsedFailure = Enum.IsDefined(typeof(ARCardFailure), failure)
                ? (ARCardFailure)failure
                : ARCardFailure.InvalidRequest;
            ARCardOperationResult result = succeeded && card != null
                ? ARCardOperationResult.Success(card)
                : ARCardOperationResult.Rejected(
                    succeeded ? ARCardFailure.InvalidConfiguration : parsedFailure, card);
            RemoteOperationCompleted?.Invoke(requestId, result);
        }

        private static bool IsBounded(string value)
        {
            return value != null && value.Trim().Length > 0 && value.Length <= MaximumIncomingIdLength;
        }

        private static bool IsOptionalBounded(string value)
        {
            return value == null || value.Length <= MaximumIncomingIdLength;
        }
    }
}
