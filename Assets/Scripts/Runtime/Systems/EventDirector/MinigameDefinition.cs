using System;
using System.Collections.Generic;
using ApirApir.MatchFlow;

namespace ApirApir.EventDirector
{
    public interface IMinigameAssignmentPolicy
    {
        bool TryCreateAssignments(
            MatchFlowSnapshot match,
            MinigameFormat format,
            int randomSeed,
            out IReadOnlyList<MinigameParticipantAssignment> assignments);
    }

    public interface IEventDirectorRandom
    {
        int Next(int exclusiveMaximum);
    }

    public sealed class SystemEventDirectorRandom : IEventDirectorRandom
    {
        private readonly Random random;

        public SystemEventDirectorRandom(int? seed = null)
        {
            random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            }

            return random.Next(exclusiveMaximum);
        }
    }

    public sealed class MinigameDefinition
    {
        private readonly List<MinigameEligibilityDeclaration> eligibility;

        public MinigameDefinition(
            string minigameId,
            IEnumerable<MinigameEligibilityDeclaration> declarations,
            IMinigameAssignmentPolicy assignmentPolicy)
        {
            MinigameId = string.IsNullOrWhiteSpace(minigameId)
                ? throw new ArgumentException("A minigame ID is required.", nameof(minigameId))
                : minigameId.Trim();
            AssignmentPolicy = assignmentPolicy ?? throw new ArgumentNullException(nameof(assignmentPolicy));
            eligibility = declarations == null
                ? throw new ArgumentNullException(nameof(declarations))
                : new List<MinigameEligibilityDeclaration>(declarations);
            if (eligibility.Count == 0)
            {
                throw new ArgumentException("At least one supported format is required.", nameof(declarations));
            }

            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < eligibility.Count; i++)
            {
                MinigameEligibilityDeclaration declaration = eligibility[i];
                if (declaration.ActiveContenders < 2 || declaration.ActiveContenders > 4 ||
                    !Enum.IsDefined(typeof(MinigameFormat), declaration.Format) ||
                    !unique.Add(declaration.ActiveContenders + ":" + (byte)declaration.Format))
                {
                    throw new ArgumentException(
                        "Minigame format declarations must be valid and unique.",
                        nameof(declarations));
                }
            }
        }

        public string MinigameId { get; }
        public IMinigameAssignmentPolicy AssignmentPolicy { get; }
        public IReadOnlyList<MinigameEligibilityDeclaration> Eligibility => eligibility;

        public bool Supports(int activeContenders, MinigameFormat format)
        {
            for (int i = 0; i < eligibility.Count; i++)
            {
                if (eligibility[i].ActiveContenders == activeContenders &&
                    eligibility[i].Format == format)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
