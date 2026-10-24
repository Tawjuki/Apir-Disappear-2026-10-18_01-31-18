using UnityEngine;

namespace ApirApir.TaguTaguan
{
    public interface ITaguTaguanDetectionService
    {
        bool IsVisible(
            TaguTaguanWorldState taya,
            TaguTaguanWorldState hider,
            TaguTaguanSettings settings);

        bool IsWithinCatchingDistance(
            TaguTaguanWorldState taya,
            TaguTaguanWorldState hider,
            TaguTaguanSettings settings);
    }

    /// <summary>
    /// Stateless 3D visibility validation. Only obstacle layers are queried, so player
    /// colliders should not be included in the configured obstacle mask.
    /// </summary>
    public sealed class TaguTaguanPhysicsDetectionService : ITaguTaguanDetectionService
    {
        public bool IsVisible(
            TaguTaguanWorldState taya,
            TaguTaguanWorldState hider,
            TaguTaguanSettings settings)
        {
            if (settings == null || !taya.HasFiniteValues || !hider.HasFiniteValues)
            {
                return false;
            }

            Vector3 toHider = hider.Position - taya.Position;
            float squaredDistance = toHider.sqrMagnitude;
            float maximumDistance = settings.detectionDistance;
            if (squaredDistance > maximumDistance * maximumDistance)
            {
                return false;
            }

            if (squaredDistance > 0.0001f && settings.fieldOfViewDegrees < 360f &&
                Vector3.Angle(taya.Forward, toHider) > settings.fieldOfViewDegrees * 0.5f)
            {
                return false;
            }

            return !Physics.Linecast(
                taya.Position,
                hider.Position,
                settings.obstacleLayers,
                QueryTriggerInteraction.Ignore);
        }

        public bool IsWithinCatchingDistance(
            TaguTaguanWorldState taya,
            TaguTaguanWorldState hider,
            TaguTaguanSettings settings)
        {
            if (settings == null || !taya.HasFiniteValues || !hider.HasFiniteValues)
            {
                return false;
            }

            float distance = settings.catchingDistance;
            return (hider.Position - taya.Position).sqrMagnitude <= distance * distance;
        }
    }
}
