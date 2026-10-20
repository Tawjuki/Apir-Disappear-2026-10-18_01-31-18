using System;
using System.Collections.Generic;
using UnityEngine;

namespace ApirApir.TaguTaguan
{
    /// <summary>
    /// Stores one authoritative timestamp per Hider and temporary, fixed-position noises.
    /// It never runs per-Hider timers and never changes player movement or visibility.
    /// </summary>
    public sealed class TurutotService
    {
        private const int MaximumActiveNoises = 16;
        private readonly Dictionary<string, double> availableAt =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<TurutotNoiseEvent> activeNoises = new List<TurutotNoiseEvent>(4);
        private readonly TaguTaguanSettings settings;
        private string eventId = string.Empty;
        private long nextNoiseSequence;

        public TurutotService(TaguTaguanSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public event Action<TurutotNoiseEvent> Activated;
        public event Action<TurutotNoiseEvent> NoiseCreated;
        public event Action<TurutotNoiseEvent> NoiseExpired;
        public event Action<string, double> CooldownStarted;
        public event Action<string, double> CooldownCompleted;
        public event Action<string, TaguTaguanFailure> ActivationRejected;

        public IReadOnlyList<TurutotNoiseEvent> ActiveNoises => activeNoises;

        public void Initialize(string activeEventId, IEnumerable<string> hiderIds, double startedAt)
        {
            eventId = activeEventId ?? string.Empty;
            availableAt.Clear();
            activeNoises.Clear();
            nextNoiseSequence = 0;
            if (hiderIds == null)
            {
                return;
            }

            foreach (string hiderId in hiderIds)
            {
                if (!string.IsNullOrWhiteSpace(hiderId))
                {
                    availableAt[hiderId] = startedAt + settings.initialTurutotCooldownSeconds;
                }
            }
        }

        public double GetAvailableAt(string playerId)
        {
            if (playerId == null || !availableAt.TryGetValue(playerId, out double value))
            {
                return 0d;
            }

            return value < 0d ? 0d : value;
        }

        public TaguTaguanOperationResult TryActivate(
            string playerId,
            Vector3 authoritativePosition,
            double serverTime,
            out TurutotNoiseEvent noise)
        {
            noise = default;
            if (string.IsNullOrWhiteSpace(playerId) || !IsFinite(serverTime) ||
                !IsFinite(authoritativePosition))
            {
                return Reject(playerId, TaguTaguanFailure.InvalidRequest);
            }

            if (!availableAt.TryGetValue(playerId, out double nextAvailable))
            {
                return Reject(playerId, TaguTaguanFailure.UnauthorizedPlayer);
            }

            if (serverTime + 0.0001d < nextAvailable)
            {
                return Reject(playerId, TaguTaguanFailure.TurutotUnavailable);
            }

            Expire(serverTime);
            if (activeNoises.Count >= MaximumActiveNoises)
            {
                activeNoises.RemoveAt(0);
            }

            nextNoiseSequence++;
            string noiseId = eventId + ":turutot:" + nextNoiseSequence;
            noise = new TurutotNoiseEvent(
                noiseId,
                playerId,
                authoritativePosition,
                serverTime,
                serverTime + settings.turutotNoiseLifetimeSeconds,
                settings.turutotNotificationRange);
            activeNoises.Add(noise);
            availableAt[playerId] = serverTime + settings.turutotReuseCooldownSeconds;
            Activated?.Invoke(noise);
            NoiseCreated?.Invoke(noise);
            CooldownStarted?.Invoke(playerId, availableAt[playerId]);
            return TaguTaguanOperationResult.Success();
        }

        public void Advance(double serverTime)
        {
            if (!IsFinite(serverTime))
            {
                return;
            }

            Expire(serverTime);
            if (availableAt.Count == 0)
            {
                return;
            }

            // Completion notifications are emitted once by changing the stored sentinel.
            var completed = new List<string>(availableAt.Count);
            foreach (KeyValuePair<string, double> entry in availableAt)
            {
                if (entry.Value >= 0d && serverTime + 0.0001d >= entry.Value)
                {
                    completed.Add(entry.Key);
                }
            }

            for (int i = 0; i < completed.Count; i++)
            {
                string playerId = completed[i];
                availableAt[playerId] = -availableAt[playerId] - 1d;
                CooldownCompleted?.Invoke(playerId, serverTime);
            }
        }

        public void Clear()
        {
            for (int i = activeNoises.Count - 1; i >= 0; i--)
            {
                NoiseExpired?.Invoke(activeNoises[i]);
            }

            activeNoises.Clear();
            availableAt.Clear();
            eventId = string.Empty;
        }

        private void Expire(double serverTime)
        {
            for (int i = activeNoises.Count - 1; i >= 0; i--)
            {
                if (activeNoises[i].ExpiresAt > serverTime)
                {
                    continue;
                }

                TurutotNoiseEvent expired = activeNoises[i];
                activeNoises.RemoveAt(i);
                NoiseExpired?.Invoke(expired);
            }
        }

        private TaguTaguanOperationResult Reject(string playerId, TaguTaguanFailure failure)
        {
            ActivationRejected?.Invoke(playerId ?? string.Empty, failure);
            return TaguTaguanOperationResult.Reject(failure);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
