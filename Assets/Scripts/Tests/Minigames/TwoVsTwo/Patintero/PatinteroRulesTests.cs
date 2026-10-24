using NUnit.Framework;
using ApirApir.Minigames;
using UnityEditor;
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
        public void RunnerMovement_UsesConfiguredWorldSpaceCourtCenter()
        {
            settings.courtCenterXZ = new Vector2(2f, 3f);

            Vector3 constrained = PatinteroRules.ConstrainToCourt(
                new Vector3(100f, 1f, -100f),
                PatinteroRole.Runner,
                0f,
                settings);

            Assert.That(constrained.x, Is.EqualTo(7.5f).Within(0.001f));
            Assert.That(constrained.z, Is.EqualTo(-5.5f).Within(0.001f));
            Assert.That(settings.StartThresholdZ, Is.EqualTo(-4.75f).Within(0.001f));
            Assert.That(settings.FarThresholdZ, Is.EqualTo(10.75f).Within(0.001f));
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
        public void MovementVelocity_NormalizesDiagonalAndUsesSeparateDeceleration()
        {
            Vector2 accelerated = MinigameMovementMath.MoveVelocityTowards(
                Vector2.zero,
                Vector2.one,
                6f,
                100f,
                2f,
                1f);
            Vector2 decelerated = MinigameMovementMath.MoveVelocityTowards(
                accelerated,
                Vector2.zero,
                6f,
                100f,
                2f,
                1f);

            Assert.That(accelerated.magnitude, Is.EqualTo(6f).Within(0.001f));
            Assert.That(decelerated.magnitude, Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void CameraRelativeInput_UsesCamerasHorizontalOrientation()
        {
            GameObject cameraObject = new("Test Camera");
            try
            {
                cameraObject.transform.rotation = Quaternion.Euler(35f, 180f, 0f);

                Vector2 up = MinigameMovementMath.ToCameraRelative(
                    Vector2.up,
                    cameraObject.transform);
                Vector2 right = MinigameMovementMath.ToCameraRelative(
                    Vector2.right,
                    cameraObject.transform);

                Assert.That(up.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(up.y, Is.EqualTo(-1f).Within(0.001f));
                Assert.That(right.x, Is.EqualTo(-1f).Within(0.001f));
                Assert.That(right.y, Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void SharedInputFrame_TracksContextActionSlotsIndependently()
        {
            MinigameInputFrame frame = new(
                new Vector2(2f, 0f),
                MinigameActionMask.Primary | MinigameActionMask.Modifier,
                MinigameActionMask.Primary,
                MinigameActionMask.Secondary);

            Assert.That(frame.Movement.magnitude, Is.EqualTo(1f).Within(0.001f));
            Assert.That(frame.IsHeld(MinigameActionSlot.Primary), Is.True);
            Assert.That(frame.IsHeld(MinigameActionSlot.Modifier), Is.True);
            Assert.That(frame.WasPressed(MinigameActionSlot.Primary), Is.True);
            Assert.That(frame.WasReleased(MinigameActionSlot.Secondary), Is.True);
            Assert.That(frame.IsHeld(MinigameActionSlot.Secondary), Is.False);
            Assert.That(MinigameInputFrame.ToMask((MinigameActionSlot)byte.MaxValue),
                Is.EqualTo(MinigameActionMask.None));
        }

        [Test]
        public void GuardPath_AllowsOnlyConfiguredIntersectionTurn()
        {
            GameObject pathObject = new("Guard Path");
            GameObject start = new("Horizontal Start");
            GameObject intersection = new("Intersection");
            GameObject end = new("Vertical End");

            try
            {
                start.transform.position = new Vector3(-2f, 0f, 0f);
                intersection.transform.position = Vector3.zero;
                end.transform.position = new Vector3(0f, 0f, 2f);

                PatinteroGuardPath path = pathObject.AddComponent<PatinteroGuardPath>();
                SerializedObject serializedPath = new(path);
                SerializedProperty segments = serializedPath.FindProperty("segments");
                segments.arraySize = 2;
                SetSegment(segments.GetArrayElementAtIndex(0), start.transform, intersection.transform, 1);
                SetSegment(segments.GetArrayElementAtIndex(1), intersection.transform, end.transform);
                serializedPath.ApplyModifiedPropertiesWithoutUndo();

                int activeSegment = 0;
                Vector3 moved = path.MoveAndConstrain(
                    new Vector3(-0.1f, 1f, 0f),
                    new Vector2(0f, 2f),
                    Vector2.up,
                    0.25f,
                    ref activeSegment);

                Assert.That(activeSegment, Is.EqualTo(1));
                Assert.That(moved.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(moved.z, Is.GreaterThan(0f));
                Assert.That(moved.y, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(pathObject);
                Object.DestroyImmediate(start);
                Object.DestroyImmediate(intersection);
                Object.DestroyImmediate(end);
            }
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

        [Test]
        public void ResultEvaluation_UsesConfiguredRunnerCompletionRequirement()
        {
            Assert.That(PatinteroRules.EvaluateResult(1, 1, 2, false), Is.EqualTo(PatinteroResult.None));
            Assert.That(PatinteroRules.EvaluateResult(0, 1, 2, false), Is.EqualTo(PatinteroResult.TayaWins));
            Assert.That(PatinteroRules.EvaluateResult(0, 2, 2, false), Is.EqualTo(PatinteroResult.RunnersWin));
        }

        private static void SetSegment(
            SerializedProperty segment,
            Transform start,
            Transform end,
            params int[] connections)
        {
            segment.FindPropertyRelative("start").objectReferenceValue = start;
            segment.FindPropertyRelative("end").objectReferenceValue = end;
            SerializedProperty permittedConnections = segment.FindPropertyRelative("permittedConnections");
            permittedConnections.arraySize = connections.Length;
            for (int i = 0; i < connections.Length; i++)
            {
                permittedConnections.GetArrayElementAtIndex(i).intValue = connections[i];
            }
        }
    }
}
