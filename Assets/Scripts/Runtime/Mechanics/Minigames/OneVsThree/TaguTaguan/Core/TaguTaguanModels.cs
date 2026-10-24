using System;
using System.Collections.Generic;
using UnityEngine;

namespace ApirApir.TaguTaguan
{
    public enum TaguTaguanPhase : byte
    {
        None,
        Initializing,
        AssigningRoles,
        Preparing,
        Playing,
        ResolvingResults,
        Completed
    }

    public enum TaguTaguanRole : byte
    {
        None,
        Taya,
        Hider
    }

    public enum TaguTaguanCompletionReason : byte
    {
        None,
        AllHidersCaught,
        TimeExpired,
        Cancelled
    }

    public enum TaguTaguanFailure : byte
    {
        None,
        NotAuthoritative,
        InvalidRequest,
        InvalidEvent,
        InvalidPhase,
        InvalidAssignment,
        UnauthorizedPlayer,
        InvalidTarget,
        AlreadyCaught,
        NotVisible,
        OutOfRange,
        TurutotUnavailable,
        DuplicateOrStaleRequest,
        WorldStateUnavailable
    }

    public readonly struct TaguTaguanOperationResult
    {
        private TaguTaguanOperationResult(bool succeeded, TaguTaguanFailure failure)
        {
            Succeeded = succeeded;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public TaguTaguanFailure Failure { get; }

        public static TaguTaguanOperationResult Success() =>
            new TaguTaguanOperationResult(true, TaguTaguanFailure.None);

        public static TaguTaguanOperationResult Reject(TaguTaguanFailure failure) =>
            new TaguTaguanOperationResult(false, failure);
    }

    [Serializable]
    public sealed class TaguTaguanSettings
    {
        [Min(1f)] public float durationSeconds = 30f;
        [Min(0f)] public float initialTurutotCooldownSeconds = 10f;
        [Min(0.1f)] public float turutotReuseCooldownSeconds = 10f;
        [Min(0.1f)] public float turutotNoiseLifetimeSeconds = 3f;
        [Min(0.1f)] public float turutotNotificationRange = 25f;
        [Min(0.1f)] public float detectionDistance = 14f;
        [Range(1f, 360f)] public float fieldOfViewDegrees = 100f;
        [Min(0.1f)] public float catchingDistance = 1.75f;
        [Min(0.02f)] public float detectionRefreshSeconds = 0.1f;
        [Min(0.1f)] public float tayaMovementSpeedMultiplier = 1.15f;
        [Min(0.1f)] public float hiderMovementSpeedMultiplier = 1f;
        public bool caughtHidersMayMove;
        public LayerMask obstacleLayers = ~0;

        public TaguTaguanSettings ValidatedCopy()
        {
            return new TaguTaguanSettings
            {
                durationSeconds = Positive(durationSeconds, 30f),
                initialTurutotCooldownSeconds = NonNegative(initialTurutotCooldownSeconds, 10f),
                turutotReuseCooldownSeconds = Positive(turutotReuseCooldownSeconds, 10f),
                turutotNoiseLifetimeSeconds = Positive(turutotNoiseLifetimeSeconds, 3f),
                turutotNotificationRange = Positive(turutotNotificationRange, 25f),
                detectionDistance = Positive(detectionDistance, 14f),
                fieldOfViewDegrees = Mathf.Clamp(Positive(fieldOfViewDegrees, 100f), 1f, 360f),
                catchingDistance = Positive(catchingDistance, 1.75f),
                detectionRefreshSeconds = Positive(detectionRefreshSeconds, 0.1f),
                tayaMovementSpeedMultiplier = Positive(tayaMovementSpeedMultiplier, 1.15f),
                hiderMovementSpeedMultiplier = Positive(hiderMovementSpeedMultiplier, 1f),
                caughtHidersMayMove = caughtHidersMayMove,
                obstacleLayers = obstacleLayers
            };
        }

        private static float Positive(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? fallback : value;

        private static float NonNegative(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? fallback : value;
    }

    public readonly struct TaguTaguanWorldState
    {
        public TaguTaguanWorldState(Vector3 position, Vector3 forward)
        {
            Position = position;
            Forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public bool HasFiniteValues => IsFiniteVector(Position) && IsFiniteVector(Forward);

        private static bool IsFiniteVector(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public interface ITaguTaguanWorldStateResolver
    {
        bool TryGetWorldState(string playerId, out TaguTaguanWorldState state);
    }

    public interface ITaguTaguanMovementAdapter
    {
        void ApplyTaguTaguanMovement(
            string playerId,
            TaguTaguanRole role,
            float speedMultiplier,
            bool movementAllowed);

        void ClearTaguTaguanMovement(string playerId);
    }

    public sealed class TaguTaguanParticipantSnapshot
    {
        public TaguTaguanParticipantSnapshot(
            string playerId,
            TaguTaguanRole role,
            bool isCaught,
            bool isDetected,
            double turutotAvailableAt)
        {
            PlayerId = playerId ?? string.Empty;
            Role = role;
            IsCaught = isCaught;
            IsDetected = isDetected;
            TurutotAvailableAt = turutotAvailableAt;
        }

        public string PlayerId { get; }
        public TaguTaguanRole Role { get; }
        public bool IsCaught { get; }
        public bool IsDetected { get; }
        public double TurutotAvailableAt { get; }
    }

    public readonly struct TurutotNoiseEvent
    {
        public TurutotNoiseEvent(
            string noiseEventId,
            string sourcePlayerId,
            Vector3 position,
            double activatedAt,
            double expiresAt,
            float notificationRange)
        {
            NoiseEventId = noiseEventId ?? string.Empty;
            SourcePlayerId = sourcePlayerId ?? string.Empty;
            Position = position;
            ActivatedAt = activatedAt;
            ExpiresAt = expiresAt;
            NotificationRange = notificationRange;
        }

        public string NoiseEventId { get; }
        public string SourcePlayerId { get; }
        public Vector3 Position { get; }
        public double ActivatedAt { get; }
        public double ExpiresAt { get; }
        public float NotificationRange { get; }
    }

    public sealed class TaguTaguanSnapshot
    {
        public TaguTaguanSnapshot(
            string matchId,
            string eventId,
            long eventSequence,
            long revision,
            TaguTaguanPhase phase,
            double startedAt,
            double endsAt,
            double capturedAt,
            TaguTaguanCompletionReason completionReason,
            IReadOnlyList<TaguTaguanParticipantSnapshot> participants,
            IReadOnlyList<TurutotNoiseEvent> activeNoises)
        {
            MatchId = matchId ?? string.Empty;
            EventId = eventId ?? string.Empty;
            EventSequence = eventSequence;
            Revision = revision;
            Phase = phase;
            StartedAt = startedAt;
            EndsAt = endsAt;
            CapturedAt = capturedAt;
            CompletionReason = completionReason;
            Participants = participants ?? Array.Empty<TaguTaguanParticipantSnapshot>();
            ActiveNoises = activeNoises ?? Array.Empty<TurutotNoiseEvent>();
        }

        public string MatchId { get; }
        public string EventId { get; }
        public long EventSequence { get; }
        public long Revision { get; }
        public TaguTaguanPhase Phase { get; }
        public double StartedAt { get; }
        public double EndsAt { get; }
        public double CapturedAt { get; }
        public TaguTaguanCompletionReason CompletionReason { get; }
        public IReadOnlyList<TaguTaguanParticipantSnapshot> Participants { get; }
        public IReadOnlyList<TurutotNoiseEvent> ActiveNoises { get; }
        public int RemainingHiderCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Participants.Count; i++)
                {
                    if (Participants[i].Role == TaguTaguanRole.Hider && !Participants[i].IsCaught)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    public sealed class TaguTaguanResult
    {
        public TaguTaguanResult(
            string matchId,
            string eventId,
            long eventSequence,
            TaguTaguanCompletionReason reason,
            bool tayaSucceeded,
            IReadOnlyList<string> caughtHiderIds,
            IReadOnlyList<string> survivingHiderIds)
        {
            MatchId = matchId ?? string.Empty;
            EventId = eventId ?? string.Empty;
            EventSequence = eventSequence;
            Reason = reason;
            TayaSucceeded = tayaSucceeded;
            CaughtHiderIds = caughtHiderIds ?? Array.Empty<string>();
            SurvivingHiderIds = survivingHiderIds ?? Array.Empty<string>();
        }

        public string MatchId { get; }
        public string EventId { get; }
        public long EventSequence { get; }
        public TaguTaguanCompletionReason Reason { get; }
        public bool TayaSucceeded { get; }
        public IReadOnlyList<string> CaughtHiderIds { get; }
        public IReadOnlyList<string> SurvivingHiderIds { get; }
    }
}
