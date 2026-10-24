using System;
using System.Text;

namespace ApirApir.PlayerProfiles
{
    public sealed class PlayerNamePolicy
    {
        public const int DefaultMaximumLength = 24;

        public PlayerNamePolicy(int maximumLength = DefaultMaximumLength)
        {
            if (maximumLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumLength));
            }

            MaximumLength = maximumLength;
        }

        public int MaximumLength { get; }

        public bool TryNormalize(string requestedName, out string normalizedName)
        {
            normalizedName = string.Empty;
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                return false;
            }

            string source;
            try
            {
                source = requestedName.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var builder = new StringBuilder(Math.Min(source.Length, MaximumLength));
            bool pendingSpace = false;
            for (int i = 0; i < source.Length; i++)
            {
                char character = source[i];
                if (char.IsControl(character))
                {
                    return false;
                }

                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(character);
                if (builder.Length > MaximumLength)
                {
                    return false;
                }
            }

            normalizedName = builder.ToString();
            return normalizedName.Length > 0 && normalizedName.Length <= MaximumLength;
        }
    }
}
