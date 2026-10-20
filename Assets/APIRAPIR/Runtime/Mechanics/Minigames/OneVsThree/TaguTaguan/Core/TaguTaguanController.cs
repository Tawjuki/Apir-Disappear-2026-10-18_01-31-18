using System;
using System.Collections.Generic;
using ApirApir.EventDirector;

namespace ApirApir.TaguTaguan
{
    /// <summary>
    /// Server-authoritative Tagu-taguan domain controller. It owns one event at a time,
    /// while movement, presentation, and transport remain replaceable adapters.
    /// </summary>
    public sealed class TaguTaguanController
    {
        private sealed class ParticipantState
        {
            public ParticipantState(string playerId, TaguTaguanRole role)
            {
                PlayerId = playerId;
                Role = role;
            }

            public string PlayerId;
            public TaguTaguanRole Role;
            public bool IsCaught;
            public bool IsDetected;
        }

        private readonly Dictionary<string, ParticipantState> participants =
            new Dictionary<string, ParticipantState>(StringComparer.Ordinal);
        private readonly List<string> participantOrder = new List<string>(4);
        private readonly Dictionary<string, ulong> lastCatchRequests =
            new Dictionary<string, ulong>(StringComparer.Ordinal);
        private readonly Dictionary<string, ulong> lastTurutotRequests =
            new Dictionary<string, ulong>(StringComparer.Ordinal);
        private readonly ITaguTaguanDetectionService detection;
        private readonly bool isAuthoritative;
        private readonly TurutotService turutot;
        private MinigameEventPlan plan;
        private string tayaPlayerId = string.Empty;
        private double lastServerTime;
        private int lastPublishedWholeSecond = -1;
        private long revision;

        public TaguTaguanController(
            bool isAuthoritative,
            TaguTaguanSettings settings = null,
            ITaguTaguanDetectionService detectionService = null)
        {
            this.isAuthoritative = isAuthoritative;
            Settings = (settings ?? new TaguTaguanSettings()).ValidatedCopy();
            detection = detectionService ?? new TaguTaguanPhysicsDetectionService();
            turutot = new TurutotService(Settings);
            turutot.Activated += value => TurutotActivated?.Invoke(value);
            turutot.NoiseCreated += value => NoiseCreated?.Invoke(value);
            turutot.NoiseExpired += HandleNoiseExpired;
            turutot.CooldownStarted += (player, availableAt) =>
                TurutotCooldownChanged?.Invoke(player, availableAt, false);
            turutot.CooldownCompleted += (player, completedAt) =>
                TurutotCooldownChanged?.Invoke(player, completedAt, true);
            turutot.ActivationRejected += (player, failure) =>
                TurutotActivationRejected?.Invoke(player, failure);
        }

        public event Action<TaguTaguanSnapshot> MinigameInitialized;
        public event Action<TaguTaguanSnapshot> RolesAssigned;
        public event Action<string> TayaSelected;
        public event Action<TaguTaguanSnapshot> PreparationStarted;
        public event Action<TaguTaguanSnapshot> MinigameStarted;
        public event Action<string> HiderDetected;
        public event Action<string> HiderNoLongerDetected;
        public event Action<string, int> HiderCaught;
        public event Action<int> RemainingHiderCountChanged;
        public event Action<TurutotNoiseEvent> TurutotActivated;
        public event Action<TurutotNoiseEvent> NoiseCreated;
        public event Action<TurutotNoiseEvent> NoiseExpired;
        public event Action<string, double, bool> TurutotCooldownChanged;
        public event Action<string, TaguTaguanFailure> TurutotActivationRejected;
        public event Action<double> TimerUpdated;
        public event Action<TaguTaguanSnapshot> StateChanged;
        public event Action<TaguTaguanResult> ResultsPublished;
        public event Action<TaguTaguanSnapshot> MinigameCompleted;

        public TaguTaguanSettings Settings { get; }
        public TaguTaguanPhase Phase { get; private set; }
        public TaguTaguanCompletionReason CompletionReason { get; private set; }
        public double StartedAt { get; private set; }
        public double EndsAt { get; private set; }
        public string TayaPlayerId => tayaPlayerId;
        public int RemainingHiderCount => CountRemainingHiders();
        public MinigameEventPlan Plan => plan;

        public TaguTaguanOperationResult TryInitialize(MinigameEventPlan eventPlan)
        {
            if (!isAuthoritative)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.NotAuthoritative);
            }

            if (eventPlan == null || eventPlan.Sequence <= 0 ||
                !string.Equals(eventPlan.MinigameId, DefaultMinigameCatalog.TaguTaguanId,
                    StringComparison.Ordinal) ||
                eventPlan.Format != MinigameFormat.OneVsThree)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidEvent);
            }

            if (plan != null)
            {
                return string.Equals(plan.EventId, eventPlan.EventId, StringComparison.Ordinal) &&
                       plan.Sequence == eventPlan.Sequence
                    ? TaguTaguanOperationResult.Success()
                    : TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidPhase);
            }

            if (!TryValidateAssignments(eventPlan, out string officialTaya))
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidAssignment);
            }

            plan = eventPlan;
            Phase = TaguTaguanPhase.Initializing;
            CompletionReason = TaguTaguanCompletionReason.None;
            participants.Clear();
            participantOrder.Clear();
            lastCatchRequests.Clear();
            lastTurutotRequests.Clear();
            tayaPlayerId = officialTaya;
            StartedAt = 0d;
            EndsAt = 0d;
            lastServerTime = 0d;
            lastPublishedWholeSecond = -1;

            for (int i = 0; i < eventPlan.Assignments.Count; i++)
            {
                MinigameParticipantAssignment assignment = eventPlan.Assignments[i];
                TaguTaguanRole role = assignment.Role == MinigameParticipantRole.Taya
                    ? TaguTaguanRole.Taya
                    : TaguTaguanRole.Hider;
                participants.Add(assignment.PlayerId, new ParticipantState(assignment.PlayerId, role));
                participantOrder.Add(assignment.PlayerId);
            }

            Touch();
            MinigameInitialized?.Invoke(CreateSnapshot(lastServerTime));
            Phase = TaguTaguanPhase.AssigningRoles;
            Touch();
            RolesAssigned?.Invoke(CreateSnapshot(lastServerTime));
            TayaSelected?.Invoke(tayaPlayerId);
            PublishState();
            return TaguTaguanOperationResult.Success();
        }

        public TaguTaguanOperationResult TryBeginPreparation()
        {
            TaguTaguanOperationResult authority = RequireAuthority();
            if (!authority.Succeeded)
            {
                return authority;
            }

            if (Phase != TaguTaguanPhase.AssigningRoles)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidPhase);
            }

            Phase = TaguTaguanPhase.Preparing;
            Touch();
            TaguTaguanSnapshot snapshot = CreateSnapshot(lastServerTime);
            PreparationStarted?.Invoke(snapshot);
            StateChanged?.Invoke(snapshot);
            return TaguTaguanOperationResult.Success();
        }

        public TaguTaguanOperationResult TryStart(double serverTime)
        {
            TaguTaguanOperationResult authority = RequireAuthority();
            if (!authority.Succeeded)
            {
                return authority;
            }

            if (Phase != TaguTaguanPhase.Preparing || !IsFinite(serverTime))
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidPhase);
            }

            StartedAt = serverTime;
            EndsAt = serverTime + Settings.durationSeconds;
            lastServerTime = serverTime;
            lastPublishedWholeSecond = (int)Math.Ceiling(Settings.durationSeconds);
            var hiders = new List<string>(3);
            for (int i = 0; i < participantOrder.Count; i++)
            {
                ParticipantState participant = participants[participantOrder[i]];
                if (participant.Role == TaguTaguanRole.Hider)
                {
                    hiders.Add(participant.PlayerId);
                }
            }

            turutot.Initialize(plan.EventId, hiders, serverTime);
            Phase = TaguTaguanPhase.Playing;
            Touch();
            TaguTaguanSnapshot snapshot = CreateSnapshot(serverTime);
            MinigameStarted?.Invoke(snapshot);
            TimerUpdated?.Invoke(Settings.durationSeconds);
            StateChanged?.Invoke(snapshot);
            return TaguTaguanOperationResult.Success();
        }

        public void Advance(double serverTime)
        {
            if (!isAuthoritative || Phase != TaguTaguanPhase.Playing || !IsFinite(serverTime) ||
                serverTime + 0.0001d < lastServerTime)
            {
                return;
            }

            lastServerTime = serverTime;
            turutot.Advance(serverTime);
            double remaining = Math.Max(0d, EndsAt - serverTime);
            int wholeSecond = (int)Math.Ceiling(remaining);
            if (wholeSecond != lastPublishedWholeSecond)
            {
                lastPublishedWholeSecond = wholeSecond;
                TimerUpdated?.Invoke(remaining);
            }

            if (serverTime + 0.0001d >= EndsAt)
            {
                Resolve(TaguTaguanCompletionReason.TimeExpired, serverTime);
            }
        }

        public void RefreshDetections(ITaguTaguanWorldStateResolver worldStates)
        {
            if (!isAuthoritative || Phase != TaguTaguanPhase.Playing || worldStates == null ||
                !worldStates.TryGetWorldState(tayaPlayerId, out TaguTaguanWorldState taya) ||
                !taya.HasFiniteValues)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < participantOrder.Count; i++)
            {
                ParticipantState hider = participants[participantOrder[i]];
                if (hider.Role != TaguTaguanRole.Hider || hider.IsCaught)
                {
                    continue;
                }

                bool visible = worldStates.TryGetWorldState(hider.PlayerId, out TaguTaguanWorldState state) &&
                               state.HasFiniteValues && detection.IsVisible(taya, state, Settings);
                if (visible == hider.IsDetected)
                {
                    continue;
                }

                hider.IsDetected = visible;
                changed = true;
                if (visible)
                {
                    HiderDetected?.Invoke(hider.PlayerId);
                }
                else
                {
                    HiderNoLongerDetected?.Invoke(hider.PlayerId);
                }
            }

            if (changed)
            {
                Touch();
                PublishState();
            }
        }

        public TaguTaguanOperationResult TryCatch(
            string requestingPlayerId,
            string targetPlayerId,
            ulong requestId,
            double serverTime,
            ITaguTaguanWorldStateResolver worldStates)
        {
            TaguTaguanOperationResult validation = ValidateGameplayRequest(
                requestingPlayerId, requestId, lastCatchRequests, serverTime);
            if (!validation.Succeeded)
            {
                return validation;
            }

            if (!string.Equals(requestingPlayerId, tayaPlayerId, StringComparison.Ordinal))
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.UnauthorizedPlayer);
            }

            if (string.IsNullOrWhiteSpace(targetPlayerId) ||
                !participants.TryGetValue(targetPlayerId, out ParticipantState target) ||
                target.Role != TaguTaguanRole.Hider)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidTarget);
            }

            lastCatchRequests[requestingPlayerId] = requestId;
            if (target.IsCaught)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.AlreadyCaught);
            }

            if (worldStates == null ||
                !worldStates.TryGetWorldState(tayaPlayerId, out TaguTaguanWorldState taya) ||
                !worldStates.TryGetWorldState(targetPlayerId, out TaguTaguanWorldState hider) ||
                !taya.HasFiniteValues || !hider.HasFiniteValues)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.WorldStateUnavailable);
            }

            if (!detection.IsWithinCatchingDistance(taya, hider, Settings))
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.OutOfRange);
            }

            if (!detection.IsVisible(taya, hider, Settings))
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.NotVisible);
            }

            if (!target.IsDetected)
            {
                target.IsDetected = true;
                HiderDetected?.Invoke(target.PlayerId);
            }

            target.IsCaught = true;
            int remaining = CountRemainingHiders();
            Touch();
            HiderCaught?.Invoke(target.PlayerId, remaining);
            RemainingHiderCountChanged?.Invoke(remaining);
            PublishState();
            if (remaining == 0)
            {
                Resolve(TaguTaguanCompletionReason.AllHidersCaught, serverTime);
            }

            return TaguTaguanOperationResult.Success();
        }

        public TaguTaguanOperationResult TryUseTurutot(
            string requestingPlayerId,
            ulong requestId,
            double serverTime,
            ITaguTaguanWorldStateResolver worldStates,
            out TurutotNoiseEvent noise)
        {
            noise = default;
            TaguTaguanOperationResult validation = ValidateGameplayRequest(
                requestingPlayerId, requestId, lastTurutotRequests, serverTime);
            if (!validation.Succeeded)
            {
                TurutotActivationRejected?.Invoke(requestingPlayerId ?? string.Empty, validation.Failure);
                return validation;
            }

            if (!participants.TryGetValue(requestingPlayerId, out ParticipantState participant) ||
                participant.Role != TaguTaguanRole.Hider)
            {
                TurutotActivationRejected?.Invoke(
                    requestingPlayerId ?? string.Empty, TaguTaguanFailure.UnauthorizedPlayer);
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.UnauthorizedPlayer);
            }

            lastTurutotRequests[requestingPlayerId] = requestId;
            if (participant.IsCaught)
            {
                TurutotActivationRejected?.Invoke(requestingPlayerId, TaguTaguanFailure.AlreadyCaught);
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.AlreadyCaught);
            }

            if (worldStates == null ||
                !worldStates.TryGetWorldState(requestingPlayerId, out TaguTaguanWorldState worldState) ||
                !worldState.HasFiniteValues)
            {
                TurutotActivationRejected?.Invoke(
                    requestingPlayerId, TaguTaguanFailure.WorldStateUnavailable);
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.WorldStateUnavailable);
            }

            TaguTaguanOperationResult result = turutot.TryActivate(
                requestingPlayerId, worldState.Position, serverTime, out noise);
            if (result.Succeeded)
            {
                Touch();
                PublishState();
            }

            return result;
        }

        public TaguTaguanOperationResult TryCancel(double serverTime)
        {
            TaguTaguanOperationResult authority = RequireAuthority();
            if (!authority.Succeeded)
            {
                return authority;
            }

            if (Phase == TaguTaguanPhase.None || Phase == TaguTaguanPhase.Completed)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidPhase);
            }

            Resolve(TaguTaguanCompletionReason.Cancelled, IsFinite(serverTime) ? serverTime : lastServerTime);
            return TaguTaguanOperationResult.Success();
        }

        public TaguTaguanSnapshot CreateSnapshot(double capturedAt)
        {
            var participantSnapshots = new List<TaguTaguanParticipantSnapshot>(participantOrder.Count);
            for (int i = 0; i < participantOrder.Count; i++)
            {
                ParticipantState participant = participants[participantOrder[i]];
                participantSnapshots.Add(new TaguTaguanParticipantSnapshot(
                    participant.PlayerId,
                    participant.Role,
                    participant.IsCaught,
                    participant.IsDetected,
                    turutot.GetAvailableAt(participant.PlayerId)));
            }

            var noises = new List<TurutotNoiseEvent>(turutot.ActiveNoises.Count);
            for (int i = 0; i < turutot.ActiveNoises.Count; i++)
            {
                noises.Add(turutot.ActiveNoises[i]);
            }

            return new TaguTaguanSnapshot(
                plan?.MatchId,
                plan?.EventId,
                plan?.Sequence ?? 0,
                revision,
                Phase,
                StartedAt,
                EndsAt,
                capturedAt,
                CompletionReason,
                participantSnapshots.AsReadOnly(),
                noises.AsReadOnly());
        }

        private TaguTaguanOperationResult ValidateGameplayRequest(
            string playerId,
            ulong requestId,
            Dictionary<string, ulong> requestHistory,
            double serverTime)
        {
            TaguTaguanOperationResult authority = RequireAuthority();
            if (!authority.Succeeded)
            {
                return authority;
            }

            if (Phase != TaguTaguanPhase.Playing)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidPhase);
            }

            if (string.IsNullOrWhiteSpace(playerId) || requestId == 0 || !IsFinite(serverTime))
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.InvalidRequest);
            }

            if (requestHistory.TryGetValue(playerId, out ulong lastRequest) && requestId <= lastRequest)
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.DuplicateOrStaleRequest);
            }

            if (!participants.ContainsKey(playerId))
            {
                return TaguTaguanOperationResult.Reject(TaguTaguanFailure.UnauthorizedPlayer);
            }

            return TaguTaguanOperationResult.Success();
        }

        private void Resolve(TaguTaguanCompletionReason reason, double serverTime)
        {
            if (Phase == TaguTaguanPhase.ResolvingResults || Phase == TaguTaguanPhase.Completed)
            {
                return;
            }

            Phase = TaguTaguanPhase.ResolvingResults;
            CompletionReason = reason;
            lastServerTime = serverTime;
            Touch();

            var caught = new List<string>(3);
            var surviving = new List<string>(3);
            for (int i = 0; i < participantOrder.Count; i++)
            {
                ParticipantState participant = participants[participantOrder[i]];
                if (participant.Role != TaguTaguanRole.Hider)
                {
                    continue;
                }

                (participant.IsCaught ? caught : surviving).Add(participant.PlayerId);
            }

            bool tayaSucceeded = reason == TaguTaguanCompletionReason.AllHidersCaught;
            var result = new TaguTaguanResult(
                plan.MatchId,
                plan.EventId,
                plan.Sequence,
                reason,
                tayaSucceeded,
                caught.AsReadOnly(),
                surviving.AsReadOnly());
            turutot.Clear();
            Phase = TaguTaguanPhase.Completed;
            Touch();
            ResultsPublished?.Invoke(result);
            TaguTaguanSnapshot snapshot = CreateSnapshot(serverTime);
            MinigameCompleted?.Invoke(snapshot);
            StateChanged?.Invoke(snapshot);
        }

        private void HandleNoiseExpired(TurutotNoiseEvent noise)
        {
            NoiseExpired?.Invoke(noise);
            if (Phase == TaguTaguanPhase.Playing)
            {
                Touch();
                PublishState();
            }
        }

        private int CountRemainingHiders()
        {
            int remaining = 0;
            for (int i = 0; i < participantOrder.Count; i++)
            {
                ParticipantState participant = participants[participantOrder[i]];
                if (participant.Role == TaguTaguanRole.Hider && !participant.IsCaught)
                {
                    remaining++;
                }
            }

            return remaining;
        }

        private static bool TryValidateAssignments(MinigameEventPlan eventPlan, out string tayaPlayerId)
        {
            tayaPlayerId = string.Empty;
            if (eventPlan.Assignments == null || eventPlan.Assignments.Count != 4)
            {
                return false;
            }

            var playerIds = new HashSet<string>(StringComparer.Ordinal);
            int hiderCount = 0;
            int activeContenders = 0;
            for (int i = 0; i < eventPlan.Assignments.Count; i++)
            {
                MinigameParticipantAssignment assignment = eventPlan.Assignments[i];
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.PlayerId) ||
                    !playerIds.Add(assignment.PlayerId))
                {
                    return false;
                }

                if (assignment.Membership == ChampionshipMembership.ActiveContender)
                {
                    activeContenders++;
                }

                if (assignment.Role == MinigameParticipantRole.Taya &&
                    assignment.Team == MinigameTeam.TeamB && tayaPlayerId.Length == 0)
                {
                    tayaPlayerId = assignment.PlayerId;
                }
                else if (assignment.Role == MinigameParticipantRole.Hider &&
                         assignment.Team == MinigameTeam.TeamA)
                {
                    hiderCount++;
                }
                else
                {
                    return false;
                }
            }

            if (tayaPlayerId.Length == 0 || hiderCount != 3 ||
                (activeContenders != 4 && activeContenders != 3))
            {
                return false;
            }

            if (activeContenders == 3)
            {
                for (int i = 0; i < eventPlan.Assignments.Count; i++)
                {
                    MinigameParticipantAssignment assignment = eventPlan.Assignments[i];
                    if ((assignment.Role == MinigameParticipantRole.Taya) !=
                        (assignment.Membership == ChampionshipMembership.EliminatedPlayer))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private TaguTaguanOperationResult RequireAuthority() => isAuthoritative
            ? TaguTaguanOperationResult.Success()
            : TaguTaguanOperationResult.Reject(TaguTaguanFailure.NotAuthoritative);

        private void Touch()
        {
            revision++;
        }

        private void PublishState()
        {
            StateChanged?.Invoke(CreateSnapshot(lastServerTime));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
