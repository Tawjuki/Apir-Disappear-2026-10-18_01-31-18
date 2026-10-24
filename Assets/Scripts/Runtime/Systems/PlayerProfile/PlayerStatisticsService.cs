using System;
using System.Collections.Generic;

namespace ApirApir.PlayerProfiles
{
    /// <summary>
    /// Consumes only authoritative, completed four-player match placements. Individual
    /// battle outcomes are intentionally outside this service and cannot award lifetime stats.
    /// </summary>
    public sealed class PlayerStatisticsService : IDisposable
    {
        private readonly PlayerProfileService profiles;
        private readonly IPlayerProfileAuthority authority;
        private ICompletedMatchResultSource source;

        public PlayerStatisticsService(
            PlayerProfileService profiles,
            IPlayerProfileAuthority authority)
        {
            this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public event Action<CompletedMatchResult, StatisticsProcessStatus> MatchResultProcessed;

        public void Attach(ICompletedMatchResultSource matchResultSource)
        {
            if (ReferenceEquals(source, matchResultSource))
            {
                return;
            }

            Detach();
            source = matchResultSource ?? throw new ArgumentNullException(nameof(matchResultSource));
            source.MatchCompleted += OnMatchCompleted;
        }

        public void Detach()
        {
            if (source == null)
            {
                return;
            }

            source.MatchCompleted -= OnMatchCompleted;
            source = null;
        }

        public StatisticsProcessStatus ProcessCompletedMatch(CompletedMatchResult result)
        {
            StatisticsProcessStatus status;
            if (!authority.IsAuthoritative)
            {
                status = StatisticsProcessStatus.NotAuthoritative;
            }
            else if (result == null || !result.IsCompleted)
            {
                status = StatisticsProcessStatus.IncompleteMatch;
            }
            else if (!CompletedMatchPolicy.TryValidate(
                         result, out IReadOnlyList<PlayerFinalPlacement> placements))
            {
                status = StatisticsProcessStatus.InvalidResult;
            }
            else if (profiles.HasProcessedMatch(result.MatchId))
            {
                status = StatisticsProcessStatus.DuplicateMatch;
            }
            else
            {
                status = profiles.TryCommitCompletedMatch(result.MatchId.Trim(), placements);
            }

            MatchResultProcessed?.Invoke(result, status);
            return status;
        }

        public void Dispose()
        {
            Detach();
        }

        private void OnMatchCompleted(CompletedMatchResult result)
        {
            ProcessCompletedMatch(result);
        }

    }
}
