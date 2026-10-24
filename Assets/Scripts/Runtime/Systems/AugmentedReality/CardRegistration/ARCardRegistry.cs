using System;
using System.Collections.Generic;

namespace ApirApir.AugmentedReality
{
    public interface IARCardRegistry
    {
        ARCardOperationResult RegisterCard(ARCardRegistrationCandidate candidate, int maximumActiveCards);
        ARCardOperationResult GetCard(string cardId);
        ARCardOperationResult ValidateCard(string cardId);
        ARCardOperationResult RevokeCard(string cardId, string authenticatedActorPlayerId);
        IReadOnlyList<RegisteredARCard> GetCardsByOwner(string ownerPlayerId);
        bool CheckOwnership(string cardId, string playerId);
    }

    public interface IARCardIdGenerator
    {
        string CreateCardId();
    }

    public sealed class GuidARCardIdGenerator : IARCardIdGenerator
    {
        public string CreateCardId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    public interface IARCardAdministrativeAuthorization
    {
        bool CanRevokeAnyCard(string authenticatedPlayerId);
    }

    public sealed class DenyARCardAdministrativeAuthorization : IARCardAdministrativeAuthorization
    {
        public bool CanRevokeAnyCard(string authenticatedPlayerId)
        {
            return false;
        }
    }

    /// <summary>
    /// Process-local development registry. It is thread-safe and preserves tombstones while
    /// the process lives, but it is deliberately not a global or cross-session database.
    /// Replace IARCardRegistry with a hosted authoritative implementation for production.
    /// </summary>
    public sealed class DevelopmentInMemoryARCardRegistry : IARCardRegistry
    {
        private const int MaximumIdGenerationAttempts = 16;
        private readonly object gate = new object();
        private readonly Dictionary<string, RegisteredARCard> cards =
            new Dictionary<string, RegisteredARCard>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> lastNumberByOwner =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> cardIdByRequest =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly IARCardIdGenerator idGenerator;
        private readonly IARCardAdministrativeAuthorization administrativeAuthorization;

        public DevelopmentInMemoryARCardRegistry(
            IARCardIdGenerator idGenerator = null,
            IARCardAdministrativeAuthorization administrativeAuthorization = null)
        {
            this.idGenerator = idGenerator ?? new GuidARCardIdGenerator();
            this.administrativeAuthorization = administrativeAuthorization ??
                                               new DenyARCardAdministrativeAuthorization();
        }

        /// <summary>Allows tests and offline development to simulate backend outages.</summary>
        public bool IsAvailable { get; set; } = true;

        public ARCardOperationResult RegisterCard(
            ARCardRegistrationCandidate candidate,
            int maximumActiveCards)
        {
            if (!IsAvailable)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.RegistryUnavailable);
            }

            if (candidate == null || maximumActiveCards <= 0 ||
                string.IsNullOrWhiteSpace(candidate.RequestId) ||
                string.IsNullOrWhiteSpace(candidate.OwnerPlayerId) ||
                string.IsNullOrWhiteSpace(candidate.OwnerPlayerTag) ||
                string.IsNullOrWhiteSpace(candidate.SourceAssembledCardId) ||
                candidate.Configuration == null || !candidate.Configuration.HasAnyPart ||
                !candidate.Configuration.HasConsistentVisualReferences ||
                !candidate.Configuration.IsWithinIdentifierLength(
                    CardRegistrationService.MaximumIdentifierLength))
            {
                return ARCardOperationResult.Rejected(ARCardFailure.InvalidRequest);
            }

            lock (gate)
            {
                string requestKey = CreateRequestKey(candidate.OwnerPlayerId, candidate.RequestId);
                if (cardIdByRequest.TryGetValue(requestKey, out string existingId) &&
                    cards.TryGetValue(existingId, out RegisteredARCard existing))
                {
                    return ARCardOperationResult.Success(existing, true);
                }

                int activeCount = 0;
                foreach (RegisteredARCard existingCard in cards.Values)
                {
                    if (existingCard.IsActive && string.Equals(
                            existingCard.OwnerPlayerId, candidate.OwnerPlayerId, StringComparison.Ordinal))
                    {
                        activeCount++;
                    }
                }

                if (activeCount >= maximumActiveCards)
                {
                    return ARCardOperationResult.Rejected(ARCardFailure.ActiveCardLimitReached);
                }

                string cardId = CreateUnusedCardId();
                if (cardId.Length == 0)
                {
                    return ARCardOperationResult.Rejected(ARCardFailure.DuplicateCardId);
                }

                int cardNumber = lastNumberByOwner.TryGetValue(candidate.OwnerPlayerId, out int last)
                    ? checked(last + 1)
                    : 1;
                var registeredCard = new RegisteredARCard(
                    cardId,
                    candidate.OwnerPlayerId,
                    candidate.OwnerPlayerTag,
                    cardNumber,
                    candidate.SourceAssembledCardId,
                    candidate.Configuration,
                    ARCardStatus.Active,
                    candidate.TrackingTargetId,
                    candidate.RegisteredUnixMilliseconds);

                cards.Add(cardId, registeredCard);
                lastNumberByOwner[candidate.OwnerPlayerId] = cardNumber;
                cardIdByRequest.Add(requestKey, cardId);
                return ARCardOperationResult.Success(registeredCard);
            }
        }

        public ARCardOperationResult GetCard(string cardId)
        {
            if (!IsAvailable)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.RegistryUnavailable);
            }

            string normalized = Normalize(cardId);
            if (normalized.Length == 0)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.InvalidRequest);
            }

            lock (gate)
            {
                return cards.TryGetValue(normalized, out RegisteredARCard card)
                    ? ARCardOperationResult.Success(card)
                    : ARCardOperationResult.Rejected(ARCardFailure.CardNotFound);
            }
        }

        public ARCardOperationResult ValidateCard(string cardId)
        {
            ARCardOperationResult result = GetCard(cardId);
            if (!result.Succeeded || result.Card == null)
            {
                return result;
            }

            return result.Card.IsActive
                ? result
                : ARCardOperationResult.Rejected(ARCardFailure.CardRevoked, result.Card);
        }

        public ARCardOperationResult RevokeCard(string cardId, string authenticatedActorPlayerId)
        {
            if (!IsAvailable)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.RegistryUnavailable);
            }

            string normalizedCardId = Normalize(cardId);
            string actorId = Normalize(authenticatedActorPlayerId);
            if (normalizedCardId.Length == 0 || actorId.Length == 0)
            {
                return ARCardOperationResult.Rejected(ARCardFailure.InvalidRequest);
            }

            lock (gate)
            {
                if (!cards.TryGetValue(normalizedCardId, out RegisteredARCard card))
                {
                    return ARCardOperationResult.Rejected(ARCardFailure.CardNotFound);
                }

                if (!string.Equals(card.OwnerPlayerId, actorId, StringComparison.Ordinal) &&
                    !administrativeAuthorization.CanRevokeAnyCard(actorId))
                {
                    return ARCardOperationResult.Rejected(ARCardFailure.Unauthorized);
                }

                if (!card.IsActive)
                {
                    return ARCardOperationResult.Rejected(ARCardFailure.AlreadyRevoked, card);
                }

                RegisteredARCard revoked = card.Revoke(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                cards[normalizedCardId] = revoked;
                return ARCardOperationResult.Success(revoked);
            }
        }

        public IReadOnlyList<RegisteredARCard> GetCardsByOwner(string ownerPlayerId)
        {
            if (!IsAvailable)
            {
                return Array.Empty<RegisteredARCard>();
            }

            string ownerId = Normalize(ownerPlayerId);
            var result = new List<RegisteredARCard>();
            lock (gate)
            {
                foreach (RegisteredARCard card in cards.Values)
                {
                    if (string.Equals(card.OwnerPlayerId, ownerId, StringComparison.Ordinal))
                    {
                        result.Add(card);
                    }
                }
            }

            result.Sort((left, right) => left.CardNumber.CompareTo(right.CardNumber));
            return result;
        }

        public bool CheckOwnership(string cardId, string playerId)
        {
            if (!IsAvailable)
            {
                return false;
            }

            lock (gate)
            {
                return cards.TryGetValue(Normalize(cardId), out RegisteredARCard card) &&
                       string.Equals(card.OwnerPlayerId, Normalize(playerId), StringComparison.Ordinal);
            }
        }

        private string CreateUnusedCardId()
        {
            for (int attempt = 0; attempt < MaximumIdGenerationAttempts; attempt++)
            {
                string candidate;
                try
                {
                    candidate = Normalize(idGenerator.CreateCardId()).ToLowerInvariant();
                }
                catch (Exception)
                {
                    continue;
                }

                if (QRCodeService.IsValidCardId(candidate) && !cards.ContainsKey(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string CreateRequestKey(string ownerPlayerId, string requestId)
        {
            return ownerPlayerId + "\u001f" + requestId;
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
