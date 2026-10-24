using System;
using System.Collections.Generic;

namespace ApirApir.Multiplayer
{
    /// <summary>Pure server-side lobby state. It deliberately has no Unity or transport dependencies.</summary>
    public sealed class AuthoritativeSessionRoster
    {
        private readonly List<SessionPlayerState> players;
        private readonly Dictionary<ulong, ulong> lastReadyRequestByClient;

        public AuthoritativeSessionRoster(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            players = new List<SessionPlayerState>(capacity);
            lastReadyRequestByClient = new Dictionary<ulong, ulong>(capacity);
        }

        public int Capacity { get; }
        public int Count => players.Count;
        public MultiplayerSessionState State { get; private set; } = MultiplayerSessionState.Offline;
        public IReadOnlyList<SessionPlayerState> Players => players;
        public bool CanStart => State == MultiplayerSessionState.WaitingRoom &&
                                players.Count == Capacity &&
                                players.TrueForAll(player => player.isReady);

        public void BeginWaitingRoom()
        {
            State = MultiplayerSessionState.WaitingRoom;
        }

        public bool TryAdd(
            ulong clientId,
            string playerId,
            string sessionPlayerId,
            ulong hostClientId,
            out string rejectionReason)
        {
            playerId = NormalizeIdentity(playerId);
            sessionPlayerId = NormalizeIdentity(sessionPlayerId);

            if (State != MultiplayerSessionState.WaitingRoom)
            {
                rejectionReason = "The match is not accepting players.";
                return false;
            }

            if (players.Count >= Capacity)
            {
                rejectionReason = $"This session supports a maximum of {Capacity} players.";
                return false;
            }

            if (playerId.Length == 0 || sessionPlayerId.Length == 0)
            {
                rejectionReason = "A valid player identity is required.";
                return false;
            }

            for (int i = 0; i < players.Count; i++)
            {
                SessionPlayerState existing = players[i];
                if (existing.clientId == clientId ||
                    string.Equals(existing.playerId, playerId, StringComparison.Ordinal) ||
                    string.Equals(existing.sessionPlayerId, sessionPlayerId, StringComparison.Ordinal))
                {
                    rejectionReason = "This player is already connected.";
                    return false;
                }
            }

            players.Add(new SessionPlayerState
            {
                clientId = clientId,
                playerId = playerId,
                sessionPlayerId = sessionPlayerId,
                isHost = clientId == hostClientId,
                isReady = false
            });
            rejectionReason = string.Empty;
            return true;
        }

        public bool TrySetReady(ulong clientId, bool ready, ulong requestId)
        {
            if (State != MultiplayerSessionState.WaitingRoom || requestId == 0 ||
                lastReadyRequestByClient.TryGetValue(clientId, out ulong previous) && requestId <= previous)
            {
                return false;
            }

            SessionPlayerState player = Find(clientId);
            if (player == null)
            {
                return false;
            }

            lastReadyRequestByClient[clientId] = requestId;
            player.isReady = ready;
            return true;
        }

        public bool TryBeginMatch(ulong requesterClientId, ulong hostClientId)
        {
            if (requesterClientId != hostClientId || !CanStart)
            {
                return false;
            }

            State = MultiplayerSessionState.InMatch;
            return true;
        }

        public bool Remove(ulong clientId)
        {
            lastReadyRequestByClient.Remove(clientId);
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].clientId != clientId)
                {
                    continue;
                }

                players.RemoveAt(i);
                if (State != MultiplayerSessionState.InMatch)
                {
                    State = MultiplayerSessionState.WaitingRoom;
                }

                return true;
            }

            return false;
        }

        public SessionPlayerState Find(ulong clientId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].clientId == clientId)
                {
                    return players[i];
                }
            }

            return null;
        }

        public SessionPlayerState[] CreateSnapshot()
        {
            SessionPlayerState[] copy = new SessionPlayerState[players.Count];
            for (int i = 0; i < players.Count; i++)
            {
                copy[i] = players[i].Clone();
            }

            return copy;
        }

        public void Clear()
        {
            players.Clear();
            lastReadyRequestByClient.Clear();
            State = MultiplayerSessionState.Offline;
        }

        private static string NormalizeIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim();
            return normalized.Length <= 96 ? normalized : normalized.Substring(0, 96);
        }
    }
}
