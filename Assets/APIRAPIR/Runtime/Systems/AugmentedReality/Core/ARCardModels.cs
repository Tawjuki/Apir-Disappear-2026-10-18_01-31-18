using System;

namespace ApirApir.AugmentedReality
{
    public enum ARCardStatus : byte
    {
        Active = 1,
        Revoked = 2
    }

    public enum ARCardFailure : byte
    {
        None,
        InvalidRequest,
        OwnerNotFound,
        PlayerTagNotFound,
        AssembledCardNotFound,
        InvalidConfiguration,
        ActiveCardLimitReached,
        CardNotFound,
        CardRevoked,
        Unauthorized,
        DuplicateCardId,
        RegistryUnavailable,
        AlreadyRevoked
    }

    /// <summary>
    /// Immutable identifiers for the assembled appearance. These are content references,
    /// not combat state and not serialized artwork.
    /// </summary>
    public sealed class ARCardConfigurationSnapshot
    {
        public ARCardConfigurationSnapshot(
            string headDefinitionId,
            string headVisualAssetId,
            string bodyDefinitionId,
            string bodyVisualAssetId,
            string legsDefinitionId,
            string legsVisualAssetId,
            string cosmeticId = null)
        {
            HeadDefinitionId = Normalize(headDefinitionId);
            HeadVisualAssetId = Normalize(headVisualAssetId);
            BodyDefinitionId = Normalize(bodyDefinitionId);
            BodyVisualAssetId = Normalize(bodyVisualAssetId);
            LegsDefinitionId = Normalize(legsDefinitionId);
            LegsVisualAssetId = Normalize(legsVisualAssetId);
            CosmeticId = Normalize(cosmeticId);
        }

        public string HeadDefinitionId { get; }
        public string HeadVisualAssetId { get; }
        public string BodyDefinitionId { get; }
        public string BodyVisualAssetId { get; }
        public string LegsDefinitionId { get; }
        public string LegsVisualAssetId { get; }
        public string CosmeticId { get; }

        public bool HasAnyPart => HeadDefinitionId.Length > 0 || BodyDefinitionId.Length > 0 ||
                                  LegsDefinitionId.Length > 0;

        public bool HasConsistentVisualReferences =>
            HasMatchingPair(HeadDefinitionId, HeadVisualAssetId) &&
            HasMatchingPair(BodyDefinitionId, BodyVisualAssetId) &&
            HasMatchingPair(LegsDefinitionId, LegsVisualAssetId);

        public bool IsWithinIdentifierLength(int maximumLength)
        {
            return maximumLength > 0 &&
                   HeadDefinitionId.Length <= maximumLength &&
                   HeadVisualAssetId.Length <= maximumLength &&
                   BodyDefinitionId.Length <= maximumLength &&
                   BodyVisualAssetId.Length <= maximumLength &&
                   LegsDefinitionId.Length <= maximumLength &&
                   LegsVisualAssetId.Length <= maximumLength &&
                   CosmeticId.Length <= maximumLength;
        }

        private static bool HasMatchingPair(string definitionId, string visualId)
        {
            return (definitionId.Length == 0 && visualId.Length == 0) ||
                   (definitionId.Length > 0 && visualId.Length > 0);
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }

    public sealed class RegisteredARCard
    {
        public RegisteredARCard(
            string cardId,
            string ownerPlayerId,
            string ownerPlayerTag,
            int cardNumber,
            string sourceAssembledCardId,
            ARCardConfigurationSnapshot configuration,
            ARCardStatus status,
            string trackingTargetId,
            long registeredUnixMilliseconds,
            long revokedUnixMilliseconds = 0)
        {
            CardId = Normalize(cardId);
            OwnerPlayerId = Normalize(ownerPlayerId);
            OwnerPlayerTag = Normalize(ownerPlayerTag);
            CardNumber = cardNumber;
            SourceAssembledCardId = Normalize(sourceAssembledCardId);
            Configuration = configuration;
            Status = status;
            TrackingTargetId = Normalize(trackingTargetId);
            RegisteredUnixMilliseconds = registeredUnixMilliseconds;
            RevokedUnixMilliseconds = revokedUnixMilliseconds;
        }

        public string CardId { get; }
        public string OwnerPlayerId { get; }
        public string OwnerPlayerTag { get; }
        public int CardNumber { get; }
        public string SourceAssembledCardId { get; }
        public ARCardConfigurationSnapshot Configuration { get; }
        public ARCardStatus Status { get; }
        public string TrackingTargetId { get; }
        public long RegisteredUnixMilliseconds { get; }
        public long RevokedUnixMilliseconds { get; }
        public bool IsActive => Status == ARCardStatus.Active;

        internal RegisteredARCard Revoke(long unixMilliseconds)
        {
            return new RegisteredARCard(
                CardId, OwnerPlayerId, OwnerPlayerTag, CardNumber, SourceAssembledCardId,
                Configuration, ARCardStatus.Revoked, TrackingTargetId,
                RegisteredUnixMilliseconds, unixMilliseconds);
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }

    public sealed class PrintableCardData
    {
        public PrintableCardData(RegisteredARCard card, string qrPayload)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            CardId = card.CardId;
            OwnerPlayerId = card.OwnerPlayerId;
            OwnerPlayerTag = card.OwnerPlayerTag;
            CardNumber = card.CardNumber;
            Configuration = card.Configuration;
            TrackingTargetId = card.TrackingTargetId;
            QrPayload = qrPayload ?? string.Empty;
        }

        public string CardId { get; }
        public string OwnerPlayerId { get; }
        public string OwnerPlayerTag { get; }
        public int CardNumber { get; }
        public ARCardConfigurationSnapshot Configuration { get; }
        public string TrackingTargetId { get; }
        public string QrPayload { get; }
    }

    public readonly struct ARCardRegistrationRequest
    {
        public ARCardRegistrationRequest(string requestId, string assembledCardId, string trackingTargetId)
        {
            RequestId = Normalize(requestId);
            AssembledCardId = Normalize(assembledCardId);
            TrackingTargetId = Normalize(trackingTargetId);
        }

        public string RequestId { get; }
        public string AssembledCardId { get; }
        public string TrackingTargetId { get; }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }

    public sealed class ARCardOperationResult
    {
        private ARCardOperationResult(
            bool succeeded,
            ARCardFailure failure,
            RegisteredARCard card,
            bool wasDuplicateRequest)
        {
            Succeeded = succeeded;
            Failure = failure;
            Card = card;
            WasDuplicateRequest = wasDuplicateRequest;
        }

        public bool Succeeded { get; }
        public ARCardFailure Failure { get; }
        public RegisteredARCard Card { get; }
        public bool WasDuplicateRequest { get; }

        public static ARCardOperationResult Success(RegisteredARCard card, bool wasDuplicateRequest = false)
        {
            return new ARCardOperationResult(true, ARCardFailure.None, card, wasDuplicateRequest);
        }

        public static ARCardOperationResult Rejected(ARCardFailure failure, RegisteredARCard card = null)
        {
            return new ARCardOperationResult(false, failure, card, false);
        }
    }

    public sealed class ARCardRegistrationCandidate
    {
        public ARCardRegistrationCandidate(
            string requestId,
            string ownerPlayerId,
            string ownerPlayerTag,
            string sourceAssembledCardId,
            ARCardConfigurationSnapshot configuration,
            string trackingTargetId,
            long registeredUnixMilliseconds)
        {
            RequestId = requestId;
            OwnerPlayerId = ownerPlayerId;
            OwnerPlayerTag = ownerPlayerTag;
            SourceAssembledCardId = sourceAssembledCardId;
            Configuration = configuration;
            TrackingTargetId = trackingTargetId;
            RegisteredUnixMilliseconds = registeredUnixMilliseconds;
        }

        public string RequestId { get; }
        public string OwnerPlayerId { get; }
        public string OwnerPlayerTag { get; }
        public string SourceAssembledCardId { get; }
        public ARCardConfigurationSnapshot Configuration { get; }
        public string TrackingTargetId { get; }
        public long RegisteredUnixMilliseconds { get; }
    }
}
