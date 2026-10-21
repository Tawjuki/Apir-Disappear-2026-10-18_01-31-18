using System;
using UnityEngine;

namespace ApirApir.Patintero
{
    [DisallowMultipleComponent]
    public sealed class PatinteroGuardPath : MonoBehaviour
    {
        [Serializable]
        private sealed class LaneSegment
        {
            [Tooltip("The two scene transforms defining this permitted line segment.")]
            public Transform start = null;
            public Transform end = null;

            [Tooltip("Indices of segments this guard may enter at a shared endpoint.")]
            public int[] permittedConnections = Array.Empty<int>();
        }

        [SerializeField] private LaneSegment[] segments = Array.Empty<LaneSegment>();
        [SerializeField, Min(0.01f)] private float intersectionSnapDistance = 0.45f;
        [SerializeField, Range(0f, 1f)] private float turnInputThreshold = 0.55f;
        [SerializeField, Range(0f, 0.5f)] private float turnPreference = 0.12f;

        public bool HasUsableSegments
        {
            get
            {
                if (segments == null)
                {
                    return false;
                }

                for (int i = 0; i < segments.Length; i++)
                {
                    if (TryGetSegment(i, out _, out _, out _, out _))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public Vector3 MoveAndConstrain(
            Vector3 currentPosition,
            Vector2 planarVelocity,
            Vector2 inputIntent,
            float deltaTime,
            ref int activeSegmentIndex)
        {
            if (!EnsureActiveSegment(currentPosition, ref activeSegmentIndex) ||
                !TryGetSegment(activeSegmentIndex, out Vector3 start, out Vector3 end,
                    out Vector3 tangent, out float length))
            {
                return currentPosition;
            }

            Vector3 projected = ProjectOntoSegment(currentPosition, start, tangent, length);
            float distance = Vector3.Dot(projected - start, tangent);
            Vector3 worldIntent = new(inputIntent.x, 0f, inputIntent.y);

            if (worldIntent.sqrMagnitude > 0.0001f &&
                TrySelectConnectedSegment(
                    activeSegmentIndex,
                    projected,
                    worldIntent.normalized,
                    start,
                    end,
                    tangent,
                    out int connectedIndex,
                    out Vector3 intersection))
            {
                activeSegmentIndex = connectedIndex;
                TryGetSegment(activeSegmentIndex, out start, out end, out tangent, out length);
                projected = ProjectOntoSegment(intersection, start, tangent, length);
                distance = Vector3.Dot(projected - start, tangent);
            }

            Vector3 velocity = new(planarVelocity.x, 0f, planarVelocity.y);
            distance = Mathf.Clamp(
                distance + Vector3.Dot(velocity, tangent) * Mathf.Max(0f, deltaTime),
                0f,
                length);

            Vector3 result = start + tangent * distance;
            result.y = currentPosition.y;
            return result;
        }

        public Vector3 SnapToPath(Vector3 position, ref int activeSegmentIndex)
        {
            if (!EnsureActiveSegment(position, ref activeSegmentIndex) ||
                !TryGetSegment(activeSegmentIndex, out Vector3 start, out _,
                    out Vector3 tangent, out float length))
            {
                return position;
            }

            Vector3 result = ProjectOntoSegment(position, start, tangent, length);
            result.y = position.y;
            return result;
        }

        private bool EnsureActiveSegment(Vector3 position, ref int activeSegmentIndex)
        {
            if (TryGetSegment(activeSegmentIndex, out _, out _, out _, out _))
            {
                return true;
            }

            float bestDistance = float.PositiveInfinity;
            int bestIndex = -1;
            for (int i = 0; segments != null && i < segments.Length; i++)
            {
                if (!TryGetSegment(i, out Vector3 start, out _, out Vector3 tangent, out float length))
                {
                    continue;
                }

                Vector3 candidate = ProjectOntoSegment(position, start, tangent, length);
                candidate.y = position.y;
                float distance = (candidate - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            activeSegmentIndex = bestIndex;
            return bestIndex >= 0;
        }

        private bool TrySelectConnectedSegment(
            int currentIndex,
            Vector3 position,
            Vector3 intent,
            Vector3 currentStart,
            Vector3 currentEnd,
            Vector3 currentTangent,
            out int selectedIndex,
            out Vector3 intersection)
        {
            selectedIndex = -1;
            intersection = position;
            LaneSegment current = segments[currentIndex];
            if (current.permittedConnections == null || current.permittedConnections.Length == 0)
            {
                return false;
            }

            float distanceFromStart = Vector3.Distance(position, currentStart);
            float distanceFromEnd = Vector3.Distance(position, currentEnd);
            bool atStart = distanceFromStart <= intersectionSnapDistance;
            bool atEnd = distanceFromEnd <= intersectionSnapDistance;
            if (!atStart && !atEnd)
            {
                return false;
            }

            intersection = atStart && (!atEnd || distanceFromStart <= distanceFromEnd)
                ? currentStart
                : currentEnd;
            Vector3 currentAwayDirection = intersection == currentStart ? currentTangent : -currentTangent;
            float currentAlignment = Mathf.Max(0f, Vector3.Dot(intent, currentAwayDirection));
            float bestAlignment = turnInputThreshold;

            for (int i = 0; i < current.permittedConnections.Length; i++)
            {
                int candidateIndex = current.permittedConnections[i];
                if (candidateIndex == currentIndex ||
                    !TryGetSegment(candidateIndex, out Vector3 candidateStart, out Vector3 candidateEnd,
                        out Vector3 candidateTangent, out _))
                {
                    continue;
                }

                bool sharesStart = HorizontalSqrDistance(candidateStart, intersection) <=
                                   intersectionSnapDistance * intersectionSnapDistance;
                bool sharesEnd = HorizontalSqrDistance(candidateEnd, intersection) <=
                                 intersectionSnapDistance * intersectionSnapDistance;
                if (!sharesStart && !sharesEnd)
                {
                    continue;
                }

                Vector3 awayDirection = sharesStart ? candidateTangent : -candidateTangent;
                float alignment = Vector3.Dot(intent, awayDirection);
                if (alignment >= bestAlignment && alignment >= currentAlignment + turnPreference)
                {
                    bestAlignment = alignment;
                    selectedIndex = candidateIndex;
                }
            }

            return selectedIndex >= 0;
        }

        private bool TryGetSegment(
            int index,
            out Vector3 start,
            out Vector3 end,
            out Vector3 tangent,
            out float length)
        {
            start = default;
            end = default;
            tangent = default;
            length = 0f;
            if (segments == null || index < 0 || index >= segments.Length)
            {
                return false;
            }

            LaneSegment segment = segments[index];
            if (segment == null || segment.start == null || segment.end == null)
            {
                return false;
            }

            start = segment.start.position;
            end = segment.end.position;
            start.y = 0f;
            end.y = 0f;
            tangent = end - start;
            length = tangent.magnitude;
            if (length <= 0.001f)
            {
                return false;
            }

            tangent /= length;
            return true;
        }

        private static Vector3 ProjectOntoSegment(
            Vector3 position,
            Vector3 start,
            Vector3 tangent,
            float length)
        {
            position.y = 0f;
            float distance = Mathf.Clamp(Vector3.Dot(position - start, tangent), 0f, length);
            return start + tangent * distance;
        }

        private static float HorizontalSqrDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return (left - right).sqrMagnitude;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            intersectionSnapDistance = Mathf.Max(0.01f, intersectionSnapDistance);
            turnInputThreshold = Mathf.Clamp01(turnInputThreshold);
            turnPreference = Mathf.Clamp(turnPreference, 0f, 0.5f);
        }

        private void OnDrawGizmosSelected()
        {
            if (segments == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.9f);
            for (int i = 0; i < segments.Length; i++)
            {
                LaneSegment segment = segments[i];
                if (segment == null || segment.start == null || segment.end == null)
                {
                    continue;
                }

                Gizmos.DrawLine(segment.start.position, segment.end.position);
                Gizmos.DrawWireSphere(segment.start.position, intersectionSnapDistance);
                Gizmos.DrawWireSphere(segment.end.position, intersectionSnapDistance);
            }
        }
#endif
    }
}
