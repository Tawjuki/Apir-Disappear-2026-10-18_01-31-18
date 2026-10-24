using System;
using System.Collections.Generic;
using ApirApir.Patintero;

namespace ApirApir.EventDirector
{
    public interface IMinigameClientIdResolver
    {
        bool TryGetClientId(string persistentPlayerId, out ulong clientId);
    }

    /// <summary>
    /// Applies Event Director plans to the existing Patintero server without moving
    /// Patintero's internal role/spawn rules into the shared orchestration layer.
    /// </summary>
    public sealed class PatinteroEventAssignmentAdapter : IDisposable
    {
        private readonly AuthoritativeEventDirector director;
        private readonly PatinteroGameManager gameManager;
        private readonly IMinigameClientIdResolver clientResolver;
        private bool disposed;

        public PatinteroEventAssignmentAdapter(
            AuthoritativeEventDirector director,
            PatinteroGameManager gameManager,
            IMinigameClientIdResolver clientResolver)
        {
            this.director = director ?? throw new ArgumentNullException(nameof(director));
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            this.clientResolver = clientResolver ?? throw new ArgumentNullException(nameof(clientResolver));
            director.PlanPublished += HandlePlanPublished;
        }

        public event Action<MinigameEventPlan, string> AssignmentRejected;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            director.PlanPublished -= HandlePlanPublished;
            disposed = true;
        }

        private void HandlePlanPublished(MinigameEventPlan plan)
        {
            if (disposed || plan == null ||
                !string.Equals(plan.MinigameId, DefaultMinigameCatalog.PatinteroId,
                    StringComparison.Ordinal))
            {
                return;
            }

            var roles = new List<PatinteroNetworkRoleAssignment>(plan.Assignments.Count);
            var clientIds = new HashSet<ulong>();
            for (int i = 0; i < plan.Assignments.Count; i++)
            {
                MinigameParticipantAssignment assignment = plan.Assignments[i];
                if (!clientResolver.TryGetClientId(assignment.PlayerId, out ulong clientId) ||
                    !clientIds.Add(clientId))
                {
                    AssignmentRejected?.Invoke(plan,
                        "A participant could not be resolved to one unique connected client.");
                    return;
                }

                PatinteroRole role = assignment.Role == MinigameParticipantRole.Runner
                    ? PatinteroRole.Runner
                    : assignment.Role == MinigameParticipantRole.Guard
                        ? PatinteroRole.Taya
                        : PatinteroRole.Unassigned;
                if (role == PatinteroRole.Unassigned)
                {
                    AssignmentRejected?.Invoke(plan, "The plan contains an unsupported Patintero role.");
                    return;
                }

                roles.Add(new PatinteroNetworkRoleAssignment(clientId, role));
            }

            if (!gameManager.TryApplyAuthoritativeAssignmentOnServer(plan.EventId, roles))
            {
                AssignmentRejected?.Invoke(plan,
                    "The Patintero server rejected a duplicate, stale, or invalid assignment.");
            }
        }
    }
}
