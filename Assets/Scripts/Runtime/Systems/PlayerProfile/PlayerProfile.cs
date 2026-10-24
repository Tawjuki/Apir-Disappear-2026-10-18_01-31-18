using System;

namespace ApirApir.PlayerProfiles
{
    /// <summary>
    /// Read-only runtime representation of a player's persistent identity and lifetime statistics.
    /// Mutations are restricted to PlayerProfileService so callers cannot alter statistics directly.
    /// </summary>
    public sealed class PlayerProfile
    {
        internal PlayerProfile(
            string playerId,
            string playerName,
            string selectedCharacterId,
            int gamesPlayed,
            int gamesWon,
            int thirdPlaceFinishes)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            SelectedCharacterId = selectedCharacterId;
            GamesPlayed = gamesPlayed;
            GamesWon = gamesWon;
            ThirdPlaceFinishes = thirdPlaceFinishes;
        }

        public string PlayerName { get; private set; }
        public string PlayerId { get; }
        public string SelectedCharacterId { get; private set; }
        public int GamesPlayed { get; private set; }
        public int GamesWon { get; private set; }
        public int ThirdPlaceFinishes { get; private set; }

        internal void SetPlayerName(string playerName)
        {
            PlayerName = playerName;
        }

        internal void SetSelectedCharacter(string characterId)
        {
            SelectedCharacterId = characterId;
        }

        internal void ApplyPlacement(FinalMatchPlacement placement)
        {
            GamesPlayed++;
            if (placement == FinalMatchPlacement.First)
            {
                GamesWon++;
            }
            else if (placement == FinalMatchPlacement.Third)
            {
                ThirdPlaceFinishes++;
            }
        }

        internal PlayerProfile Copy()
        {
            return new PlayerProfile(
                PlayerId,
                PlayerName,
                SelectedCharacterId,
                GamesPlayed,
                GamesWon,
                ThirdPlaceFinishes);
        }

        public PlayerProfileSnapshot CreateSnapshot()
        {
            return new PlayerProfileSnapshot(
                PlayerId,
                PlayerName,
                SelectedCharacterId,
                GamesPlayed,
                GamesWon,
                ThirdPlaceFinishes);
        }
    }

    /// <summary>Immutable data suitable for events, persistence, and public network replication.</summary>
    public sealed class PlayerProfileSnapshot
    {
        public PlayerProfileSnapshot(
            string playerId,
            string playerName,
            string selectedCharacterId,
            int gamesPlayed,
            int gamesWon,
            int thirdPlaceFinishes)
        {
            PlayerId = playerId ?? string.Empty;
            PlayerName = playerName ?? string.Empty;
            SelectedCharacterId = selectedCharacterId ?? string.Empty;
            GamesPlayed = gamesPlayed;
            GamesWon = gamesWon;
            ThirdPlaceFinishes = thirdPlaceFinishes;
        }

        public string PlayerName { get; }
        public string PlayerId { get; }
        public string SelectedCharacterId { get; }
        public int GamesPlayed { get; }
        public int GamesWon { get; }
        public int ThirdPlaceFinishes { get; }
    }
}
