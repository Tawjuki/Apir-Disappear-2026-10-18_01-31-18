using NUnit.Framework;
using UnityEngine;

namespace ApirApir.Patintero.Tests
{
    public sealed class PatinteroRulesTests
    {
        private PatinteroSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<PatinteroSettings>();
            settings.courtWidth = 12f;
            settings.courtLength = 18f;
            settings.playerRadius = 0.5f;
            settings.endZoneDepth = 1.25f;
            settings.runnerSpeed = 6f;
            settings.tayaSpeed = 7f;
            settings.tagRadius = 1.1f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void RunnerMovement_NormalizesDiagonalAndClampsToCourt()
        {
            Vector3 moved = PatinteroRules.MoveAndConstrain(
                new Vector3(5.4f, 1f, 8.4f),
                Vector2.one,
                1f,
                PatinteroRole.Runner,
                0f,
                settings);

            Assert.That(moved.x, Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(moved.z, Is.EqualTo(8.5f).Within(0.001f));
            Assert.That(moved.y, Is.EqualTo(1f));
        }

        [Test]
        public void TayaMovement_RemainsOnAssignedLine()
        {
            Vector3 moved = PatinteroRules.MoveAndConstrain(
                new Vector3(0f, 1f, 3f),
                new Vector2(1f, 1f),
                0.5f,
                PatinteroRole.Taya,
                -2f,
                settings);

            Assert.That(moved.z, Is.EqualTo(-2f).Within(0.001f));
            Assert.That(moved.x, Is.GreaterThan(0f));
        }

        [Test]
        public void Runner_MustReachFarSideBeforeReturnCounts()
        {
            RunnerProgress atStart = PatinteroRules.AdvanceRunner(
                RunnerProgress.Outbound,
                settings.StartThresholdZ,
                settings);
            RunnerProgress atFar = PatinteroRules.AdvanceRunner(
                atStart,
                settings.FarThresholdZ,
                settings);
            RunnerProgress returned = PatinteroRules.AdvanceRunner(
                atFar,
                settings.StartThresholdZ,
                settings);

            Assert.That(atStart, Is.EqualTo(RunnerProgress.Outbound));
            Assert.That(atFar, Is.EqualTo(RunnerProgress.Returning));
            Assert.That(returned, Is.EqualTo(RunnerProgress.Completed));
        }

        [Test]
        public void Tag_UsesConfiguredHorizontalRadius()
        {
            Assert.That(PatinteroRules.IsTag(Vector3.zero, new Vector3(1.09f, 5f, 0f), settings.tagRadius), Is.True);
            Assert.That(PatinteroRules.IsTag(Vector3.zero, new Vector3(1.11f, 0f, 0f), settings.tagRadius), Is.False);
        }

        [TestCase(2, false, false, PatinteroResult.None)]
        [TestCase(0, false, false, PatinteroResult.TayaWins)]
        [TestCase(2, false, true, PatinteroResult.TayaWins)]
        [TestCase(0, true, true, PatinteroResult.RunnersWin)]
        public void ResultEvaluation_IsDeterministic(int activeRunners, bool completed, bool expired, PatinteroResult expected)
        {
            Assert.That(PatinteroRules.EvaluateResult(activeRunners, completed, expired), Is.EqualTo(expected));
        }
    }
}
