using System;

namespace ApirApir.AugmentedReality
{
    public sealed class QRCodeService
    {
        private const string Prefix = "APIRAR:1:";
        private const int CardIdLength = 32;

        public string CreatePayload(string cardId)
        {
            string normalized = Normalize(cardId).ToLowerInvariant();
            if (!IsValidCardId(normalized))
            {
                throw new ArgumentException("The card ID must be a 32-character hexadecimal identifier.", nameof(cardId));
            }

            return Prefix + normalized;
        }

        public bool TryParsePayload(string payload, out string cardId)
        {
            cardId = string.Empty;
            string normalized = Normalize(payload);
            if (!normalized.StartsWith(Prefix, StringComparison.Ordinal) ||
                normalized.Length != Prefix.Length + CardIdLength)
            {
                return false;
            }

            string candidate = normalized.Substring(Prefix.Length).ToLowerInvariant();
            if (!IsValidCardId(candidate))
            {
                return false;
            }

            cardId = candidate;
            return true;
        }

        public static bool IsValidCardId(string cardId)
        {
            if (cardId == null || cardId.Length != CardIdLength)
            {
                return false;
            }

            for (int i = 0; i < cardId.Length; i++)
            {
                char value = cardId[i];
                if (!((value >= '0' && value <= '9') ||
                      (value >= 'a' && value <= 'f') ||
                      (value >= 'A' && value <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }

    /// <summary>Adapter boundary for a real QR library. No camera decoding is performed here.</summary>
    public interface IQRCodeScanner : IDisposable
    {
        event Action<string> QRCodeDetected;
        bool IsRunning { get; }
        bool TryStart(out string failureReason);
        void Stop();
    }

    /// <summary>Logic-test scanner only; SubmitDecodedPayload simulates a decoder result.</summary>
    public sealed class MockQRCodeScanner : IQRCodeScanner
    {
        public event Action<string> QRCodeDetected;
        public bool IsRunning { get; private set; }

        public bool TryStart(out string failureReason)
        {
            IsRunning = true;
            failureReason = string.Empty;
            return true;
        }

        public void SubmitDecodedPayload(string payload)
        {
            if (IsRunning)
            {
                QRCodeDetected?.Invoke(payload);
            }
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public sealed class ARCardIdentificationService : IDisposable
    {
        private readonly IQRCodeScanner scanner;
        private readonly QRCodeService qrCodes;
        private readonly CardRegistrationService registrations;
        private bool subscribed;

        public ARCardIdentificationService(
            IQRCodeScanner scanner,
            CardRegistrationService registrations,
            QRCodeService qrCodes = null)
        {
            this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            this.registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            this.qrCodes = qrCodes ?? new QRCodeService();
        }

        public event Action<string> QRCodeDetected;
        public event Action<RegisteredARCard> CardIdentificationSucceeded;
        public event Action<string, ARCardFailure> CardIdentificationFailed;

        public bool Start(out string failureReason)
        {
            if (!subscribed)
            {
                scanner.QRCodeDetected += HandleDecodedPayload;
                subscribed = true;
            }

            return scanner.TryStart(out failureReason);
        }

        public void Stop()
        {
            scanner.Stop();
        }

        public void Dispose()
        {
            if (subscribed)
            {
                scanner.QRCodeDetected -= HandleDecodedPayload;
                subscribed = false;
            }

            scanner.Dispose();
        }

        private void HandleDecodedPayload(string payload)
        {
            QRCodeDetected?.Invoke(payload ?? string.Empty);
            if (!qrCodes.TryParsePayload(payload, out string cardId))
            {
                CardIdentificationFailed?.Invoke(string.Empty, ARCardFailure.InvalidRequest);
                return;
            }

            ARCardOperationResult result = registrations.Validate(cardId);
            if (result.Succeeded && result.Card != null)
            {
                CardIdentificationSucceeded?.Invoke(result.Card);
            }
            else
            {
                CardIdentificationFailed?.Invoke(cardId, result.Failure);
            }
        }
    }
}
