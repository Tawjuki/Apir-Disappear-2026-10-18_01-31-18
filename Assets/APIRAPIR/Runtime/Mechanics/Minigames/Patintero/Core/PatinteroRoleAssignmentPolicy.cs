using System;
using System.Collections.Generic;

namespace ApirApir.Patintero
{
    /// <summary>Patintero-owned policy for its supported 2v2 and 1v3 configurations.</summary>
    public sealed class PatinteroRoleAssignmentPolicy
    {
        public bool TryCreate(
            IReadOnlyList<string> activeContenderIds,
            IReadOnlyList<string> eliminatedPlayerIds,
            int randomSeed,
            out IReadOnlyList<PatinteroAssignmentEntry> assignments)
        {
            assignments = Array.Empty<PatinteroAssignmentEntry>();
            if (!TryCopyUnique(activeContenderIds, out List<string> contenders) ||
                !TryCopyUnique(eliminatedPlayerIds, out List<string> eliminated) ||
                HasOverlap(contenders, eliminated) || contenders.Count + eliminated.Count != 4)
            {
                return false;
            }

            var random = new Random(randomSeed);
            Shuffle(contenders, random);
            Shuffle(eliminated, random);
            var result = new List<PatinteroAssignmentEntry>(4);

            switch (contenders.Count)
            {
                case 4:
                    result.Add(new PatinteroAssignmentEntry(contenders[0], PatinteroRole.Runner, 0));
                    result.Add(new PatinteroAssignmentEntry(contenders[1], PatinteroRole.Runner, 0));
                    result.Add(new PatinteroAssignmentEntry(contenders[2], PatinteroRole.Taya, 1));
                    result.Add(new PatinteroAssignmentEntry(contenders[3], PatinteroRole.Taya, 1));
                    break;
                case 3 when eliminated.Count == 1:
                    for (int i = 0; i < contenders.Count; i++)
                    {
                        result.Add(new PatinteroAssignmentEntry(contenders[i], PatinteroRole.Runner, 0));
                    }

                    result.Add(new PatinteroAssignmentEntry(eliminated[0], PatinteroRole.Taya, 1));
                    break;
                case 2 when eliminated.Count == 2:
                    // Each team has one finalist and one eliminated player. Team A runs;
                    // Team B guards for this opportunity.
                    result.Add(new PatinteroAssignmentEntry(contenders[0], PatinteroRole.Runner, 0));
                    result.Add(new PatinteroAssignmentEntry(eliminated[0], PatinteroRole.Runner, 0));
                    result.Add(new PatinteroAssignmentEntry(contenders[1], PatinteroRole.Taya, 1));
                    result.Add(new PatinteroAssignmentEntry(eliminated[1], PatinteroRole.Taya, 1));
                    break;
                default:
                    return false;
            }

            assignments = result.AsReadOnly();
            return true;
        }

        private static bool TryCopyUnique(
            IReadOnlyList<string> source,
            out List<string> result)
        {
            result = new List<string>();
            if (source == null)
            {
                return false;
            }

            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                string value = source[i] == null ? string.Empty : source[i].Trim();
                if (value.Length == 0 || !unique.Add(value))
                {
                    return false;
                }

                result.Add(value);
            }

            return true;
        }

        private static bool HasOverlap(List<string> first, List<string> second)
        {
            var values = new HashSet<string>(first, StringComparer.Ordinal);
            for (int i = 0; i < second.Count; i++)
            {
                if (values.Contains(second[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Shuffle(List<string> values, Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (values[i], values[other]) = (values[other], values[i]);
            }
        }
    }
}
