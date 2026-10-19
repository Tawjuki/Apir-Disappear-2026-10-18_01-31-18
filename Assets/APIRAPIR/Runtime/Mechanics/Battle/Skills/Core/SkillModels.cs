using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public enum SkillTargetType : byte
    {
        Enemy,
        Self
    }

    public enum SkillSpecialRule : byte
    {
        None,
        RandomNegativeStatus,
        RandomPositiveStatus,
        CleanseOneNegative,
        SacrificeHitPoints,
        PreviousExchangeRetaliation
    }

    public sealed class SkillStatusApplication
    {
        public SkillStatusApplication(string statusId, float probability, SkillTargetType target)
        {
            if (string.IsNullOrWhiteSpace(statusId))
            {
                throw new ArgumentException("A status application requires an ID.", nameof(statusId));
            }

            if (probability < 0f || probability > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(probability));
            }

            StatusId = statusId.Trim();
            Probability = probability;
            Target = target;
        }

        public string StatusId { get; }
        public float Probability { get; }
        public SkillTargetType Target { get; }
    }

    public sealed class SkillExecutionDefinition
    {
        public SkillExecutionDefinition(
            int damagePerInstance = 0,
            int damageInstances = 0,
            int healing = 0,
            IReadOnlyList<SkillStatusApplication> statusApplications = null,
            SkillSpecialRule specialRule = SkillSpecialRule.None,
            float specialProbability = 0f,
            int specialValue = 0,
            int alternateDamage = 0)
        {
            if (damagePerInstance < 0 || damageInstances < 0 || healing < 0 ||
                specialValue < 0 || alternateDamage < 0)
            {
                throw new ArgumentOutOfRangeException("Skill amounts cannot be negative.");
            }

            if ((damagePerInstance == 0) != (damageInstances == 0))
            {
                throw new ArgumentException("Damage and instance count must both be zero or positive.");
            }

            if (specialProbability < 0f || specialProbability > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(specialProbability));
            }

            DamagePerInstance = damagePerInstance;
            DamageInstances = damageInstances;
            Healing = healing;
            StatusApplications = Copy(statusApplications);
            SpecialRule = specialRule;
            SpecialProbability = specialProbability;
            SpecialValue = specialValue;
            AlternateDamage = alternateDamage;
        }

        public int DamagePerInstance { get; }
        public int DamageInstances { get; }
        public int Healing { get; }
        public IReadOnlyList<SkillStatusApplication> StatusApplications { get; }
        public SkillSpecialRule SpecialRule { get; }
        public float SpecialProbability { get; }
        public int SpecialValue { get; }
        public int AlternateDamage { get; }

        private static IReadOnlyList<SkillStatusApplication> Copy(
            IReadOnlyList<SkillStatusApplication> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<SkillStatusApplication>();
            }

            var result = new SkillStatusApplication[source.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = source[i] ??
                    throw new ArgumentException("Status applications cannot contain null entries.");
            }

            return result;
        }
    }

    public static class SoulIds
    {
        public const string Kanayunan = "kanayunan";
        public const string Dambana = "dambana";
        public const string Katubigan = "katubigan";
        public const string Pamayanan = "pamayanan";
        public const string Aliwanan = "aliwanan";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Kanayunan, Dambana, Katubigan, Pamayanan, Aliwanan
        };
    }

    public sealed class SoulDefinition
    {
        public SoulDefinition(string id, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A Soul requires an ID and display name.");
            }

            Id = id.Trim();
            DisplayName = displayName.Trim();
        }

        public string Id { get; }
        public string DisplayName { get; }
    }

    public sealed class SoulCatalog
    {
        private readonly Dictionary<string, SoulDefinition> definitions;

        public SoulCatalog(IEnumerable<SoulDefinition> definitions)
        {
            this.definitions = new Dictionary<string, SoulDefinition>(StringComparer.Ordinal);
            foreach (SoulDefinition definition in definitions ??
                     throw new ArgumentNullException(nameof(definitions)))
            {
                if (definition == null || !this.definitions.TryAdd(definition.Id, definition))
                {
                    throw new ArgumentException("Soul definitions must be unique and non-null.");
                }
            }
        }

        public IReadOnlyDictionary<string, SoulDefinition> Definitions => definitions;
        public bool Contains(string soulId) => definitions.ContainsKey(soulId ?? string.Empty);
    }

    public static class DefaultSoulCatalog
    {
        public static SoulCatalog Create() => new SoulCatalog(new[]
        {
            new SoulDefinition(SoulIds.Kanayunan, "Kanayunan"),
            new SoulDefinition(SoulIds.Dambana, "Dambana"),
            new SoulDefinition(SoulIds.Katubigan, "Katubigan"),
            new SoulDefinition(SoulIds.Pamayanan, "Pamayanan"),
            new SoulDefinition(SoulIds.Aliwanan, "Aliwanan")
        });
    }
}
