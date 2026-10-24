using UnityEngine;

namespace ApirApir.Patintero
{
    [CreateAssetMenu(menuName = "APIR APIR/Patintero Settings", fileName = "PatinteroSettings")]
    public sealed class PatinteroSettings : ScriptableObject
    {
        [Header("Court")]
        [Min(4f)] public float courtWidth = 12f;
        [Min(6f)] public float courtLength = 18f;
        [Tooltip("World-space X/Z center of the playable court.")]
        public Vector2 courtCenterXZ = Vector2.zero;
        [Min(0.25f)] public float playerRadius = 0.45f;
        [Min(0.25f)] public float endZoneDepth = 1.25f;
        public float[] guardingLineZ = { 0f };

        [Header("Movement and tagging")]
        [Min(0.5f)] public float runnerWalkSpeed = 3.75f;
        [Min(0.5f)] public float runnerSpeed = 5.5f;
        [Min(0.5f)] public float tayaSpeed = 6f;
        [Min(0.1f)] public float acceleration = 28f;
        [Min(0.1f)] public float deceleration = 36f;
        [Min(1f)] public float rotationSpeedDegrees = 720f;
        [Min(0.1f)] public float tagRadius = 1.15f;
        [Min(0f)] public float tagCooldownSeconds = 0.35f;
        [Range(10, 60)] public int inputSendRate = 30;
        [Range(0.1f, 2f)] public float inputKeepAliveSeconds = 0.5f;

        [Header("Round")]
        [Min(5f)] public float roundDurationSeconds = 30f;
        [Range(2, 4)] public int requiredPlayers = 4;
        [Range(1, 2)] public int tayaCount = 2;
        [Range(1, 2)] public int requiredRunnerCompletions = 1;

        public float HalfWidth => courtWidth * 0.5f;
        public float HalfLength => courtLength * 0.5f;
        public float MinimumX => courtCenterXZ.x - HalfWidth;
        public float MaximumX => courtCenterXZ.x + HalfWidth;
        public float MinimumZ => courtCenterXZ.y - HalfLength;
        public float MaximumZ => courtCenterXZ.y + HalfLength;
        public float StartThresholdZ => MinimumZ + endZoneDepth;
        public float FarThresholdZ => MaximumZ - endZoneDepth;
        public float MovementAcceleration => acceleration > 0f ? acceleration : 28f;
        public float MovementDeceleration => deceleration > 0f ? deceleration : 36f;
        public float RotationSpeed => rotationSpeedDegrees > 0f ? rotationSpeedDegrees : 720f;
        public float RunnerWalkSpeed => runnerWalkSpeed > 0f ? runnerWalkSpeed : 3.75f;
        public float RunnerRunSpeed => runnerSpeed > 0f ? runnerSpeed : 5.5f;
        public float TagCooldown => Mathf.Max(0f, tagCooldownSeconds);
        public float InputKeepAliveSeconds => Mathf.Clamp(inputKeepAliveSeconds, 0.1f, 2f);

        public float GetGuardLineZ(int index)
        {
            if (guardingLineZ == null || guardingLineZ.Length == 0)
            {
                return 0f;
            }

            return guardingLineZ[Mathf.Clamp(index, 0, guardingLineZ.Length - 1)];
        }

        private void OnValidate()
        {
            endZoneDepth = Mathf.Clamp(endZoneDepth, 0.25f, Mathf.Max(0.25f, courtLength * 0.35f));
            acceleration = acceleration > 0f ? Mathf.Max(0.1f, acceleration) : 28f;
            deceleration = deceleration > 0f ? Mathf.Max(0.1f, deceleration) : 36f;
            rotationSpeedDegrees = rotationSpeedDegrees > 0f
                ? Mathf.Max(1f, rotationSpeedDegrees)
                : 720f;
            runnerWalkSpeed = runnerWalkSpeed > 0f ? Mathf.Max(0.5f, runnerWalkSpeed) : 3.75f;
            runnerSpeed = runnerSpeed > 0f ? Mathf.Max(runnerWalkSpeed, runnerSpeed) : 5.5f;
            tagCooldownSeconds = Mathf.Max(0f, tagCooldownSeconds);
            inputKeepAliveSeconds = Mathf.Clamp(
                inputKeepAliveSeconds > 0f ? inputKeepAliveSeconds : 0.5f,
                0.1f,
                2f);
            requiredPlayers = Mathf.Clamp(requiredPlayers, 2, 4);
            tayaCount = requiredPlayers == 4
                ? 2
                : Mathf.Clamp(tayaCount, 1, Mathf.Min(2, requiredPlayers - 1));
            requiredRunnerCompletions = Mathf.Clamp(requiredRunnerCompletions, 1,
                Mathf.Max(1, requiredPlayers - tayaCount));
        }
    }
}
