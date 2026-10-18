namespace ApirApir.Patintero
{
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
