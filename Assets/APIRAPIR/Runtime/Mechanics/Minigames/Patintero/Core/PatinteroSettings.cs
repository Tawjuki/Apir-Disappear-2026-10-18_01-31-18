using UnityEngine;

namespace ApirApir.Patintero
{
    [CreateAssetMenu(menuName = "APIR APIR/Patintero Settings", fileName = "PatinteroSettings")]
    public sealed class PatinteroSettings : ScriptableObject
    {
        [Header("Court")]
        [Min(4f)] public float courtWidth = 12f;
        [Min(6f)] public float courtLength = 18f;
        [Min(0.25f)] public float playerRadius = 0.45f;
        [Min(0.25f)] public float endZoneDepth = 1.25f;
        public float[] guardingLineZ = { 0f };

        [Header("Movement and tagging")]
        [Min(0.5f)] public float runnerSpeed = 5.5f;
        [Min(0.5f)] public float tayaSpeed = 6f;
        [Min(0.1f)] public float tagRadius = 1.15f;
        [Range(10, 60)] public int inputSendRate = 30;

        [Header("Round")]
        [Min(5f)] public float roundDurationSeconds = 30f;
        [Range(2, 4)] public int requiredPlayers = 4;
        [Range(1, 2)] public int tayaCount = 1;

        public float HalfWidth => courtWidth * 0.5f;
        public float HalfLength => courtLength * 0.5f;
        public float StartThresholdZ => -HalfLength + endZoneDepth;
        public float FarThresholdZ => HalfLength - endZoneDepth;

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
            requiredPlayers = Mathf.Clamp(requiredPlayers, 2, 4);
            tayaCount = Mathf.Clamp(tayaCount, 1, Mathf.Min(2, requiredPlayers - 1));
        }
    }
}
