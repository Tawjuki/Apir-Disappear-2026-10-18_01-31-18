using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApirApir.Multiplayer
{
    public enum AccountAuthenticationState : byte
    {
        Uninitialized,
        Initializing,
        AuthenticationRequired,
        Registering,
        LoggingIn,
        Linking,
        Authenticated,
        SigningOut,
        Failed
    }

    public enum AccountAuthenticationStatus : byte
    {
        Success,
        Unchanged,
        InvalidUsername,
        InvalidPassword,
        UsernameUnavailable,
        InvalidCredentials,
        CredentialAlreadyLinked,
        AuthenticationRequired,
        AlreadyAuthenticated,
        AnonymousAccountRequiresLinking,
        NoRecoverableAnonymousSession,
        Busy,
        NetworkUnavailable,
        ServiceUnavailable,
        RateLimited,
        SessionExpired,
        Cancelled,
        Failed
    }

    public readonly struct AccountAuthenticationResult
    {
        public AccountAuthenticationResult(
            AccountAuthenticationStatus status,
            string playerId = null,
            string errorMessage = null)
        {
            Status = status;
            PlayerId = playerId == null ? string.Empty : playerId.Trim();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public AccountAuthenticationStatus Status { get; }
        public string PlayerId { get; }
        public string ErrorMessage { get; }
        public bool Succeeded =>
            (Status == AccountAuthenticationStatus.Success ||
             Status == AccountAuthenticationStatus.Unchanged) &&
            PlayerId.Length > 0;
    }

    public enum AccountBackendStatus : byte
    {
        Success,
        UsernameUnavailable,
        InvalidCredentials,
        CredentialAlreadyLinked,
        InvalidState,
        NetworkUnavailable,
        ServiceUnavailable,
        RateLimited,
        SessionExpired,
        Rejected,
        Cancelled,
        Failed
    }

    public readonly struct AccountBackendResult
    {
        public AccountBackendResult(
            AccountBackendStatus status,
            string errorMessage = null)
        {
            Status = status;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public AccountBackendStatus Status { get; }
        public string ErrorMessage { get; }
        public bool Succeeded => Status == AccountBackendStatus.Success;
    }

    public interface IAccountAuthenticationBackend
    {
        bool IsInitialized { get; }
        bool IsSignedIn { get; }
        bool HasCachedSession { get; }
        bool HasUsernamePassword { get; }
        string PlayerId { get; }

        Task<AccountBackendResult> InitializeAsync(
            CancellationToken cancellationToken = default);

        Task<AccountBackendResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default);

        Task<AccountBackendResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default);

        Task<AccountBackendResult> LinkUsernamePasswordAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default);

        Task<AccountBackendResult> ResumeAnonymousAsync(
            CancellationToken cancellationToken = default);

        void SignOut(bool clearCachedCredentials);
    }

    public interface IAccountSessionTerminator
    {
        Task TerminateActiveSessionAsync(
            CancellationToken cancellationToken = default);
    }

    public static class AccountCredentialPolicy
    {
        public const int MinimumUsernameLength = 3;
        public const int MaximumUsernameLength = 20;
        public const int MinimumPasswordLength = 8;
        public const int MaximumPasswordLength = 30;

        public static bool IsValidUsername(string username)
        {
            if (username == null ||
                username.Length < MinimumUsernameLength ||
                username.Length > MaximumUsernameLength)
            {
                return false;
            }

            for (int i = 0; i < username.Length; i++)
            {
                char character = username[i];
                bool valid = character >= 'a' && character <= 'z' ||
                             character >= 'A' && character <= 'Z' ||
                             character >= '0' && character <= '9' ||
                             character == '.' || character == '-' ||
                             character == '_' || character == '@';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsValidPassword(string password)
        {
            if (password == null ||
                password.Length < MinimumPasswordLength ||
                password.Length > MaximumPasswordLength)
            {
                return false;
            }

            bool hasLower = false;
            bool hasUpper = false;
            bool hasNumber = false;
            bool hasSymbol = false;
            for (int i = 0; i < password.Length; i++)
            {
                char character = password[i];
                if (char.IsControl(character))
                {
                    return false;
                }

                hasLower |= char.IsLower(character);
                hasUpper |= char.IsUpper(character);
                hasNumber |= char.IsDigit(character);
                hasSymbol |= !char.IsLetterOrDigit(character) &&
                             !char.IsWhiteSpace(character);
            }

            return hasLower && hasUpper && hasNumber && hasSymbol;
        }
    }
}
