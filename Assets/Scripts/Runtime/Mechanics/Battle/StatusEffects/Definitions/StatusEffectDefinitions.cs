using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public enum StatusPolarity : byte
    {
        Negative,
        Positive
    }

    public enum StatusReapplicationPolicy : byte
    {
        RefreshToLongerDuration,
        Ignore,
        Stack
    }

    public sealed class StatusEffectDefinition
    {
        public StatusEffectDefinition(
            string id,
            string displayName,
            StatusPolarity polarity,
            int durationExchanges,
            float maximumHitPointDamagePercent = 0f,
            int periodicDamage = 0,
            float periodicDamageProbability = 1f,
            int periodicHealing = 0,
            float outgoingDamageModifierPercent = 0f,
            float criticalChanceModifierPercent = 0f,
            float speedModifierPercent = 0f,
            float incomingDamageModifierPercent = 0f,
            float healingReceivedModifierPercent = 0f,
            float actionPreventionProbability = 0f,
            bool consumedByNextDirectHit = false,
            bool consumedByNextActionAttempt = false)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Status IDs and names are required.");
            }

            if (durationExchanges <= 0 || maximumHitPointDamagePercent < 0f ||
                periodicDamage < 0 || periodicHealing < 0 ||
                periodicDamageProbability < 0f || periodicDamageProbability > 1f ||
                actionPreventionProbability < 0f || actionPreventionProbability > 1f)
            {
                throw new ArgumentOutOfRangeException("Status values are outside their valid range.");
            }

            Id = id.Trim();
            DisplayName = displayName.Trim();
            Polarity = polarity;
            DurationExchanges = durationExchanges;
            MaximumHitPointDamagePercent = maximumHitPointDamagePercent;
            PeriodicDamage = periodicDamage;
            PeriodicDamageProbability = periodicDamageProbability;
            PeriodicHealing = periodicHealing;
            OutgoingDamageModifierPercent = outgoingDamageModifierPercent;
            CriticalChanceModifierPercent = criticalChanceModifierPercent;
            SpeedModifierPercent = speedModifierPercent;
            IncomingDamageModifierPercent = incomingDamageModifierPercent;
            HealingReceivedModifierPercent = healingReceivedModifierPercent;
            ActionPreventionProbability = actionPreventionProbability;
            ConsumedByNextDirectHit = consumedByNextDirectHit;
            ConsumedByNextActionAttempt = consumedByNextActionAttempt;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public StatusPolarity Polarity { get; }
        public int DurationExchanges { get; }
        public float MaximumHitPointDamagePercent { get; }
        public int PeriodicDamage { get; }
        public float PeriodicDamageProbability { get; }
        public int PeriodicHealing { get; }
        public float OutgoingDamageModifierPercent { get; }
        public float CriticalChanceModifierPercent { get; }
        public float SpeedModifierPercent { get; }
        public float IncomingDamageModifierPercent { get; }
        public float HealingReceivedModifierPercent { get; }
        public float ActionPreventionProbability { get; }
        public bool ConsumedByNextDirectHit { get; }
        public bool ConsumedByNextActionAttempt { get; }
    }

    public static class StatusEffectIds
    {
        public const string Lason = "lason";
        public const string Sunog = "sunog";
        public const string Sugat = "sugat";
        public const string Kuryente = "kuryente";
        public const string Basa = "basa";
        public const string Madumi = "madumi";
        public const string Lamig = "lamig";
        public const string Paghihilo = "paghihilo";
        public const string Pagsigla = "pagsigla";
        public const string Lakas = "lakas";
        public const string Talas = "talas";
        public const string Laban = "laban";
        public const string Bilis = "bilis";
    }

    public sealed class StatusEffectCatalog
    {
        private readonly Dictionary<string, StatusEffectDefinition> definitions;
        private readonly StatusEffectDefinition[] negative;
        private readonly StatusEffectDefinition[] positive;

        public StatusEffectCatalog(IEnumerable<StatusEffectDefinition> definitions)
        {
            this.definitions = new Dictionary<string, StatusEffectDefinition>(StringComparer.Ordinal);
            var negatives = new List<StatusEffectDefinition>();
            var positives = new List<StatusEffectDefinition>();
            foreach (StatusEffectDefinition definition in definitions ??
                     throw new ArgumentNullException(nameof(definitions)))
            {
                if (definition == null || !this.definitions.TryAdd(definition.Id, definition))
                {
                    throw new ArgumentException("Status definitions must be unique and non-null.");
                }

                (definition.Polarity == StatusPolarity.Negative ? negatives : positives).Add(definition);
            }

            negative = negatives.ToArray();
            positive = positives.ToArray();
        }

        public IReadOnlyDictionary<string, StatusEffectDefinition> Definitions => definitions;
        public IReadOnlyList<StatusEffectDefinition> Negative => negative;
        public IReadOnlyList<StatusEffectDefinition> Positive => positive;

        public bool TryGet(string id, out StatusEffectDefinition definition) =>
            definitions.TryGetValue(id ?? string.Empty, out definition);

        public static StatusEffectCatalog CreateDefault(int sunogDamagePerExchange = 10)
        {
            if (sunogDamagePerExchange < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sunogDamagePerExchange));
            }

            return new StatusEffectCatalog(new[]
            {
                new StatusEffectDefinition(StatusEffectIds.Lason, "Lason", StatusPolarity.Negative, 2,
                    maximumHitPointDamagePercent: 10f),
                new StatusEffectDefinition(StatusEffectIds.Sunog, "Sunog", StatusPolarity.Negative, 2,
                    periodicDamage: sunogDamagePerExchange,
                    outgoingDamageModifierPercent: -25f, speedModifierPercent: -25f),
                new StatusEffectDefinition(StatusEffectIds.Sugat, "Sugat", StatusPolarity.Negative, 2,
                    incomingDamageModifierPercent: 25f, consumedByNextDirectHit: true),
                new StatusEffectDefinition(StatusEffectIds.Kuryente, "Kuryente", StatusPolarity.Negative, 1,
                    speedModifierPercent: -50f, actionPreventionProbability: 0.25f,
                    consumedByNextActionAttempt: true),
                new StatusEffectDefinition(StatusEffectIds.Basa, "Basa", StatusPolarity.Negative, 1,
                    outgoingDamageModifierPercent: -50f, criticalChanceModifierPercent: -50f,
                    speedModifierPercent: -75f),
                new StatusEffectDefinition(StatusEffectIds.Madumi, "Madumi", StatusPolarity.Negative, 3,
                    healingReceivedModifierPercent: -50f),
                new StatusEffectDefinition(StatusEffectIds.Lamig, "Lamig", StatusPolarity.Negative, 1,
                    outgoingDamageModifierPercent: -25f, criticalChanceModifierPercent: -25f,
                    actionPreventionProbability: 0.25f),
                new StatusEffectDefinition(StatusEffectIds.Paghihilo, "Paghihilo", StatusPolarity.Negative, 5,
                    periodicDamage: 10, periodicDamageProbability: 0.5f),
                new StatusEffectDefinition(StatusEffectIds.Pagsigla, "Pagsigla", StatusPolarity.Positive, 3,
                    periodicHealing: 10),
                new StatusEffectDefinition(StatusEffectIds.Lakas, "Lakas", StatusPolarity.Positive, 2,
                    outgoingDamageModifierPercent: 25f),
                new StatusEffectDefinition(StatusEffectIds.Talas, "Talas", StatusPolarity.Positive, 2,
                    criticalChanceModifierPercent: 50f),
                new StatusEffectDefinition(StatusEffectIds.Laban, "Laban", StatusPolarity.Positive, 2,
                    incomingDamageModifierPercent: -30f),
                new StatusEffectDefinition(StatusEffectIds.Bilis, "Bilis", StatusPolarity.Positive, 2,
                    speedModifierPercent: 25f)
            });
        }
    }
}
