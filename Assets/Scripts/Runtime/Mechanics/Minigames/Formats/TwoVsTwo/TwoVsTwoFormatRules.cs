namespace ApirApir.EventDirector.Formats
{
    /// <summary>Shared team-shape rules for standard and championship 2v2 events.</summary>
    public static class TwoVsTwoFormatRules
    {
        public const int TeamSize = 2;
        public const int HumanPlayerCount = TeamSize * 2;

        public static bool IsAllowedContenderCount(int activeContenders)
        {
            return activeContenders == 4 || activeContenders == 2;
        }

        public static bool IsValidTeamShape(int teamACount, int teamBCount)
        {
            return teamACount == TeamSize && teamBCount == TeamSize;
        }

        public static bool IsValidFinalistDistribution(
            int activeContenders,
            int activeTeamA,
            int activeTeamB)
        {
            return activeContenders == 4 ||
                   (activeContenders == 2 && activeTeamA == 1 && activeTeamB == 1);
        }
    }
}
