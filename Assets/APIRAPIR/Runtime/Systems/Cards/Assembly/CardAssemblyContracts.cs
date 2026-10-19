using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.MatchFlow;

namespace ApirApir.Cards
{
    public interface ICardAssemblyAuthority
    {
        bool IsAuthoritative { get; }
    }

    public sealed class FixedCardAssemblyAuthority : ICardAssemblyAuthority
    {
        public FixedCardAssemblyAuthority(bool isAuthoritative)
        {
            IsAuthoritative = isAuthoritative;
        }

        public bool IsAuthoritative { get; }
    }

    public interface ICardAssemblyAccessPolicy
    {
        bool CanModifyEquipment(string playerId);
    }

    public sealed class FixedCardAssemblyAccessPolicy : ICardAssemblyAccessPolicy
    {
        private readonly bool allowed;

        public FixedCardAssemblyAccessPolicy(bool allowed)
        {
            this.allowed = allowed;
        }

        public bool CanModifyEquipment(string playerId) => allowed;
    }

    /// <summary>Uses the existing final Preparation window; no second phase manager is created.</summary>
    public sealed class MatchFlowCardAssemblyAccessPolicy : ICardAssemblyAccessPolicy
    {
        private readonly AuthoritativeMatchFlow matchFlow;

        public MatchFlowCardAssemblyAccessPolicy(AuthoritativeMatchFlow matchFlow)
        {
            this.matchFlow = matchFlow ?? throw new ArgumentNullException(nameof(matchFlow));
        }

        public bool CanModifyEquipment(string playerId)
        {
            return !string.IsNullOrWhiteSpace(playerId) &&
                   matchFlow.Phase == MatchPhase.FinalPreparation;
        }
    }

    /// <summary>
    /// Optional bridge to the established player skill loadout. Assembly reads it only when a
    /// battle loadout is requested and never owns Souls or skills.
    /// </summary>
    public interface ICardSkillLoadoutSource
    {
        bool TryGetSkills(string playerId, out IReadOnlyList<SkillDefinition> skills);
    }

    public sealed class EmptyCardSkillLoadoutSource : ICardSkillLoadoutSource
    {
        public bool TryGetSkills(string playerId, out IReadOnlyList<SkillDefinition> skills)
        {
            skills = Array.Empty<SkillDefinition>();
            return !string.IsNullOrWhiteSpace(playerId);
        }
    }
}
