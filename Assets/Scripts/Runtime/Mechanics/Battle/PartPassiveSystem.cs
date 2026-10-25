using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ApirApir.Battle
{
    public enum PartPassiveKind : byte
    {
        StatusImmunity,
        FlatDamageOnDirectHit,
        HealAfterNegativeStatusApplied,
        ApplyStatusAtHitPointThreshold,
        ApplyStatusAfterDirectDamageReceived,
        HealAfterCriticalHit,
        ApplyStatusAfterDodge
    }

    /// <summary>Immutable data describing one reusable card-part passive effect.</summary>
    public sealed class PartPassiveDefinition
    {
        public PartPassiveDefinition(
            string id,
            PartPassiveKind kind,
            string statusId = null,
            int amount = 0,
            float hitPointThresholdPercent = 0f,
            string requiredTargetStatusId = null,
            bool oncePerExchange = false,
            bool oncePerDuel = false)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A passive requires a stable ID.", nameof(id));
            }

            bool requiresStatus = kind == PartPassiveKind.StatusImmunity ||
                                  kind == PartPassiveKind.ApplyStatusAtHitPointThreshold ||
                                  kind == PartPassiveKind.ApplyStatusAfterDirectDamageReceived ||
                                  kind == PartPassiveKind.ApplyStatusAfterDodge;
            bool requiresAmount = kind == PartPassiveKind.FlatDamageOnDirectHit ||
                                  kind == PartPassiveKind.HealAfterNegativeStatusApplied ||
                                  kind == PartPassiveKind.HealAfterCriticalHit;
            if (requiresStatus && string.IsNullOrWhiteSpace(statusId))
            {
                throw new ArgumentException("This passive kind requires a status ID.", nameof(statusId));
            }

            if (requiresAmount && amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (kind == PartPassiveKind.ApplyStatusAtHitPointThreshold &&
                (hitPointThresholdPercent <= 0f || hitPointThresholdPercent > 100f))
            {
                throw new ArgumentOutOfRangeException(nameof(hitPointThresholdPercent));
            }

            Id = id.Trim();
            Kind = kind;
            StatusId = statusId == null ? string.Empty : statusId.Trim();
            Amount = amount;
            HitPointThresholdPercent = hitPointThresholdPercent;
            RequiredTargetStatusId = requiredTargetStatusId == null
                ? string.Empty
                : requiredTargetStatusId.Trim();
            OncePerExchange = oncePerExchange;
            OncePerDuel = oncePerDuel;
        }

        public string Id { get; }
        public PartPassiveKind Kind { get; }
        public string StatusId { get; }
        public int Amount { get; }
        public float HitPointThresholdPercent { get; }
        public string RequiredTargetStatusId { get; }
        public bool OncePerExchange { get; }
        public bool OncePerDuel { get; }
    }

    public static class PartPassiveIds
    {
        public const string MatibayNaIsip = "passive_matibay_na_isip";
        public const string HulingDepensa = "passive_huling_depensa";
        public const string BanalNaPanalangin = "passive_banal_na_panalangin";
        public const string BanalNaPanangga = "passive_banal_na_panangga";
        // Source-compatible aliases for integrations compiled against the earlier names.
        public const string Insulated = BanalNaPanalangin;
        public const string ProtektadongKatawan = BanalNaPanangga;
        public const string MahikangPaggaling = "passive_mahikang_paggaling";
        public const string MalinisNaGayuma = "passive_malinis_na_gayuma";
        public const string SumpangDagdag = "passive_sumpang_dagdag";
        public const string PanlabanSaLamig = "passive_panlaban_sa_lamig";
        public const string NgipinNgDragon = "passive_ngipin_ng_dragon";
        public const string BalatNgDragonSunog = "passive_balat_ng_dragon_sunog";
        public const string BalatNgDragonLaban = "passive_balat_ng_dragon_laban";
        public const string KaliskisNgBakunawa = "passive_kaliskis_ng_bakunawa";
        public const string PootNgBulkan = "passive_poot_ng_bulkan";
        public const string DugoNgBulkan = "passive_dugo_ng_bulkan";
        public const string Naglalagablab = "passive_naglalagablab";
        public const string MataNgBuwanImmunity = "passive_mata_ng_buwan_immunity";
        public const string MataNgBuwanHealing = "passive_mata_ng_buwan_healing";
        public const string LiwanagNgBuwan = "passive_liwanag_ng_buwan";
        public const string HakbangNgBuwan = "passive_hakbang_ng_buwan";
    }

    public sealed class PartPassiveCatalog
    {
        private readonly Dictionary<string, PartPassiveDefinition> definitions;
        private readonly IReadOnlyDictionary<string, PartPassiveDefinition> readOnlyDefinitions;

        public PartPassiveCatalog(IEnumerable<PartPassiveDefinition> values)
        {
            definitions = new Dictionary<string, PartPassiveDefinition>(StringComparer.Ordinal);
            foreach (PartPassiveDefinition value in values ?? throw new ArgumentNullException(nameof(values)))
            {
                if (value == null || !definitions.TryAdd(value.Id, value))
                {
                    throw new ArgumentException("Passive definitions must be unique and non-null.", nameof(values));
                }
            }

            readOnlyDefinitions =
                new ReadOnlyDictionary<string, PartPassiveDefinition>(definitions);
        }

        public IReadOnlyDictionary<string, PartPassiveDefinition> Definitions => readOnlyDefinitions;

        public bool TryGet(string id, out PartPassiveDefinition definition) =>
            definitions.TryGetValue(NormalizeId(id), out definition);

        private static string NormalizeId(string id)
        {
            switch (id ?? string.Empty)
            {
                case "passive_insulated": return PartPassiveIds.BanalNaPanalangin;
                case "passive_protektadong_katawan": return PartPassiveIds.BanalNaPanangga;
                default: return id ?? string.Empty;
            }
        }

        public PartPassiveDefinition GetRequired(string id)
        {
            if (!TryGet(id, out PartPassiveDefinition definition))
            {
                throw new KeyNotFoundException("Unknown card-part passive: " + id);
            }

            return definition;
        }

        public static PartPassiveCatalog CreateDefault() => new PartPassiveCatalog(new[]
        {
            new PartPassiveDefinition(PartPassiveIds.MatibayNaIsip, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Paghihilo),
            new PartPassiveDefinition(PartPassiveIds.HulingDepensa,
                PartPassiveKind.ApplyStatusAtHitPointThreshold, StatusEffectIds.Laban,
                hitPointThresholdPercent: 30f, oncePerDuel: true),
            new PartPassiveDefinition(PartPassiveIds.BanalNaPanalangin, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Kuryente),
            new PartPassiveDefinition(PartPassiveIds.BanalNaPanangga, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Sugat),
            new PartPassiveDefinition(PartPassiveIds.MahikangPaggaling,
                PartPassiveKind.HealAfterNegativeStatusApplied, amount: 5, oncePerExchange: true),
            new PartPassiveDefinition(PartPassiveIds.MalinisNaGayuma, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Madumi),
            new PartPassiveDefinition(PartPassiveIds.SumpangDagdag,
                PartPassiveKind.FlatDamageOnDirectHit, amount: 5,
                requiredTargetStatusId: "*negative", oncePerExchange: true),
            new PartPassiveDefinition(PartPassiveIds.PanlabanSaLamig, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Lamig),
            new PartPassiveDefinition(PartPassiveIds.NgipinNgDragon,
                PartPassiveKind.FlatDamageOnDirectHit, amount: 10, oncePerExchange: true),
            new PartPassiveDefinition(PartPassiveIds.BalatNgDragonSunog, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Sunog),
            new PartPassiveDefinition(PartPassiveIds.BalatNgDragonLaban,
                PartPassiveKind.ApplyStatusAtHitPointThreshold, StatusEffectIds.Laban,
                hitPointThresholdPercent: 30f, oncePerDuel: true),
            new PartPassiveDefinition(PartPassiveIds.KaliskisNgBakunawa, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Basa),
            new PartPassiveDefinition(PartPassiveIds.PootNgBulkan,
                PartPassiveKind.FlatDamageOnDirectHit, amount: 10,
                requiredTargetStatusId: StatusEffectIds.Sunog, oncePerExchange: true),
            new PartPassiveDefinition(PartPassiveIds.DugoNgBulkan, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Lason),
            new PartPassiveDefinition(PartPassiveIds.Naglalagablab,
                PartPassiveKind.ApplyStatusAfterDirectDamageReceived, StatusEffectIds.Lakas,
                oncePerDuel: true),
            new PartPassiveDefinition(PartPassiveIds.MataNgBuwanImmunity, PartPassiveKind.StatusImmunity,
                StatusEffectIds.Paghihilo),
            new PartPassiveDefinition(PartPassiveIds.MataNgBuwanHealing,
                PartPassiveKind.HealAfterCriticalHit, amount: 5, oncePerExchange: true),
            new PartPassiveDefinition(PartPassiveIds.LiwanagNgBuwan,
                PartPassiveKind.ApplyStatusAtHitPointThreshold, StatusEffectIds.Pagsigla,
                hitPointThresholdPercent: 30f, oncePerDuel: true),
            new PartPassiveDefinition(PartPassiveIds.HakbangNgBuwan,
                PartPassiveKind.ApplyStatusAfterDodge, StatusEffectIds.Bilis, oncePerDuel: true)
        });
    }

    /// <summary>
    /// Evaluates immutable passive definitions against per-fighter battle state. This object
    /// carries no shared mutable definition state and is owned by one authoritative battle.
    /// </summary>
    public sealed class PartPassiveRuntimeSystem
    {
        private readonly StatusEffectCatalog statuses;

        public PartPassiveRuntimeSystem(StatusEffectCatalog statuses)
        {
            this.statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
        }

        public bool IsImmune(CardCombatState target, string statusId)
        {
            if (target == null || string.IsNullOrEmpty(statusId))
            {
                return false;
            }

            for (int i = 0; i < target.Parts.Count; i++)
            {
                PartCombatState part = target.Parts[i];
                if (!part.IsFunctional)
                {
                    continue;
                }

                for (int j = 0; j < part.Definition.PassiveAbilities.Count; j++)
                {
                    PartPassiveDefinition passive = part.Definition.PassiveAbilities[j];
                    if (passive.Kind == PartPassiveKind.StatusImmunity &&
                        string.Equals(passive.StatusId, statusId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public int PrepareFlatDamageBonus(CardCombatState source, CardCombatState target)
        {
            source.PendingDirectHitPassives.Clear();
            int result = 0;
            for (int i = 0; i < source.Parts.Count; i++)
            {
                PartCombatState part = source.Parts[i];
                if (!part.IsFunctional)
                {
                    continue;
                }

                for (int j = 0; j < part.Definition.PassiveAbilities.Count; j++)
                {
                    PartPassiveDefinition passive = part.Definition.PassiveAbilities[j];
                    if (passive.Kind != PartPassiveKind.FlatDamageOnDirectHit ||
                        !CanTrigger(source, part, passive) || !TargetConditionMatches(target, passive))
                    {
                        continue;
                    }

                    result += passive.Amount;
                    source.PendingDirectHitPassives.Add(new PendingPassiveTrigger(
                        CreateUsageKey(part, passive), passive.OncePerExchange, passive.OncePerDuel));
                }
            }

            return result;
        }

        public void CompleteFlatDamage(CardCombatState source, bool successfulHit)
        {
            if (successfulHit)
            {
                for (int i = 0; i < source.PendingDirectHitPassives.Count; i++)
                {
                    PendingPassiveTrigger trigger = source.PendingDirectHitPassives[i];
                    source.MarkPassiveTriggered(
                        trigger.Key, trigger.OncePerExchange, trigger.OncePerDuel);
                }
            }

            source.PendingDirectHitPassives.Clear();
        }

        public int ConsumeNegativeStatusHealing(CardCombatState source)
        {
            return ConsumeHealing(source, PartPassiveKind.HealAfterNegativeStatusApplied);
        }

        public int ConsumeCriticalHealing(CardCombatState source)
        {
            return ConsumeHealing(source, PartPassiveKind.HealAfterCriticalHit);
        }

        public bool TryConsumeHitPointThreshold(CardCombatState target, out string statusId)
        {
            statusId = string.Empty;
            if (target == null || target.IsDefeated)
            {
                return false;
            }

            decimal percentage = target.MaximumHitPoints <= 0
                ? 100m
                : (decimal)target.CurrentHitPoints * 100m / target.MaximumHitPoints;
            for (int i = 0; i < target.Parts.Count; i++)
            {
                PartCombatState part = target.Parts[i];
                if (!part.IsFunctional)
                {
                    continue;
                }

                for (int j = 0; j < part.Definition.PassiveAbilities.Count; j++)
                {
                    PartPassiveDefinition passive = part.Definition.PassiveAbilities[j];
                    if (passive.Kind == PartPassiveKind.ApplyStatusAtHitPointThreshold &&
                        percentage <= (decimal)passive.HitPointThresholdPercent &&
                        CanTrigger(target, part, passive))
                    {
                        Consume(target, part, passive);
                        statusId = passive.StatusId;
                        return true;
                    }
                }
            }

            return false;
        }

        public bool TryConsumeDirectDamageStatus(CardCombatState target, out string statusId)
        {
            return TryConsumeStatus(
                target, PartPassiveKind.ApplyStatusAfterDirectDamageReceived, out statusId);
        }

        public bool TryConsumeDodgeStatus(CardCombatState target, out string statusId)
        {
            return TryConsumeStatus(target, PartPassiveKind.ApplyStatusAfterDodge, out statusId);
        }

        private int ConsumeHealing(CardCombatState source, PartPassiveKind kind)
        {
            int result = 0;
            for (int i = 0; i < source.Parts.Count; i++)
            {
                PartCombatState part = source.Parts[i];
                if (!part.IsFunctional)
                {
                    continue;
                }

                for (int j = 0; j < part.Definition.PassiveAbilities.Count; j++)
                {
                    PartPassiveDefinition passive = part.Definition.PassiveAbilities[j];
                    if (passive.Kind == kind && CanTrigger(source, part, passive))
                    {
                        Consume(source, part, passive);
                        result += passive.Amount;
                    }
                }
            }

            return result;
        }

        private bool TryConsumeStatus(
            CardCombatState target,
            PartPassiveKind kind,
            out string statusId)
        {
            statusId = string.Empty;
            if (target == null || target.IsDefeated)
            {
                return false;
            }

            for (int i = 0; i < target.Parts.Count; i++)
            {
                PartCombatState part = target.Parts[i];
                if (!part.IsFunctional)
                {
                    continue;
                }

                for (int j = 0; j < part.Definition.PassiveAbilities.Count; j++)
                {
                    PartPassiveDefinition passive = part.Definition.PassiveAbilities[j];
                    if (passive.Kind == kind && CanTrigger(target, part, passive))
                    {
                        Consume(target, part, passive);
                        statusId = passive.StatusId;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TargetConditionMatches(CardCombatState target, PartPassiveDefinition passive)
        {
            if (string.IsNullOrEmpty(passive.RequiredTargetStatusId))
            {
                return true;
            }

            if (!string.Equals(passive.RequiredTargetStatusId, "*negative", StringComparison.Ordinal))
            {
                return target.HasStatus(passive.RequiredTargetStatusId);
            }

            for (int i = 0; i < statuses.Negative.Count; i++)
            {
                if (target.HasStatus(statuses.Negative[i].Id))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanTrigger(
            CardCombatState owner,
            PartCombatState part,
            PartPassiveDefinition passive)
        {
            PassiveUsageKey key = CreateUsageKey(part, passive);
            return (!passive.OncePerExchange || !owner.WasPassiveTriggeredThisExchange(key)) &&
                   (!passive.OncePerDuel || !owner.WasPassiveTriggeredThisDuel(key));
        }

        private static void Consume(
            CardCombatState owner,
            PartCombatState part,
            PartPassiveDefinition passive)
        {
            owner.MarkPassiveTriggered(
                CreateUsageKey(part, passive),
                passive.OncePerExchange,
                passive.OncePerDuel);
        }

        private static PassiveUsageKey CreateUsageKey(
            PartCombatState part,
            PartPassiveDefinition passive) => new PassiveUsageKey(part, passive);

    }
}
