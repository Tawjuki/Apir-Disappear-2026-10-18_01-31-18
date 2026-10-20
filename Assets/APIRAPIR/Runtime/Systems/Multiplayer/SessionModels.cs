using System;

namespace ApirApir.Multiplayer
{
    public enum MultiplayerConnectionMode
    {
        Offline,
        Lan,
        OnlineRelay
    }

    public enum MultiplayerSessionState
    {
        Offline,
        Connecting,
        WaitingRoom,
        InMatch,
        Disconnecting
    }

    [Serializable]
    public sealed class SessionPlayerState
    {
        public ulong clientId;
        public string playerId = string.Empty;
        public string sessionPlayerId = string.Empty;
        public bool isHost;
        public bool isReady;

        public SessionPlayerState Clone()
        {
            return new SessionPlayerState
            {
                clientId = clientId,
                playerId = playerId,
                sessionPlayerId = sessionPlayerId,
                isHost = isHost,
                isReady = isReady
            };
        }
    }

    [Serializable]
    internal sealed class SessionSnapshot
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public long revision;
        public MultiplayerSessionState state;
        public SessionPlayerState[] players = Array.Empty<SessionPlayerState>();
    }

    [Serializable]
    internal sealed class ConnectionIdentity
    {
        public const int CurrentProtocol = 1;

        public int protocol = CurrentProtocol;
        public string playerId = string.Empty;
        public string sessionPlayerId = string.Empty;
    }
}
