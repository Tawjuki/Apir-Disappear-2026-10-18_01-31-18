using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ApirApir.Battle.Tests
{
    public sealed class BattleSystemTests
    {
        private static readonly DateTimeOffset StartTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Test]
        public void BlankCards_FightWithoutEquippedParts()
        {
            SkillDefinition basic = DamageSkill("basic", SkillCategory.Normal, 10);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { basic }),
                Loadout("p2", skills: new[] { basic }));

            battle.Start(StartTime);
            SubmitSkill(battle, "p1", "one", basic.Id);
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(90));
            Assert.That(battle.State.Fighters["p1"].MaximumHitPoints, Is.EqualTo(100));
        }

        [Test]
        public void Loadout_RejectsMoreThanFourSkills()
        {
            var skills = Enumerable.Range(0, 5)
                .Select(i => DamageSkill("skill-" + i, SkillCategory.Normal, 1))
                .ToArray();

            Assert.Throws<ArgumentException>(() => Loadout("p1", skills: skills));
        }

        [Test]
        public void BasicAttack_IsNotAddedAsAFifthAction()
        {
            SkillDefinition[] fourSkills = Enumerable.Range(0, 4)
                .Select(i => DamageSkill("skill-" + i, SkillCategory.Normal, 1))
                .ToArray();
            BattleController battle = CreateBattle(
                Loadout("p1", skills: fourSkills),
                Loadout("p2", skills: new[] { fourSkills[0] }));
            battle.Start(StartTime);

            ActionSubmissionResult result = battle.SubmitAction(
                BattleAction.UseSkill("p1", "one", "basic-attack"),
                StartTime);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectionReason, Is.EqualTo(ActionRejectionReason.SkillNotEquipped));
        }

        [Test]
        public void NormalSkill_NeverEntersCooldown()
        {
            SkillDefinition normal = DamageSkill("normal", SkillCategory.Normal, 1);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { normal }),
                Loadout("p2", skills: new[] { normal }));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", normal.Id);
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p1"].GetCooldown(normal.Id), Is.Zero);
        }

        [Test]
        public void Cooldown_AdvancesOnlyAfterACompleteExchange()
        {
            SkillDefinition special = DamageSkill("soul", SkillCategory.Soul, 1, cooldown: 1);
            SkillDefinition basic = DamageSkill("basic", SkillCategory.Normal, 1);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { special }),
                Loadout("p2", skills: new[] { basic }));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", special.Id);
            Assert.That(battle.State.Fighters["p1"].GetCooldown(special.Id), Is.Zero);
            SubmitPass(battle, "p2", "two");
            Assert.That(battle.State.Fighters["p1"].GetCooldown(special.Id), Is.EqualTo(1));

            SubmitPass(battle, "p2", "three");
            Assert.That(battle.State.Fighters["p1"].GetCooldown(special.Id), Is.Zero);
        }

        [Test]
        public void CooldownConvention_CanIncludeActivationExchange()
        {
            SkillDefinition special = DamageSkill("soul", SkillCategory.Soul, 1, cooldown: 1);
            SkillDefinition basic = DamageSkill("basic", SkillCategory.Normal, 1);
            var settings = new BattleSettings
            {
                CooldownConvention = CooldownConvention.IncludeActivationExchange
            };
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { special }),
                Loadout("p2", skills: new[] { basic }),
                settings);
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", special.Id);
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p1"].GetCooldown(special.Id), Is.Zero);
        }

        [Test]
        public void AllSkillsUnavailable_ForcesPlayerToSkip()
        {
            SkillDefinition special = DamageSkill("soul", SkillCategory.Soul, 1, cooldown: 2);
            SkillDefinition basic = DamageSkill("basic", SkillCategory.Normal, 1);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { special }),
                Loadout("p2", skills: new[] { basic }));
            var events = new List<BattleEvent>();
            battle.EventPublished += events.Add;
            battle.Start(StartTime);
            SubmitSkill(battle, "p1", "one", special.Id);
            SubmitPass(battle, "p2", "two");

            SubmitPass(battle, "p2", "three");

            Assert.That(events.Any(e =>
                e.Type == BattleEventType.ActionSkipped &&
                e.ActorPlayerId == "p1"), Is.True);
        }

        [Test]
        public void BothPlayersOnCooldown_AutoSkipUntilASkillBecomesAvailable()
        {
            SkillDefinition special = DamageSkill("soul", SkillCategory.Soul, 1, cooldown: 2);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { special }),
                Loadout("p2", skills: new[] { special }));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", special.Id);
            SubmitSkill(battle, "p2", "two", special.Id);

            Assert.That(battle.State.Phase, Is.EqualTo(BattlePhase.SelectingActions));
            Assert.That(battle.State.ExchangeNumber, Is.EqualTo(4));
            Assert.That(battle.State.Fighters["p1"].GetCooldown(special.Id), Is.Zero);
            Assert.That(battle.State.Fighters["p2"].GetCooldown(special.Id), Is.Zero);
        }

        [Test]
        public void MultiHit_PerformsMaintenanceRollForEveryPartAndHit()
        {
            SkillDefinition multi = DamageSkill("multi", SkillCategory.Normal, 1, hitCount: 4);
            PartDefinition head = Part("head", PartSlot.Head, PartRarity.Common);
            PartDefinition body = Part("body", PartSlot.Body, PartRarity.Common);
            PartDefinition legs = Part("legs", PartSlot.Legs, PartRarity.Common);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { multi }),
                Loadout("p2", new[] { new EquippedPart(head), new EquippedPart(body), new EquippedPart(legs) }, new[] { multi }));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", multi.Id);
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].Parts.Select(p => p.CurrentMaintenance),
                Is.EqualTo(new[] { 16, 16, 16 }));
        }

        [Test]
        public void Maintenance_NeverFallsBelowZero()
        {
            SkillDefinition manyHits = DamageSkill("many", SkillCategory.Normal, 1, hitCount: 30);
            var wornPart = new EquippedPart(Part("head", PartSlot.Head, PartRarity.Common), 2);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { manyHits }),
                Loadout("p2", new[] { wornPart }, new[] { manyHits }));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", manyHits.Id);
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].Parts[0].CurrentMaintenance, Is.Zero);
        }

        [Test]
        public void ZeroMaintenance_DisablesAttackBonusBeforeLaterAction()
        {
            SkillDefinition weak = DamageSkill("weak", SkillCategory.Normal, 1);
            SkillDefinition counter = DamageSkill("counter", SkillCategory.Normal, 10);
            var fragileBonus = new EquippedPart(
                Part("bonus", PartSlot.Head, PartRarity.Common, attackBonus: 50f),
                1);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { weak }),
                Loadout("p2", new[] { fragileBonus }, new[] { counter }),
                random: new SequenceRandom(0d));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", weak.Id);
            SubmitSkill(battle, "p2", "two", counter.Id);

            Assert.That(battle.State.Fighters["p2"].Parts[0].IsFunctional, Is.False);
            Assert.That(battle.State.Fighters["p1"].CurrentHitPoints, Is.EqualTo(90));
        }

        [Test]
        public void BrokenPart_KeepsHitPointsDuringBattle_ThenRecalculatesAfterBattle()
        {
            SkillDefinition weak = DamageSkill("weak", SkillCategory.Normal, 1);
            var fragileHealth = new EquippedPart(
                Part("health", PartSlot.Body, PartRarity.Common, hitPoints: 50),
                1);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { weak }),
                Loadout("p2", new[] { fragileHealth }, new[] { weak }));
            battle.Start(StartTime);
            SubmitSkill(battle, "p1", "one", weak.Id);
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].MaximumHitPoints, Is.EqualTo(150));
            battle.Cancel();

            Assert.That(battle.State.Result.SecondFighter.FinalMaximumHitPoints, Is.EqualTo(100));
            Assert.That(battle.State.Result.SecondFighter.FinalHitPoints, Is.EqualTo(100));
        }

        [Test]
        public void PartAlreadyAtZeroMaintenance_DoesNotAddHitPointsToANewBattle()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 1);
            var brokenHealthPart = new EquippedPart(
                Part("health", PartSlot.Body, PartRarity.Common, hitPoints: 50),
                0);
            BattleController battle = CreateBattle(
                Loadout("p1", new[] { brokenHealthPart }, new[] { skill }),
                Loadout("p2", skills: new[] { skill }));

            Assert.That(battle.State.Fighters["p1"].MaximumHitPoints, Is.EqualTo(100));
        }

        [Test]
        public void Damage_AppliesCombinedPercentageAndRoundsOnce()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 10);
            var head = new EquippedPart(Part("head", PartSlot.Head, PartRarity.Epic, attackBonus: 10f));
            var body = new EquippedPart(Part("body", PartSlot.Body, PartRarity.Epic, attackBonus: 20f));
            BattleController battle = CreateBattle(
                Loadout("p1", new[] { head, body }, new[] { skill }),
                Loadout("p2", skills: new[] { skill }));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", skill.Id);
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(87));
        }

        [Test]
        public void InvalidAndDuplicateActions_AreRejected()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 1);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { skill }),
                Loadout("p2", skills: new[] { skill }));
            battle.Start(StartTime);

            ActionSubmissionResult outsider = battle.SubmitAction(
                BattleAction.UseSkill("outsider", "one", skill.Id),
                StartTime);
            BattleAction firstAction = BattleAction.UseSkill("p1", "two", skill.Id);
            ActionSubmissionResult first = battle.SubmitAction(firstAction, StartTime);
            ActionSubmissionResult duplicate = battle.SubmitAction(firstAction, StartTime);

            Assert.That(outsider.RejectionReason, Is.EqualTo(ActionRejectionReason.UnknownPlayer));
            Assert.That(first.Accepted, Is.True);
            Assert.That(duplicate.RejectionReason, Is.EqualTo(ActionRejectionReason.DuplicateCommand));
        }

        [Test]
        public void SimultaneousBattles_KeepIndependentState()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 10);
            BattleController first = CreateBattle(
                Loadout("a", skills: new[] { skill }),
                Loadout("b", skills: new[] { skill }),
                battleId: "battle-a");
            BattleController second = CreateBattle(
                Loadout("c", skills: new[] { skill }),
                Loadout("d", skills: new[] { skill }),
                battleId: "battle-b");
            first.Start(StartTime);
            second.Start(StartTime);

            SubmitSkill(first, "a", "one", skill.Id);
            SubmitPass(first, "b", "two");

            Assert.That(first.State.Fighters["b"].CurrentHitPoints, Is.EqualTo(90));
            Assert.That(second.State.Fighters["d"].CurrentHitPoints, Is.EqualTo(100));
            Assert.That(second.State.ExchangeNumber, Is.EqualTo(1));
        }

        [Test]
        public void BattleCompletion_IsPublishedOnlyOnce()
        {
            SkillDefinition lethal = DamageSkill("lethal", SkillCategory.Normal, 100);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { lethal }),
                Loadout("p2", skills: new[] { lethal }));
            int completionCount = 0;
            battle.BattleCompleted += _ => completionCount++;
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", lethal.Id);
            SubmitPass(battle, "p2", "two");
            bool cancelled = battle.Cancel();

            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(cancelled, Is.False);
        }

        [Test]
        public void SelectionTimeout_AutoPassesMissingActionWithoutPerFrameUpdate()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 10);
            var settings = new BattleSettings { SelectionDuration = TimeSpan.FromSeconds(10) };
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { skill }),
                Loadout("p2", skills: new[] { skill }),
                settings);
            battle.Start(StartTime);
            SubmitSkill(battle, "p1", "one", skill.Id);

            bool early = battle.AdvanceClock(StartTime.AddSeconds(9));
            bool expired = battle.AdvanceClock(StartTime.AddSeconds(10));

            Assert.That(early, Is.False);
            Assert.That(expired, Is.True);
            Assert.That(battle.State.Fighters["p2"].CurrentHitPoints, Is.EqualTo(90));
            Assert.That(battle.State.ExchangeNumber, Is.EqualTo(2));
        }

        [Test]
        public void KayaKulob_CanBeResolvedEveryExchange()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 1);
            var settings = new BattleSettings { KayaKulobFrequency = KayaKulobFrequency.EveryExchange };
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { skill }),
                Loadout("p2", skills: new[] { skill }),
                settings,
                new SequenceRandom(0.1d, 0.9d));
            battle.Start(StartTime);
            Assert.That(battle.State.KayaPlayerId, Is.EqualTo("p1"));

            SubmitPass(battle, "p1", "one");
            SubmitPass(battle, "p2", "two");

            Assert.That(battle.State.KayaPlayerId, Is.EqualTo("p2"));
        }

        [Test]
        public void InterruptionPolicy_CanResolveCommittedRetaliation()
        {
            SkillDefinition lethal = DamageSkill("lethal", SkillCategory.Normal, 100);
            var settings = new BattleSettings
            {
                DefeatInterruptionPolicy = DefeatInterruptionPolicy.ResolveCommittedActions
            };
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { lethal }),
                Loadout("p2", skills: new[] { lethal }),
                settings,
                new SequenceRandom(0d));
            battle.Start(StartTime);

            SubmitSkill(battle, "p1", "one", lethal.Id);
            SubmitSkill(battle, "p2", "two", lethal.Id);

            Assert.That(battle.State.Result.EndReason, Is.EqualTo(BattleEndReason.MutualDefeat));
            Assert.That(battle.State.Result.IsDraw, Is.True);
        }

        [Test]
        public void DeterministicRandom_ProducesReproducibleOrderAndTears()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 1);
            EquippedPart rare = new EquippedPart(Part("rare", PartSlot.Head, PartRarity.Rare));
            BattleController first = CreateBattle(
                Loadout("p1", skills: new[] { skill }),
                Loadout("p2", new[] { rare }, new[] { skill }),
                random: new SequenceRandom(0.25d, 0.49d));
            BattleController second = CreateBattle(
                Loadout("p1", skills: new[] { skill }),
                Loadout("p2", new[] { rare }, new[] { skill }),
                random: new SequenceRandom(0.25d, 0.49d),
                battleId: "battle-2");
            first.Start(StartTime);
            second.Start(StartTime);
            SubmitSkill(first, "p1", "one", skill.Id);
            SubmitPass(first, "p2", "two");
            SubmitSkill(second, "p1", "one", skill.Id);
            SubmitPass(second, "p2", "two");

            Assert.That(second.State.KayaPlayerId, Is.EqualTo(first.State.KayaPlayerId));
            Assert.That(second.State.Fighters["p2"].Parts[0].CurrentMaintenance,
                Is.EqualTo(first.State.Fighters["p2"].Parts[0].CurrentMaintenance));
        }

        [Test]
        public void Defeat_DoesNotRemoveEquippedPartsOrSkills()
        {
            SkillDefinition lethal = DamageSkill("lethal", SkillCategory.Normal, 100);
            EquippedPart part = new EquippedPart(Part("head", PartSlot.Head, PartRarity.Common));
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { lethal }),
                Loadout("p2", new[] { part }, new[] { lethal }));
            battle.Start(StartTime);
            SubmitSkill(battle, "p1", "one", lethal.Id);
            SubmitPass(battle, "p2", "two");

            CardCombatState defeated = battle.State.Fighters["p2"];
            Assert.That(defeated.Parts.Count, Is.EqualTo(1));
            Assert.That(defeated.TryGetSkill(lethal.Id, out SkillDefinition retained), Is.True);
            Assert.That(retained, Is.SameAs(lethal));
        }

        [Test]
        public void NonAuthoritativeController_CannotStartOrResolveActions()
        {
            SkillDefinition skill = DamageSkill("skill", SkillCategory.Normal, 1);
            BattleController battle = CreateBattle(
                Loadout("p1", skills: new[] { skill }),
                Loadout("p2", skills: new[] { skill }),
                authoritative: false);

            Assert.That(battle.Start(StartTime), Is.False);
            Assert.That(
                battle.SubmitAction(BattleAction.UseSkill("p1", "one", skill.Id), StartTime).RejectionReason,
                Is.EqualTo(ActionRejectionReason.NotAuthoritative));
        }

        private static BattleController CreateBattle(
            CardBattleLoadout first,
            CardBattleLoadout second,
            BattleSettings settings = null,
            IRandomNumberProvider random = null,
            string battleId = "battle",
            bool authoritative = true)
        {
            BattleSettings configured = settings ?? new BattleSettings();
            configured.BaseDodgeChance = 0f;
            configured.BaseCriticalChance = 0f;
            return new BattleController(
                battleId,
                first,
                second,
                configured,
                new FixedBattleAuthority(authoritative),
                random ?? new SequenceRandom(0d));
        }

        private static CardBattleLoadout Loadout(
            string playerId,
            IReadOnlyList<EquippedPart> parts = null,
            IReadOnlyList<SkillDefinition> skills = null)
        {
            return new CardBattleLoadout(playerId, "card-" + playerId, parts, skills);
        }

        private static SkillDefinition DamageSkill(
            string id,
            SkillCategory category,
            int power,
            int hitCount = 1,
            int cooldown = 0)
        {
            return new SkillDefinition(
                id,
                category,
                cooldown,
                new ISkillEffect[] { new DamageSkillEffect(power, hitCount) },
                soulId: category == SkillCategory.Soul ? SoulIds.Kanayunan : null,
                rank: category == SkillCategory.Soul ? 1 : 0,
                talinoUnlockCost: category == SkillCategory.Soul ? 5 : 0);
        }

        private static PartDefinition Part(
            string id,
            PartSlot slot,
            PartRarity rarity,
            int hitPoints = 0,
            float attackBonus = 0f)
        {
            return new PartDefinition(id, slot, rarity, hitPoints, attackBonus);
        }

        private static void SubmitSkill(BattleController battle, string playerId, string commandId, string skillId)
        {
            Assert.That(
                battle.SubmitAction(BattleAction.UseSkill(playerId, commandId, skillId), StartTime).Accepted,
                Is.True);
        }

        private static void SubmitPass(BattleController battle, string playerId, string commandId)
        {
            Assert.That(
                battle.SubmitAction(BattleAction.Pass(playerId, commandId), StartTime).Accepted,
                Is.True);
        }

        private sealed class SequenceRandom : IRandomNumberProvider
        {
            private readonly Queue<double> values;
            private readonly double fallback;

            public SequenceRandom(params double[] values)
            {
                this.values = new Queue<double>(values);
                fallback = values.Length == 0 ? 0d : values[values.Length - 1];
            }

            public double NextUnit()
            {
                return values.Count > 0 ? values.Dequeue() : fallback;
            }
        }
    }
}
