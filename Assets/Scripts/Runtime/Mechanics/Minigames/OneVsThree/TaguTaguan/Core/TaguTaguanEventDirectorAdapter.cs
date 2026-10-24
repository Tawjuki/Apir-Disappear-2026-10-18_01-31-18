using System;
using System.Collections.Generic;
using ApirApir.EventDirector;

namespace ApirApir.TaguTaguan
{
    /// <summary>Reports the authoritative Tagu-taguan outcome to the existing Event Director.</summary>
    public sealed class TaguTaguanEventDirectorAdapter : IDisposable
    {
        private readonly AuthoritativeEventDirector director;
        private readonly TaguTaguanController controller;
        private bool disposed;

        public TaguTaguanEventDirectorAdapter(
            AuthoritativeEventDirector director,
            TaguTaguanController controller)
        {
            this.director = director ?? throw new ArgumentNullException(nameof(director));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            director.PlanPublished += HandlePlanPublished;
            controller.ResultsPublished += HandleResultsPublished;
        }

        public event Action<MinigameEventPlan, TaguTaguanFailure> InitializationRejected;
        public event Action<TaguTaguanResult, EventDirectorFailure> ResultRejected;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            director.PlanPublished -= HandlePlanPublished;
            controller.ResultsPublished -= HandleResultsPublished;
            disposed = true;
        }

        private void HandlePlanPublished(MinigameEventPlan plan)
        {
            if (disposed || plan == null ||
                !string.Equals(plan.MinigameId, DefaultMinigameCatalog.TaguTaguanId,
                    StringComparison.Ordinal))
            {
                return;
            }

            TaguTaguanOperationResult result = controller.TryInitialize(plan);
            if (!result.Succeeded)
            {
                InitializationRejected?.Invoke(plan, result.Failure);
            }
        }

        private void HandleResultsPublished(TaguTaguanResult result)
        {
            if (disposed || result == null || result.Reason == TaguTaguanCompletionReason.Cancelled ||
                controller.Plan == null)
            {
                return;
            }

            var individual = new List<MinigameIndividualResult>(4);
            for (int i = 0; i < controller.Plan.Assignments.Count; i++)
            {
                MinigameParticipantAssignment assignment = controller.Plan.Assignments[i];
                bool succeeded = assignment.Role == MinigameParticipantRole.Taya
                    ? result.TayaSucceeded
                    : Contains(result.SurvivingHiderIds, assignment.PlayerId);
                individual.Add(new MinigameIndividualResult(assignment.PlayerId, succeeded, 0));
            }

            MinigameTeam winner = result.TayaSucceeded ? MinigameTeam.TeamB : MinigameTeam.TeamA;
            MinigameTeam loser = result.TayaSucceeded ? MinigameTeam.TeamA : MinigameTeam.TeamB;
            EventDirectorOperationResult completion = director.TryCompleteCurrentPlan(
                result.EventId,
                result.EventSequence,
                winner,
                loser,
                individual.AsReadOnly(),
                out _);
            if (!completion.Succeeded)
            {
                ResultRejected?.Invoke(result, completion.Failure);
            }
        }

        private static bool Contains(IReadOnlyList<string> values, string playerId)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], playerId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
