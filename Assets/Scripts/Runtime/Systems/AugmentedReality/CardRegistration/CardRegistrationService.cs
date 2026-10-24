using System;
using System.Collections.Generic;

namespace ApirApir.AugmentedReality
{
    public interface IAssembledCardSnapshotProvider
    {
        bool TryGetSnapshot(
            string authenticatedOwnerPlayerId,
            string assembledCardId,
            out ARCardConfigurationSnapshot configuration);
    }

    public interface IPlayerTagProvider
    {
        bool TryGetPlayerTag(string playerId, out string playerTag);
    }

    public interface IARClock
    {
        long UtcNowUnixMilliseconds { get; }
    }

    public sealed class SystemARClock : IARClock
    {
        public long UtcNowUnixMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public sealed class CardRegistrationService
    {
        public const int MaximumIdentifierLength = 128;
        public const int MaximumPlayerTagLength = 192;

        private readonly IARCardRegistry registry;
        private readonly IAssembledCardSnapshotProvider assembledCards;
        private readonly IPlayerTagProvider playerTags;
        private readonly QRCodeService qrCodes;
        private readonly IARClock clock;

        public CardRegistrationService(
            IARCardRegistry registry,
            IAssembledCardSnapshotProvider assembledCards,
            IPlayerTagProvider playerTags,
            int maximumActiveCardsPerPlayer,
            QRCodeService qrCodes = null,
            IARClock clock = null)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.assembledCards = assembledCards ?? throw new ArgumentNullException(nameof(assembledCards));
            this.playerTags = playerTags ?? throw new ArgumentNullException(nameof(playerTags));
            if (maximumActiveCardsPerPlayer <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumActiveCardsPerPlayer));
            }

            MaximumActiveCardsPerPlayer = maximumActiveCardsPerPlayer;
            this.qrCodes = qrCodes ?? new QRCodeService();
            this.clock = clock ?? new SystemARClock();
        }

        public event Action<RegisteredARCard> CardRegistered;
        public event Action<RegisteredARCard> CardRevoked;
        public event Action<string, ARCardFailure> CardValidationFailed;

        public int MaximumActiveCardsPerPlayer { get; }

        public ARCardOperationResult Register(
            string authenticatedPlayerId,
            ARCardRegistrationRequest request)
        {
            string playerId = Normalize(authenticatedPlayerId);
            if (!IsValidIdentifier(playerId) || !IsValidIdentifier(request.RequestId) ||
                !IsValidIdentifier(request.AssembledCardId) ||
                request.TrackingTargetId.Length > MaximumIdentifierLength)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.InvalidRequest);
            }

            if (!playerTags.TryGetPlayerTag(playerId, out string playerTag))
            {
                return ARCardOperationResult.Rejected(ARCardFailure.PlayerTagNotFound);
            }

            playerTag = Normalize(playerTag);
            if (playerTag.Length == 0 || playerTag.Length > MaximumPlayerTagLength)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.PlayerTagNotFound);
            }

            if (!assembledCards.TryGetSnapshot(playerId, request.AssembledCardId, out ARCardConfigurationSnapshot snapshot) ||
                snapshot == null)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.AssembledCardNotFound);
            }

            if (!snapshot.HasAnyPart || !snapshot.HasConsistentVisualReferences ||
                !snapshot.IsWithinIdentifierLength(MaximumIdentifierLength))
            {
                return ARCardOperationResult.Rejected(ARCardFailure.InvalidConfiguration);
            }

            var candidate = new ARCardRegistrationCandidate(
                request.RequestId,
                playerId,
                playerTag,
                request.AssembledCardId,
                snapshot,
                request.TrackingTargetId,
                clock.UtcNowUnixMilliseconds);
            ARCardOperationResult result = registry.RegisterCard(candidate, MaximumActiveCardsPerPlayer);
            if (result.Succeeded && !result.WasDuplicateRequest)
            {
                CardRegistered?.Invoke(result.Card);
            }

            return result;
        }

        public ARCardOperationResult GetCard(string cardId)
        {
            return registry.GetCard(Normalize(cardId));
        }

        public ARCardOperationResult Validate(string cardId)
        {
            string normalized = Normalize(cardId);
            ARCardOperationResult result = registry.ValidateCard(normalized);
            if (!result.Succeeded)
            {
                CardValidationFailed?.Invoke(normalized, result.Failure);
            }

            return result;
        }

        public ARCardOperationResult Revoke(string authenticatedPlayerId, string cardId)
        {
            ARCardOperationResult result = registry.RevokeCard(
                Normalize(cardId), Normalize(authenticatedPlayerId));
            if (result.Succeeded)
            {
                CardRevoked?.Invoke(result.Card);
            }

            return result;
        }

        public IReadOnlyList<RegisteredARCard> GetCardsByOwner(string ownerPlayerId)
        {
            return registry.GetCardsByOwner(Normalize(ownerPlayerId));
        }

        public bool CheckOwnership(string cardId, string playerId)
        {
            return registry.CheckOwnership(Normalize(cardId), Normalize(playerId));
        }

        public ARCardOperationResult TryCreatePrintableData(
            string cardId,
            out PrintableCardData printableData)
        {
            printableData = null;
            ARCardOperationResult validation = Validate(cardId);
            if (!validation.Succeeded || validation.Card == null)
            {
                return validation;
            }

            printableData = new PrintableCardData(
                validation.Card, qrCodes.CreatePayload(validation.Card.CardId));
            return validation;
        }

        private static bool IsValidIdentifier(string value)
        {
            return value.Length > 0 && value.Length <= MaximumIdentifierLength;
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
