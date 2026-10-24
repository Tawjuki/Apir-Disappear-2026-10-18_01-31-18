using System;

namespace ApirApir.EventDirector.Formats
{
    /// <summary>
    /// Shared participation shape for the two approved 1v3 variants. Gameplay and role
    /// selection remain owned by the selected minigame.
    /// </summary>
    public static class OneVsThreeFormatRules
    {
        public const int MajoritySize = 3;
        public const int SoloSize = 1;
        public const int HumanPlayerCount = MajoritySize + SoloSize;

        public static bool IsAllowedContenderCount(int activeContenders)
        {
            return activeContenders == 4 || activeContenders == 3;
        }

        public static bool IsValidTeamShape(int teamACount, int teamBCount)
        {
            return teamACount == MajoritySize && teamBCount == SoloSize;
        }

        public static bool IsValidThreeContenderMembership(
            int activeTeamA,
            int activeTeamB)
        {
            return activeTeamA == MajoritySize && activeTeamB == 0;
        }
    }
}
