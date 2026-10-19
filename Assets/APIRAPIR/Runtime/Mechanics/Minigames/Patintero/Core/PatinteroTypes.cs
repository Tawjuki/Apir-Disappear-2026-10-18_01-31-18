namespace ApirApir.Patintero
{
    public readonly struct PatinteroAssignmentEntry
    {
        public PatinteroAssignmentEntry(string playerId, PatinteroRole role, byte team)
        {
            PlayerId = playerId ?? string.Empty;
            Role = role;
            Team = team;
        }

        public string PlayerId { get; }
        public PatinteroRole Role { get; }
        public byte Team { get; }
    }

    public readonly struct PatinteroNetworkRoleAssignment
    {
        public PatinteroNetworkRoleAssignment(ulong clientId, PatinteroRole role)
        {
            ClientId = clientId;
            Role = role;
        }

        public ulong ClientId { get; }
        public PatinteroRole Role { get; }
    }

    public enum PatinteroRole : byte
    {
        Unassigned,
        Runner,
        Taya
    }

    public enum PatinteroRoundState : byte
    {
        WaitingForPlayers,
        Playing,
        Finished
    }

    public enum PatinteroResult : byte
    {
        None,
        RunnersWin,
        TayaWins
    }

    public enum RunnerProgress : byte
    {
        Outbound,
        Returning,
        Completed,
        Caught
    }
}
