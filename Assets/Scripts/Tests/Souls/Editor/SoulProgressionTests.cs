using System.Linq;
using ApirApir.Battle;
using ApirApir.Souls;
using NUnit.Framework;

namespace ApirApir.Tests.Souls
{
    public sealed class SoulProgressionTests
    {
        [Test]
        public void NewPlayer_HasAllNormalSkillsAvailableWithoutTalino()
        {
            SoulProgressionService service = Service();
            SoulProgressionResult result = service.InitializePlayer("p1");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Snapshot.Talino, Is.Zero);
            Assert.That(result.Snapshot.UnlockedSkillIds.Count, Is.EqualTo(5));
            Assert.That(result.Snapshot.UnlockedSkillIds, Does.Contain(DefaultSkillCatalog.BasicAttackId));
        }

        [Test]
        public void Unlock_RequiresSoulAndSufficientTalino_ThenDeductsAtomically()
        {
            SoulProgressionService service = Service();
            service.InitializePlayer("p1", 5);

            SoulProgressionResult missingSoul = service.UnlockSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", 0, "unlock-a");
            Assert.That(missingSoul.Failure, Is.EqualTo(SoulProgressionFailure.SoulNotOwned));
            Assert.That(missingSoul.Snapshot.Talino, Is.EqualTo(5));

            SoulProgressionResult soul = service.GrantSoul("p1", SoulIds.Kanayunan, 0, "soul-a");
            SoulProgressionResult unlocked = service.UnlockSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", soul.Snapshot.Revision, "unlock-b");

            Assert.That(unlocked.Succeeded, Is.True);
            Assert.That(unlocked.Snapshot.Talino, Is.Zero);
            Assert.That(unlocked.Snapshot.UnlockedSkillIds,
                Does.Contain("soul_kanayunan_sibat_ng_kawayan"));
        }

        [Test]
        public void InsufficientTalino_DoesNotUnlockOrSpend()
        {
            SoulProgressionService service = Service();
            service.InitializePlayer("p1", 4);
            SoulProgressionResult soul = service.GrantSoul("p1", SoulIds.Kanayunan, 0, "soul");

            SoulProgressionResult result = service.UnlockSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", soul.Snapshot.Revision, "unlock");

            Assert.That(result.Failure, Is.EqualTo(SoulProgressionFailure.InsufficientTalino));
            Assert.That(result.Snapshot.Talino, Is.EqualTo(4));
            Assert.That(result.Snapshot.UnlockedSkillIds.Count, Is.EqualTo(5));
        }

        [Test]
        public void DuplicateUnlockCommand_CannotSpendTwice()
        {
            SoulProgressionService service = Service();
            service.InitializePlayer("p1", 50);
            SoulProgressionResult soul = service.GrantSoul("p1", SoulIds.Kanayunan, 0, "soul");
            SoulProgressionResult first = service.UnlockSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", soul.Snapshot.Revision, "same");

            SoulProgressionResult duplicate = service.UnlockSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", first.Snapshot.Revision, "same");

            Assert.That(duplicate.Failure, Is.EqualTo(SoulProgressionFailure.DuplicateCommand));
            Assert.That(duplicate.Snapshot.Talino, Is.EqualTo(45));
            Assert.That(duplicate.Snapshot.UnlockedSkillIds.Count(x =>
                x == "soul_kanayunan_sibat_ng_kawayan"), Is.EqualTo(1));
        }

        [Test]
        public void HigherRank_DoesNotRequireEarlierRanks()
        {
            SoulProgressionService service = Service();
            service.InitializePlayer("p1", 25);
            SoulProgressionResult soul = service.GrantSoul("p1", SoulIds.Kanayunan, 0, "soul");

            SoulProgressionResult result = service.UnlockSkill(
                "p1", "soul_kanayunan_lason_ng_dahon", soul.Snapshot.Revision, "rank-five");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Snapshot.Talino, Is.Zero);
        }

        [Test]
        public void SoulSkill_RequiresCurrentSelectedSoulToEquip()
        {
            SoulProgressionService service = Service();
            service.InitializePlayer("p1", 5);
            SoulProgressionResult soul = service.GrantSoul("p1", SoulIds.Kanayunan, 0, "soul");
            SoulProgressionResult unlocked = service.UnlockSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", soul.Snapshot.Revision, "unlock");

            SoulProgressionResult rejected = service.EquipSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", unlocked.Snapshot.Revision, "equip-a");
            SoulProgressionResult selected = service.SelectSoul(
                "p1", SoulIds.Kanayunan, unlocked.Snapshot.Revision, "select");
            SoulProgressionResult equipped = service.EquipSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", selected.Snapshot.Revision, "equip-b");

            Assert.That(rejected.Failure,
                Is.EqualTo(SoulProgressionFailure.SkillNotPermittedBySelectedSoul));
            Assert.That(equipped.Succeeded, Is.True);
        }

        [Test]
        public void Loadout_LimitsAllSkillsIncludingBasicAttackToFour()
        {
            SoulProgressionService service = Service();
            SoulProgressionSnapshot snapshot = service.InitializePlayer("p1").Snapshot;
            string[] firstFour = DefaultSkillCatalog.Create().NormalSkills.Take(4).Select(x => x.Id).ToArray();
            for (int i = 0; i < firstFour.Length; i++)
            {
                SoulProgressionResult result = service.EquipSkill(
                    "p1", firstFour[i], snapshot.Revision, "equip-" + i);
                Assert.That(result.Succeeded, Is.True);
                snapshot = result.Snapshot;
            }

            SoulProgressionResult fifth = service.EquipSkill(
                "p1", DefaultSkillCatalog.SuwerteId, snapshot.Revision, "equip-fifth");

            Assert.That(snapshot.EquippedSkillIds, Does.Contain(DefaultSkillCatalog.BasicAttackId));
            Assert.That(snapshot.EquippedSkillIds.Count, Is.EqualTo(4));
            Assert.That(fifth.Failure, Is.EqualTo(SoulProgressionFailure.SkillLimitReached));
        }

        [Test]
        public void CardAssemblySkillSource_ReturnsOnlyEquippedDefinitions()
        {
            SoulProgressionService service = Service();
            SoulProgressionSnapshot snapshot = service.InitializePlayer("p1").Snapshot;
            snapshot = service.EquipSkill(
                "p1", DefaultSkillCatalog.BasicAttackId, snapshot.Revision, "equip").Snapshot;

            Assert.That(service.TryGetSkills("p1", out var skills), Is.True);
            Assert.That(skills.Select(x => x.Id),
                Is.EqualTo(new[] { DefaultSkillCatalog.BasicAttackId }));
        }

        [Test]
        public void BattleDefeat_DoesNotMutateProgressionOwnership()
        {
            SoulProgressionService service = Service();
            service.InitializePlayer("p1", 5);
            SoulProgressionResult soul = service.GrantSoul("p1", SoulIds.Kanayunan, 0, "soul");
            SoulProgressionResult unlocked = service.UnlockSkill(
                "p1", "soul_kanayunan_sibat_ng_kawayan", soul.Snapshot.Revision, "unlock");

            Assert.That(service.TryGetSnapshot("p1", out SoulProgressionSnapshot afterBattle), Is.True);
            Assert.That(afterBattle.Revision, Is.EqualTo(unlocked.Snapshot.Revision));
            Assert.That(afterBattle.OwnedSoulIds, Does.Contain(SoulIds.Kanayunan));
            Assert.That(afterBattle.UnlockedSkillIds,
                Does.Contain("soul_kanayunan_sibat_ng_kawayan"));
        }

        [Test]
        public void NonAuthoritativeService_RejectsMutations()
        {
            var service = new SoulProgressionService(new FixedSoulProgressionAuthority(false));

            SoulProgressionResult result = service.InitializePlayer("p1", 100);

            Assert.That(result.Failure, Is.EqualTo(SoulProgressionFailure.NotAuthoritative));
            Assert.That(service.TryGetSnapshot("p1", out _), Is.False);
        }

        private static SoulProgressionService Service() =>
            new SoulProgressionService(new FixedSoulProgressionAuthority(true));
    }
}
