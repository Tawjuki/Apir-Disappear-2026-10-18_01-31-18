using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ApirApir.Battle.Tests
{
    public sealed class CompleteSkillAndStatusTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

        [Test]
        public void Catalog_ContainsExactlyRequiredNonFusionSkills()
        {
            SkillCatalog catalog = DefaultSkillCatalog.Create();

            Assert.That(catalog.Definitions.Count, Is.EqualTo(30));
            Assert.That(catalog.NormalSkills.Count, Is.EqualTo(5));
            Assert.That(catalog.SoulSkills.Count, Is.EqualTo(25));
            Assert.That(catalog.Definitions.Values.Any(x => x.Category == SkillCategory.Fusion), Is.False);
            Assert.That(catalog.Definitions.Keys.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(30));
        }

        [Test]
        public void SoulCatalog_ContainsExactlyFiveDistinctSouls()
        {
            SoulCatalog souls = DefaultSoulCatalog.Create();
            Assert.That(souls.Definitions.Count, Is.EqualTo(5));
            Assert.That(souls.Definitions.Keys, Is.EquivalentTo(SoulIds.All));
        }

        [TestCase(SoulIds.Kanayunan)]
        [TestCase(SoulIds.Dambana)]
        [TestCase(SoulIds.Katubigan)]
        [TestCase(SoulIds.Pamayanan)]
        [TestCase(SoulIds.Aliwanan)]
        public void Catalog_EachSoulHasFiveRanksAndCorrectTalino(string soulId)
        {
            IReadOnlyList<SkillDefinition> skills = DefaultSkillCatalog.Create().GetSoulSkills(soulId);

            Assert.That(skills.Count, Is.EqualTo(5));
            Assert.That(skills.Select(x => x.Rank), Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(skills.OrderBy(x => x.Rank).Select(x => x.TalinoUnlockCost),
                Is.EqualTo(new[] { 5, 10, 15, 20, 25 }));
        }

        [Test]
        public void Catalog_NormalSkillsAreFreeImmediateAndZeroCooldown()
        {
            foreach (SkillDefinition skill in DefaultSkillCatalog.Create().NormalSkills)
            {
                Assert.That(skill.SoulId, Is.Empty, skill.Id);
                Assert.That(skill.Rank, Is.Zero, skill.Id);
                Assert.That(skill.TalinoUnlockCost, Is.Zero, skill.Id);
                Assert.That(skill.CooldownExchanges, Is.Zero, skill.Id);
            }
        }

        [TestCase(DefaultSkillCatalog.BasicAttackId, 10, 1, 0)]
        [TestCase(DefaultSkillCatalog.PahingaId, 0, 0, 0)]
        [TestCase(DefaultSkillCatalog.SunodSunodId, 4, 2, 0)]
        [TestCase(DefaultSkillCatalog.PangguloId, 6, 1, 0)]
        [TestCase(DefaultSkillCatalog.SuwerteId, 0, 0, 0)]
        public void NormalSkillData_IsExact(string id, int damage, int instances, int cooldown)
        {
            Assert.That(DefaultSkillCatalog.Create().TryGet(id, out SkillDefinition skill), Is.True);
            Assert.That(skill.Execution.DamagePerInstance, Is.EqualTo(damage));
            Assert.That(skill.Execution.DamageInstances, Is.EqualTo(instances));
            Assert.That(skill.CooldownExchanges, Is.EqualTo(cooldown));
        }

        [Test]
        public void StatusCatalog_ContainsEightNegativeAndFivePositiveDefinitions()
        {
            StatusEffectCatalog catalog = StatusEffectCatalog.CreateDefault();

            Assert.That(catalog.Definitions.Count, Is.EqualTo(13));
            Assert.That(catalog.Negative.Count, Is.EqualTo(8));
            Assert.That(catalog.Positive.Count, Is.EqualTo(5));
        }

        [TestCase("soul_kanayunan_ani_ng_lakas", StatusEffectIds.Lakas, 0.80f)]
        [TestCase("soul_kanayunan_pahinga_sa_lilim", StatusEffectIds.Pagsigla, 1f)]
        [TestCase("soul_kanayunan_lason_ng_dahon", StatusEffectIds.Lason, 0.40f)]
        [TestCase("soul_dambana_basbas", StatusEffectIds.Laban, 1f)]
        [TestCase("soul_dambana_sindak_ng_kandila", StatusEffectIds.Sunog, 0.40f)]
        [TestCase("soul_katubigan_daloy_ng_tubig", StatusEffectIds.Bilis, 1f)]
        [TestCase("soul_katubigan_yelong_ambon", StatusEffectIds.Lamig, 0.45f)]
        [TestCase("soul_katubigan_basang_sipa", StatusEffectIds.Basa, 0.40f)]
        [TestCase("soul_pamayanan_bayanihan", StatusEffectIds.Lakas, 1f)]
        [TestCase("soul_pamayanan_barikada_ng_barangay", StatusEffectIds.Laban, 1f)]
        [TestCase("soul_pamayanan_batong_kalye", StatusEffectIds.Sugat, 0.55f)]
        [TestCase("soul_aliwanan_alikabok", StatusEffectIds.Madumi, 0.50f)]
        [TestCase("soul_aliwanan_tamang_tira", StatusEffectIds.Talas, 1f)]
        [TestCase("soul_aliwanan_laruang_kuryente", StatusEffectIds.Kuryente, 0.35f)]
        [TestCase("soul_aliwanan_paikot_ulo", StatusEffectIds.Paghihilo, 0.40f)]
        public void SoulSkillStatusProbability_IsExact(string skillId, string statusId, float chance)
        {
            Assert.That(DefaultSkillCatalog.Create().TryGet(skillId, out SkillDefinition skill), Is.True);
            SkillStatusApplication application = skill.Execution.StatusApplications.Single();
            Assert.That(application.StatusId, Is.EqualTo(statusId));
            Assert.That(application.Probability, Is.EqualTo(chance).Within(0.0001f));
        }

        [Test]
        public void RandomStatusNormalSkills_RetainTwentyFivePercentChance()
        {
            SkillCatalog catalog = DefaultSkillCatalog.Create();
            catalog.TryGet(DefaultSkillCatalog.PangguloId, out SkillDefinition panggulo);
            catalog.TryGet(DefaultSkillCatalog.SuwerteId, out SkillDefinition suwerte);

            Assert.That(panggulo.Execution.SpecialProbability, Is.EqualTo(0.25f));
            Assert.That(suwerte.Execution.SpecialProbability, Is.EqualTo(0.25f));
        }

        [Test]
        public void MultiHit_UsesIndependentDodgeAndMaintenanceRolls()
        {
            SkillDefinition skill = Get("soul_katubigan_tilamsik");
            var part = new EquippedPart(new PartDefinition("head", PartSlot.Head, PartRarity.Common));
            BattleController battle = Battle(
                Loadout("p1", skill),
                Loadout("p2", skill, part),
                DodgeSettings(0.10f),
                new SequenceRandom(0d, 0.05d, 0.20d, 0.99d, 0d, 0.20d, 0.99d, 0d));
            battle.Start(Now);

            Submit(battle, "p1", "a", skill.Id);
            Pass(battle, "p2", "b");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(90));
            Assert.That(battle.State.Fighters["p2"].Parts[0].CurrentMaintenance, Is.EqualTo(18));
        }

        [Test]
        public void DodgedHit_DoesNotApplyOnHitStatus()
        {
            SkillDefinition skill = Get("soul_kanayunan_lason_ng_dahon");
            BattleController battle = Battle(
                Loadout("p1", skill), Loadout("p2", skill), DodgeSettings(0.30f),
                new SequenceRandom(0d, 0.1d));
            battle.Start(Now);

            Submit(battle, "p1", "a", skill.Id);
            Pass(battle, "p2", "b");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(100));
            Assert.That(battle.State.Fighters["p2"].HasStatus(StatusEffectIds.Lason), Is.False);
        }

        [Test]
        public void SelfBuffChance_IsIndependentOfEnemyDodge()
        {
            SkillDefinition skill = Get("soul_kanayunan_ani_ng_lakas");
            BattleController battle = Battle(
                Loadout("p1", skill), Loadout("p2", skill), DodgeSettings(0.30f),
                new SequenceRandom(0d, 0.1d, 0.1d));
            battle.Start(Now);

            Submit(battle, "p1", "a", skill.Id);
            Pass(battle, "p2", "b");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(100));
            Assert.That(battle.State.Fighters["p1"].HasStatus(StatusEffectIds.Lakas), Is.True);
        }

        [Test]
        public void Lason_TicksForTwoFutureExchangesUsingMaximumHitPoints()
        {
            SkillDefinition poison = StatusOnly("poison", StatusEffectIds.Lason, SkillTargetType.Enemy);
            BattleController battle = Battle(
                Loadout("p1", poison), Loadout("p2", poison), NoVariance(),
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Submit(battle, "p1", "a", poison.Id);
            Pass(battle, "p2", "b");

            Pass(battle, "p1", "c");
            Pass(battle, "p2", "d");
            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(90));
            Pass(battle, "p1", "e");
            Pass(battle, "p2", "f");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(80));
            Assert.That(battle.State.Fighters["p2"].HasStatus(StatusEffectIds.Lason), Is.False);
        }

        [Test]
        public void Pagsigla_HealsTenForThreeFutureExchanges()
        {
            SkillDefinition regen = StatusOnly("regen", StatusEffectIds.Pagsigla, SkillTargetType.Self);
            SkillDefinition hit = Damage("hit", 30);
            BattleController battle = Battle(
                Loadout("p1", regen), Loadout("p2", hit), NoVariance(),
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Submit(battle, "p1", "a", regen.Id);
            Submit(battle, "p2", "b", hit.Id);
            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(70));

            for (int i = 0; i < 3; i++)
            {
                Pass(battle, "p1", "p1-" + i);
                Pass(battle, "p2", "p2-" + i);
            }

            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(100));
            Assert.That(battle.State.Fighters["p1"].HasStatus(StatusEffectIds.Pagsigla), Is.False);
        }

        [Test]
        public void Madumi_ReducesImmediateHealingByHalf()
        {
            SkillDefinition dirt = StatusOnly("dirt", StatusEffectIds.Madumi, SkillTargetType.Enemy);
            SkillDefinition hit = Damage("hit", 10);
            SkillDefinition heal = Get(DefaultSkillCatalog.PahingaId);
            BattleController battle = Battle(
                Loadout("p1", heal), Loadout("p2", hit, dirt), NoVariance(),
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Pass(battle, "p1", "a");
            Submit(battle, "p2", "b", hit.Id);
            Pass(battle, "p1", "c");
            Submit(battle, "p2", "d", dirt.Id);
            Submit(battle, "p1", "e", heal.Id);
            Pass(battle, "p2", "f");

            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(93));
        }

        [Test]
        public void Sunog_DealsConfiguredPeriodicDamageAndReducesOutgoingDamage()
        {
            SkillDefinition burn = StatusOnly("burn", StatusEffectIds.Sunog, SkillTargetType.Enemy);
            SkillDefinition hit = Damage("hit", 10);
            var settings = NoVariance();
            settings.SunogDamagePerExchange = 10;
            BattleController battle = Battle(
                Loadout("p1", hit), Loadout("p2", burn), settings,
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Pass(battle, "p1", "a");
            Submit(battle, "p2", "b", burn.Id);
            Submit(battle, "p1", "c", hit.Id);
            Pass(battle, "p2", "d");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(92));
            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(90));
        }

        [Test]
        public void BilisAndFunctionalPartSpeed_UseCentralDodgeFormula()
        {
            SkillDefinition flow = Get("soul_katubigan_daloy_ng_tubig");
            var speedPart = new EquippedPart(new PartDefinition(
                "speed-head", PartSlot.Head, PartRarity.Epic, speedBonusPercent: 5f));
            BattleSettings settings = DodgeSettings(0.10f);
            BattleController battle = Battle(
                Loadout("p1", new[] { flow }, speedPart), Loadout("p2", flow), settings,
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Submit(battle, "p1", "a", flow.Id);
            Pass(battle, "p2", "b");

            var modifiers = new CombatModifierCalculator(StatusEffectCatalog.CreateDefault(), settings);
            Assert.That(modifiers.GetSpeedModifierPercent(battle.State.Fighters["p1"]), Is.EqualTo(30f));
            Assert.That(modifiers.GetDodgeChance(battle.State.Fighters["p1"]), Is.EqualTo(0.13f).Within(0.0001f));
        }

        [Test]
        public void CriticalChanceAndMultiplier_AreConfigurableAndRolledPerHit()
        {
            SkillDefinition hit = Damage("hit", 10);
            BattleSettings settings = NoVariance();
            settings.BaseCriticalChance = 1f;
            settings.CriticalDamageMultiplier = 1.5f;
            BattleController battle = Battle(
                Loadout("p1", hit), Loadout("p2", hit), settings,
                new SequenceRandom(0d, 0.99d, 0d));
            battle.Start(Now);
            Submit(battle, "p1", "a", hit.Id);
            Pass(battle, "p2", "b");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(85));
        }

        [Test]
        public void Paghihilo_UsesSeparateInternalProbabilityRoll()
        {
            SkillDefinition dizzy = StatusOnly("dizzy", StatusEffectIds.Paghihilo, SkillTargetType.Enemy);
            BattleController battle = Battle(
                Loadout("p1", dizzy), Loadout("p2", dizzy), NoVariance(),
                new SequenceRandom(0d, 0.40d, 0.60d));
            battle.Start(Now);
            Submit(battle, "p1", "a", dizzy.Id);
            Pass(battle, "p2", "b");
            Pass(battle, "p1", "c");
            Pass(battle, "p2", "d");
            Pass(battle, "p1", "e");
            Pass(battle, "p2", "f");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(90));
            Assert.That(battle.State.Fighters["p2"].Statuses[StatusEffectIds.Paghihilo], Is.EqualTo(3));
        }

        [Test]
        public void Sugat_AmplifiesOnlyNextDirectHit()
        {
            SkillDefinition wound = StatusOnly("wound", StatusEffectIds.Sugat, SkillTargetType.Enemy);
            SkillDefinition hit = Damage("hit", 10);
            BattleController battle = Battle(
                Loadout("p1", wound, hit), Loadout("p2", hit), NoVariance(),
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Submit(battle, "p1", "a", wound.Id);
            Pass(battle, "p2", "b");
            Submit(battle, "p1", "c", hit.Id);
            Pass(battle, "p2", "d");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(87));
            Assert.That(battle.State.Fighters["p2"].HasStatus(StatusEffectIds.Sugat), Is.False);
        }

        [Test]
        public void Alay_SacrificesTenWithoutMaintenanceAndDealsTwentyEight()
        {
            SkillDefinition alay = Get("soul_dambana_alay");
            var ownPart = new EquippedPart(new PartDefinition("own", PartSlot.Head, PartRarity.Common));
            BattleController battle = Battle(
                Loadout("p1", new[] { alay }, ownPart), Loadout("p2", alay), NoVariance(),
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Submit(battle, "p1", "a", alay.Id);
            Pass(battle, "p2", "b");

            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(90));
            Assert.That(battle.State.Fighters["p1"].Parts[0].CurrentMaintenance, Is.EqualTo(20));
            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(72));
        }

        [Test]
        public void Ganti_UsesAuthoritativePreviousExchangeHistory()
        {
            SkillDefinition ganti = Get("soul_pamayanan_ganti_ng_kapitbahay");
            SkillDefinition hit = Damage("hit", 1);
            BattleController battle = Battle(
                Loadout("p1", ganti), Loadout("p2", hit), NoVariance(),
                new SequenceRandom(0d, 0.99d));
            battle.Start(Now);
            Pass(battle, "p1", "a");
            Submit(battle, "p2", "b", hit.Id);
            Submit(battle, "p1", "c", ganti.Id);
            Pass(battle, "p2", "d");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(77));
        }

        [TestCase(StatusEffectIds.Sunog, -25f, -25f, 0f, 0f)]
        [TestCase(StatusEffectIds.Basa, -50f, -75f, -50f, 0f)]
        [TestCase(StatusEffectIds.Lamig, -25f, 0f, -25f, 0f)]
        [TestCase(StatusEffectIds.Lakas, 25f, 0f, 0f, 0f)]
        [TestCase(StatusEffectIds.Talas, 0f, 0f, 50f, 0f)]
        [TestCase(StatusEffectIds.Laban, 0f, 0f, 0f, -30f)]
        [TestCase(StatusEffectIds.Bilis, 0f, 25f, 0f, 0f)]
        public void StatusDefinition_ContainsConfiguredCombatModifiers(
            string id, float attack, float speed, float critical, float incoming)
        {
            Assert.That(StatusEffectCatalog.CreateDefault().TryGet(id, out StatusEffectDefinition value), Is.True);
            Assert.That(value.OutgoingDamageModifierPercent, Is.EqualTo(attack));
            Assert.That(value.SpeedModifierPercent, Is.EqualTo(speed));
            Assert.That(value.CriticalChanceModifierPercent, Is.EqualTo(critical));
            Assert.That(value.IncomingDamageModifierPercent, Is.EqualTo(incoming));
        }

        [Test]
        public void Kuryente_ConsumesOneActionAttemptAndPublishesSeparatePrevention()
        {
            SkillDefinition shock = StatusOnly("shock", StatusEffectIds.Kuryente, SkillTargetType.Enemy);
            SkillDefinition hit = Damage("hit", 10);
            BattleController battle = Battle(
                Loadout("p1", shock), Loadout("p2", hit), NoVariance(),
                new SequenceRandom(0d, 0.1d, 0.99d));
            var events = new List<BattleEvent>();
            battle.EventPublished += events.Add;
            battle.Start(Now);
            Submit(battle, "p1", "a", shock.Id);
            Pass(battle, "p2", "b");
            Pass(battle, "p1", "c");
            Submit(battle, "p2", "d", hit.Id);

            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(100));
            Assert.That(battle.State.Fighters["p2"].HasStatus(StatusEffectIds.Kuryente), Is.False);
            Assert.That(events.Any(x => x.Type == BattleEventType.ActionPrevented), Is.True);
        }

        private static SkillDefinition Get(string id)
        {
            Assert.That(DefaultSkillCatalog.Create().TryGet(id, out SkillDefinition skill), Is.True);
            return skill;
        }

        private static SkillDefinition Damage(string id, int amount) =>
            new SkillDefinition(id, SkillCategory.Normal, 0, Array.Empty<ISkillEffect>(),
                execution: new SkillExecutionDefinition(amount, 1));

        private static SkillDefinition StatusOnly(
            string id, string statusId, SkillTargetType target) =>
            new SkillDefinition(id, SkillCategory.Normal, 0, Array.Empty<ISkillEffect>(),
                targetType: target,
                execution: new SkillExecutionDefinition(
                    statusApplications: new[] { new SkillStatusApplication(statusId, 1f, target) }));

        private static CardBattleLoadout Loadout(
            string playerId, params SkillDefinition[] skills) =>
            new CardBattleLoadout(playerId, "card-" + playerId, null, skills);

        private static CardBattleLoadout Loadout(
            string playerId, SkillDefinition skill, EquippedPart part) =>
            new CardBattleLoadout(playerId, "card-" + playerId, new[] { part }, new[] { skill });

        private static CardBattleLoadout Loadout(
            string playerId, IReadOnlyList<SkillDefinition> skills, EquippedPart part) =>
            new CardBattleLoadout(playerId, "card-" + playerId, new[] { part }, skills);

        private static BattleController Battle(
            CardBattleLoadout first,
            CardBattleLoadout second,
            BattleSettings settings,
            IRandomNumberProvider random) =>
            new BattleController("battle", first, second, settings,
                new FixedBattleAuthority(true), random);

        private static BattleSettings NoVariance() => new BattleSettings
        {
            BaseDodgeChance = 0f,
            BaseCriticalChance = 0f
        };

        private static BattleSettings DodgeSettings(float chance) => new BattleSettings
        {
            BaseDodgeChance = chance,
            MaximumDodgeChance = 0.30f,
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
