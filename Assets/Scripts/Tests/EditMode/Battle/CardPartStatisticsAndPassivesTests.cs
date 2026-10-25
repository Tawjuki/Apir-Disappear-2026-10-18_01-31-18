using System;
using System.Collections.Generic;
using System.Linq;
using ApirApir.Crafting;
using NUnit.Framework;

namespace ApirApir.Battle.Tests
{
    public sealed class CardPartStatisticsAndPassivesTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Test]
        public void DefaultCatalog_RegistersExactRarityCountsAndResolvablePassives()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            Assert.That(catalog.Parts.Count, Is.EqualTo(33));
            Assert.That(catalog.Parts.Values.Count(value => value.Rarity == PartRarity.Common), Is.EqualTo(12));
            Assert.That(catalog.Parts.Values.Count(value => value.Rarity == PartRarity.Rare), Is.EqualTo(12));
            Assert.That(catalog.Parts.Values.Count(value => value.Rarity == PartRarity.Epic), Is.EqualTo(9));
            Assert.That(catalog.Parts.Values.Where(value => value.Rarity == PartRarity.Common)
                .All(value => value.PassiveAbilityReferences.Count == 0), Is.True);

            foreach (CraftablePartDefinition part in catalog.Parts.Values)
            {
                for (int i = 0; i < part.PassiveAbilityReferences.Count; i++)
                {
                    Assert.That(DefaultPartDefinitionCatalog.Passives.TryGet(
                        part.PassiveAbilityReferences[i], out _), Is.True, part.Id);
                }
            }
        }

        [Test]
        public void LegacyRarePulisPassiveIds_ResolveToRenamedPariPassives()
        {
            PartPassiveCatalog catalog = DefaultPartDefinitionCatalog.Passives;

            Assert.That(catalog.GetRequired("passive_insulated").Id,
                Is.EqualTo(PartPassiveIds.BanalNaPanalangin));
            Assert.That(catalog.GetRequired("passive_protektadong_katawan").Id,
                Is.EqualTo(PartPassiveIds.BanalNaPanangga));
        }

        [TestCase("part_common_magsasaka_head", PartSlot.Head, PartRarity.Common, 15, 12, 0, 0)]
        [TestCase("part_common_magsasaka_body", PartSlot.Body, PartRarity.Common, 25, 13, 0, 0)]
        [TestCase("part_common_magsasaka_legs", PartSlot.Legs, PartRarity.Common, 12, 10, 5, 0)]
        [TestCase("part_common_mangangahoy_head", PartSlot.Head, PartRarity.Common, 18, 16, 0, 0)]
        [TestCase("part_common_mangangahoy_body", PartSlot.Body, PartRarity.Common, 28, 15, 0, 0)]
        [TestCase("part_common_mangangahoy_legs", PartSlot.Legs, PartRarity.Common, 14, 12, 5, 0)]
        [TestCase("part_common_pulis_head", PartSlot.Head, PartRarity.Common, 18, 14, 0, 5)]
        [TestCase("part_common_pulis_body", PartSlot.Body, PartRarity.Common, 26, 14, 0, 0)]
        [TestCase("part_common_pulis_legs", PartSlot.Legs, PartRarity.Common, 13, 11, 5, 0)]
        [TestCase("part_common_mangingisda_head", PartSlot.Head, PartRarity.Common, 16, 12, 0, 5)]
        [TestCase("part_common_mangingisda_body", PartSlot.Body, PartRarity.Common, 23, 13, 0, 0)]
        [TestCase("part_common_mangingisda_legs", PartSlot.Legs, PartRarity.Common, 15, 11, 10, 0)]
        [TestCase("part_rare_abogado_head", PartSlot.Head, PartRarity.Rare, 30, 25, 0, 15)]
        [TestCase("part_rare_abogado_body", PartSlot.Body, PartRarity.Rare, 42, 23, 0, 0)]
        [TestCase("part_rare_abogado_legs", PartSlot.Legs, PartRarity.Rare, 26, 19, 5, 0)]
        [TestCase("part_rare_pari_head", PartSlot.Head, PartRarity.Rare, 35, 29, 0, 10)]
        [TestCase("part_rare_pari_body", PartSlot.Body, PartRarity.Rare, 45, 26, 0, 0)]
        [TestCase("part_rare_pari_legs", PartSlot.Legs, PartRarity.Rare, 28, 22, 10, 0)]
        [TestCase("part_rare_manggagayuma_head", PartSlot.Head, PartRarity.Rare, 28, 25, 0, 10)]
        [TestCase("part_rare_manggagayuma_body", PartSlot.Body, PartRarity.Rare, 38, 22, 0, 0)]
        [TestCase("part_rare_manggagayuma_legs", PartSlot.Legs, PartRarity.Rare, 27, 19, 10, 0)]
        [TestCase("part_rare_mangkukulam_head", PartSlot.Head, PartRarity.Rare, 32, 27, 0, 10)]
        [TestCase("part_rare_mangkukulam_body", PartSlot.Body, PartRarity.Rare, 40, 25, 0, 0)]
        [TestCase("part_rare_mangkukulam_legs", PartSlot.Legs, PartRarity.Rare, 29, 21, 5, 5)]
        [TestCase("part_epic_bakunawa_head", PartSlot.Head, PartRarity.Epic, 70, 50, 0, 10)]
        [TestCase("part_epic_bakunawa_body", PartSlot.Body, PartRarity.Epic, 65, 43, 0, 0)]
        [TestCase("part_epic_bakunawa_legs", PartSlot.Legs, PartRarity.Epic, 50, 31, 15, 0)]
        [TestCase("part_epic_lalahon_head", PartSlot.Head, PartRarity.Epic, 65, 48, 0, 10)]
        [TestCase("part_epic_lalahon_body", PartSlot.Body, PartRarity.Epic, 70, 42, 0, 0)]
        [TestCase("part_epic_lalahon_legs", PartSlot.Legs, PartRarity.Epic, 52, 31, 10, 5)]
        [TestCase("part_epic_mayari_head", PartSlot.Head, PartRarity.Epic, 60, 46, 0, 25)]
        [TestCase("part_epic_mayari_body", PartSlot.Body, PartRarity.Epic, 62, 40, 0, 10)]
        [TestCase("part_epic_mayari_legs", PartSlot.Legs, PartRarity.Epic, 55, 29, 20, 10)]
        public void DefaultPart_HasAuthoritativeStatistics(
            string id, PartSlot slot, PartRarity rarity,
            float hp, float attack, float speed, float critical)
        {
            PartDefinition part = DefaultPartDefinitionCatalog.GetRequired(id);

            Assert.That(part.Slot, Is.EqualTo(slot));
            Assert.That(part.Rarity, Is.EqualTo(rarity));
            Assert.That(part.AdditionalHitPoints, Is.Zero);
            Assert.That(part.HitPointBonusPercent, Is.EqualTo(hp));
            Assert.That(part.AttackBonusPercent, Is.EqualTo(attack));
            Assert.That(part.SpeedBonusPercent, Is.EqualTo(speed));
            Assert.That(part.CriticalChanceBonusPercent, Is.EqualTo(critical));
        }

        [TestCase("common_magsasaka", 152, 35)]
        [TestCase("common_mangangahoy", 160, 43)]
        [TestCase("common_pulis", 157, 39)]
        [TestCase("common_mangingisda", 154, 36)]
        [TestCase("rare_abogado", 198, 67)]
        [TestCase("rare_pari", 208, 77)]
        [TestCase("rare_manggagayuma", 193, 66)]
        [TestCase("rare_mangkukulam", 201, 73)]
        [TestCase("epic_bakunawa", 285, 124)]
        [TestCase("epic_lalahon", 287, 121)]
        [TestCase("epic_mayari", 277, 115)]
        public void CompleteCharacter_AssemblyAndBattleStatsMatch(
            string characterId, int expectedHp, float expectedAttack)
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();
            EquippedPart[] equipped = CreateEquippedSet(catalog, characterId);
            CardBattleLoadout loadout = new CardBattleLoadout(
                "p1", "card-p1", equipped,
                new[] { Damage("hit", 1) });
            BattleController battle = CreateBattle(loadout, Blank("p2"));

            Assert.That(battle.State.Fighters["p1"].MaximumHitPoints, Is.EqualTo(expectedHp));
            Assert.That(new CardStatCalculator().CalculateAttackBonusPercent(
                battle.State.Fighters["p1"]), Is.EqualTo(expectedAttack));
        }

        [Test]
        public void MixedBuild_UsesAdditiveNonCompoundingPercentages()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();
            EquippedPart[] parts =
            {
                new EquippedPart(catalog.Parts["part_epic_bakunawa_head"].BattleDefinition),
                new EquippedPart(catalog.Parts["part_rare_abogado_body"].BattleDefinition),
                new EquippedPart(catalog.Parts["part_common_mangingisda_legs"].BattleDefinition)
            };
            BattleController battle = CreateBattle(Loadout("p1", parts, Damage("hit", 1)), Blank("p2"));
            CardCombatState fighter = battle.State.Fighters["p1"];
            var modifiers = new CombatModifierCalculator(StatusEffectCatalog.CreateDefault(), new BattleSettings());

            Assert.That(fighter.MaximumHitPoints, Is.EqualTo(227));
            Assert.That(new CardStatCalculator().CalculateAttackBonusPercent(fighter), Is.EqualTo(84f));
            Assert.That(modifiers.GetSpeedModifierPercent(fighter), Is.EqualTo(10f));
            Assert.That(modifiers.GetCriticalChance(fighter), Is.EqualTo(0.11f).Within(0.0001f));
        }

        [Test]
        public void DefaultCombatProbabilities_AreTenPercentAndDodgeIsCapped()
        {
            var settings = new BattleSettings();
            Assert.That(settings.BaseDodgeChance, Is.EqualTo(0.10f));
            Assert.That(settings.MaximumDodgeChance, Is.EqualTo(0.30f));
            Assert.That(settings.BaseCriticalChance, Is.EqualTo(0.10f));
        }

        [TestCase("part_rare_abogado_head", StatusEffectIds.Paghihilo)]
        [TestCase("part_rare_pari_head", StatusEffectIds.Kuryente)]
        [TestCase("part_rare_pari_body", StatusEffectIds.Sugat)]
        [TestCase("part_rare_manggagayuma_body", StatusEffectIds.Madumi)]
        [TestCase("part_rare_mangkukulam_body", StatusEffectIds.Lamig)]
        [TestCase("part_epic_bakunawa_body", StatusEffectIds.Sunog)]
        [TestCase("part_epic_bakunawa_legs", StatusEffectIds.Basa)]
        [TestCase("part_epic_lalahon_body", StatusEffectIds.Lason)]
        [TestCase("part_epic_mayari_head", StatusEffectIds.Paghihilo)]
        public void FunctionalImmunity_BlocksStatusButNotDirectDamage(string partId, string statusId)
        {
            SkillDefinition skill = DamageWithStatus("attack", 10, statusId);
            BattleController battle = CreateBattle(
                Loadout("p1", null, skill),
                Loadout("p2", new[] { new EquippedPart(DefaultPartDefinitionCatalog.GetRequired(partId)) }, skill));
            int startingHp = battle.State.Fighters["p2"].MaximumHitPoints;
            battle.Start(Now);

            Submit(battle, "p1", "one", skill.Id);
            Pass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.LessThan(startingHp));
            Assert.That(battle.State.Fighters["p2"].HasStatus(statusId), Is.False);
        }

        [Test]
        public void BrokenPart_StopsGrantingImmunity()
        {
            SkillDefinition skill = DamageWithStatus("attack", 10, StatusEffectIds.Kuryente);
            PartDefinition definition = DefaultPartDefinitionCatalog.GetRequired("part_rare_pari_head");
            BattleController battle = CreateBattle(
                Loadout("p1", null, skill),
                Loadout("p2", new[] { new EquippedPart(definition, 0) }, skill));
            battle.Start(Now);

            Submit(battle, "p1", "one", skill.Id);
            Pass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].HasStatus(StatusEffectIds.Kuryente), Is.True);
        }

        [Test]
        public void BakunawaFlatDamage_TriggersOncePerExchangeAndDoesNotScaleWithAttack()
        {
            SkillDefinition multi = Damage("multi", 10, 2);
            EquippedPart head = new EquippedPart(
                DefaultPartDefinitionCatalog.GetRequired("part_epic_bakunawa_head"));
            BattleController battle = CreateBattle(
                Loadout("p1", new[] { head }, multi),
                Loadout("p2", null, multi));
            battle.Start(Now);

            Submit(battle, "p1", "one", multi.Id);
            Pass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(60));
        }

        [Test]
        public void ThresholdAndDodgePassives_ApplyCentralStatusesAfterQualifyingEvents()
        {
            SkillDefinition heavy = Damage("heavy", 120);
            EquippedPart body = new EquippedPart(
                DefaultPartDefinitionCatalog.GetRequired("part_epic_mayari_body"));
            BattleController threshold = CreateBattle(
                Loadout("p1", null, heavy), Loadout("p2", new[] { body }, heavy));
            threshold.Start(Now);
            Submit(threshold, "p1", "one", heavy.Id);
            Pass(threshold, "p2", "two");
            Assert.That(threshold.State.Fighters["p2"].HasStatus(StatusEffectIds.Pagsigla), Is.True);

            var dodgeSettings = NoVariance();
            dodgeSettings.BaseDodgeChance = 1f;
            dodgeSettings.MaximumDodgeChance = 1f;
            EquippedPart legs = new EquippedPart(
                DefaultPartDefinitionCatalog.GetRequired("part_epic_mayari_legs"));
            BattleController dodge = CreateBattle(
                Loadout("p1", null, heavy), Loadout("p2", new[] { legs }, heavy),
                dodgeSettings, new SequenceRandom(0.1d, 0d));
            dodge.Start(Now);
            Submit(dodge, "p1", "three", heavy.Id);
            Pass(dodge, "p2", "four");
            Assert.That(dodge.State.Fighters["p2"].HasStatus(StatusEffectIds.Bilis), Is.True);
        }

        [Test]
        public void NegativeStatusHealing_IsSuccessfulOnlyAndAffectedByMadumi()
        {
            SkillDefinition dirtyHit = DamageWithStatus("dirty", 10, StatusEffectIds.Madumi);
            SkillDefinition applyStatus = StatusOnly("curse", StatusEffectIds.Lamig);
            EquippedPart healer = new EquippedPart(
                DefaultPartDefinitionCatalog.GetRequired("part_rare_manggagayuma_head"));
            BattleController battle = CreateBattle(
                Loadout("p1", new[] { healer }, applyStatus),
                Loadout("p2", null, dirtyHit),
                NoVariance(),
                new SequenceRandom(0.9d, 0.99d));
            battle.Start(Now);

            Pass(battle, "p1", "one");
            Submit(battle, "p2", "two", dirtyHit.Id);
            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(118));

            Submit(battle, "p1", "three", applyStatus.Id);
            Pass(battle, "p2", "four");

            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(121));
            Assert.That(battle.State.Fighters["p2"].HasStatus(StatusEffectIds.Lamig), Is.True);
        }

        [Test]
        public void StatusSynergyFlatDamage_RequiresExistingNegativeStatus()
        {
            SkillDefinition curse = StatusOnly("curse", StatusEffectIds.Sunog);
            SkillDefinition hit = Damage("hit", 10);
            EquippedPart head = new EquippedPart(
                DefaultPartDefinitionCatalog.GetRequired("part_rare_mangkukulam_head"));
            BattleController battle = CreateBattle(
                Loadout("p1", new[] { head }, curse, hit),
                Loadout("p2", null, hit));
            battle.Start(Now);

            Submit(battle, "p1", "one", curse.Id);
            Pass(battle, "p2", "two");
            Submit(battle, "p1", "three", hit.Id);
            Pass(battle, "p2", "four");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(72));
        }

        [Test]
        public void FirstDirectDamageAndFirstCritical_TriggerOncePerDuelAndExchange()
        {
            SkillDefinition hit = Damage("hit", 10);
            EquippedPart lalahonLegs = new EquippedPart(
                DefaultPartDefinitionCatalog.GetRequired("part_epic_lalahon_legs"));
            BattleController directDamage = CreateBattle(
                Loadout("p1", null, hit), Loadout("p2", new[] { lalahonLegs }, hit));
            directDamage.Start(Now);
            Submit(directDamage, "p1", "one", hit.Id);
            Pass(directDamage, "p2", "two");
            Assert.That(directDamage.State.Fighters["p2"].HasStatus(StatusEffectIds.Lakas), Is.True);

            SkillDefinition multi = Damage("multi", 10, 2);
            EquippedPart mayariHead = new EquippedPart(
                DefaultPartDefinitionCatalog.GetRequired("part_epic_mayari_head"));
            var criticalSettings = NoVariance();
            criticalSettings.BaseCriticalChance = 1f;
            BattleController critical = CreateBattle(
                Loadout("p1", new[] { mayariHead }, multi),
                Loadout("p2", null, hit),
                criticalSettings,
                new SequenceRandom(0.9d, 0.99d, 0d, 0.99d, 0.99d, 0d, 0.99d, 0d));
            critical.Start(Now);
            Pass(critical, "p1", "three");
            Submit(critical, "p2", "four", hit.Id);
            Submit(critical, "p1", "five", multi.Id);
            Pass(critical, "p2", "six");

            Assert.That(critical.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(150));
        }

        private static EquippedPart[] CreateEquippedSet(CraftingCatalog catalog, string characterId)
        {
            return new[]
            {
                new EquippedPart(catalog.Parts["part_" + characterId + "_head"].BattleDefinition),
                new EquippedPart(catalog.Parts["part_" + characterId + "_body"].BattleDefinition),
                new EquippedPart(catalog.Parts["part_" + characterId + "_legs"].BattleDefinition)
            };
        }

        private static CardBattleLoadout Blank(string playerId) =>
            Loadout(playerId, null, Damage("hit", 1));

        private static CardBattleLoadout Loadout(
            string playerId, IReadOnlyList<EquippedPart> parts, params SkillDefinition[] skills) =>
            new CardBattleLoadout(playerId, "card-" + playerId, parts, skills);

        private static SkillDefinition Damage(string id, int amount, int instances = 1) =>
            new SkillDefinition(id, SkillCategory.Normal, 0, Array.Empty<ISkillEffect>(),
                execution: new SkillExecutionDefinition(amount, instances));

        private static SkillDefinition DamageWithStatus(string id, int amount, string statusId) =>
            new SkillDefinition(id, SkillCategory.Normal, 0, Array.Empty<ISkillEffect>(),
                execution: new SkillExecutionDefinition(amount, 1,
                    statusApplications: new[]
                    {
                        new SkillStatusApplication(statusId, 1f, SkillTargetType.Enemy)
                    }));

        private static SkillDefinition StatusOnly(string id, string statusId) =>
            new SkillDefinition(id, SkillCategory.Normal, 0, Array.Empty<ISkillEffect>(),
                execution: new SkillExecutionDefinition(
                    statusApplications: new[]
                    {
                        new SkillStatusApplication(statusId, 1f, SkillTargetType.Enemy)
                    }));

        private static BattleController CreateBattle(
            CardBattleLoadout first,
            CardBattleLoadout second,
            BattleSettings settings = null,
            IRandomNumberProvider random = null) =>
            new BattleController("battle", first, second, settings ?? NoVariance(),
                new FixedBattleAuthority(true), random ?? new SequenceRandom(0.1d, 0.99d));

        private static BattleSettings NoVariance() => new BattleSettings
        {
            BaseDodgeChance = 0f,
            BaseCriticalChance = 0f
        };

        private static void Submit(BattleController battle, string player, string command, string skill)
        {
            Assert.That(battle.SubmitAction(BattleAction.UseSkill(player, command, skill), Now).Accepted, Is.True);
        }

        private static void Pass(BattleController battle, string player, string command)
        {
            Assert.That(battle.SubmitAction(BattleAction.Pass(player, command), Now).Accepted, Is.True);
        }

        private sealed class SequenceRandom : IRandomNumberProvider
        {
            private readonly Queue<double> values;
            private readonly double fallback;

            public SequenceRandom(params double[] values)
            {
                this.values = new Queue<double>(values);
                fallback = values.Length == 0 ? 0.99d : values[values.Length - 1];
            }

            public double NextUnit() => values.Count > 0 ? values.Dequeue() : fallback;
        }
    }
}
