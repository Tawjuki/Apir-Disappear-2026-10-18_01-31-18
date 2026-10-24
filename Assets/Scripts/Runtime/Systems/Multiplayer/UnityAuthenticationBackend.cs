using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace ApirApir.Multiplayer
{
    /// <summary>
    /// The only adapter that calls the Unity Authentication SDK. It deliberately retains no
    /// username or password fields and returns user-safe errors without credential details.
    /// </summary>
    public sealed class UnityAuthenticationBackend : IAccountAuthenticationBackend
    {
        private enum Operation : byte
        {
            Initialize,
            Register,
            Login,
            Link,
            ResumeAnonymous
        }

        public bool IsInitialized =>
            UnityServices.State == ServicesInitializationState.Initialized;

        public bool IsSignedIn =>
            IsInitialized && AuthenticationService.Instance.IsSignedIn;

        public bool HasCachedSession =>
            IsInitialized && AuthenticationService.Instance.SessionTokenExists;

        public bool HasUsernamePassword =>
            IsSignedIn &&
            !string.IsNullOrWhiteSpace(AuthenticationService.Instance.PlayerInfo?.Username);

        public string PlayerId => IsSignedIn
            ? AuthenticationService.Instance.PlayerId ?? string.Empty
            : string.Empty;

        public async Task<AccountBackendResult> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }

                return new AccountBackendResult(AccountBackendStatus.Success);
            }
            catch (OperationCanceledException)
            {
                return new AccountBackendResult(AccountBackendStatus.Cancelled);
            }
            catch (RequestFailedException exception)
            {
                return MapException(exception, Operation.Initialize);
            }
            catch (Exception)
            {
                return new AccountBackendResult(
                    AccountBackendStatus.ServiceUnavailable,
                    "Unity Services could not be initialized.");
            }
        }

        public Task<AccountBackendResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                () => AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(
                    username,
                    password),
                Operation.Register,
                cancellationToken);
        }

        public Task<AccountBackendResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                () => AuthenticationService.Instance.SignInWithUsernamePasswordAsync(
                    username,
                    password),
                Operation.Login,
                cancellationToken);
        }

        public Task<AccountBackendResult> LinkUsernamePasswordAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                () => AuthenticationService.Instance.AddUsernamePasswordAsync(
                    username,
                    password),
                Operation.Link,
                cancellationToken);
        }

        public Task<AccountBackendResult> ResumeAnonymousAsync(
            CancellationToken cancellationToken = default)
        {
            if (!HasCachedSession)
            {
                return Task.FromResult(new AccountBackendResult(
                    AccountBackendStatus.InvalidState,
                    "No cached anonymous session is available."));
            }

            return ExecuteAsync(
                () => AuthenticationService.Instance.SignInAnonymouslyAsync(),
                Operation.ResumeAnonymous,
                cancellationToken);
        }

        public void SignOut(bool clearCachedCredentials)
        {
            if (IsSignedIn)
            {
                AuthenticationService.Instance.SignOut(clearCachedCredentials);
            }
        }

        private static async Task<AccountBackendResult> ExecuteAsync(
            Func<Task> operation,
            Operation operationType,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await operation();
                return new AccountBackendResult(AccountBackendStatus.Success);
            }
            catch (OperationCanceledException)
            {
                return new AccountBackendResult(AccountBackendStatus.Cancelled);
            }
            catch (RequestFailedException exception)
            {
                return MapException(exception, operationType);
            }
            catch (Exception)
            {
                return new AccountBackendResult(
                    AccountBackendStatus.Failed,
                    "Authentication could not be completed.");
            }
        }

        private static AccountBackendResult MapException(
            RequestFailedException exception,
            Operation operation)
        {
            int code = exception.ErrorCode;
            if (code == CommonErrorCodes.TransportError ||
                code == CommonErrorCodes.Timeout)
            {
                return new AccountBackendResult(
                    AccountBackendStatus.NetworkUnavailable,
                    "Authentication could not reach Unity Services.");
            }

            if (code == CommonErrorCodes.ServiceUnavailable)
            {
                return new AccountBackendResult(
                    AccountBackendStatus.ServiceUnavailable,
                    "Unity Authentication is temporarily unavailable.");
            }

            if (code == CommonErrorCodes.TooManyRequests)
            {
                return new AccountBackendResult(
                    AccountBackendStatus.RateLimited,
                    "Too many authentication attempts. Try again later.");
            }

            if (code == CommonErrorCodes.TokenExpired ||
                code == CommonErrorCodes.InvalidToken ||
                code == AuthenticationErrorCodes.InvalidSessionToken)
            {
                return operation == Operation.Login
                    ? InvalidCredentials()
                    : new AccountBackendResult(
                        AccountBackendStatus.SessionExpired,
                        "The authenticated session expired. Sign in again.");
            }

            if (operation == Operation.Register &&
                (code == CommonErrorCodes.Conflict ||
                 code == AuthenticationErrorCodes.AccountAlreadyLinked))
            {
                return new AccountBackendResult(
                    AccountBackendStatus.UsernameUnavailable,
                    "The account could not be registered with those credentials.");
            }

            if (operation == Operation.Link &&
                (code == CommonErrorCodes.Conflict ||
                 code == AuthenticationErrorCodes.AccountAlreadyLinked ||
                 code == AuthenticationErrorCodes.AccountLinkLimitExceeded))
            {
                return new AccountBackendResult(
                    AccountBackendStatus.CredentialAlreadyLinked,
                    "Those credentials cannot be linked to this account.");
            }

            if (operation == Operation.Login &&
                (code == AuthenticationErrorCodes.InvalidParameters ||
                 code == CommonErrorCodes.InvalidRequest ||
                 code == CommonErrorCodes.NotFound ||
                 code == CommonErrorCodes.Forbidden))
            {
                return InvalidCredentials();
            }

            if (code == AuthenticationErrorCodes.ClientInvalidUserState)
            {
                return new AccountBackendResult(
                    AccountBackendStatus.InvalidState,
                    "Authentication is not available in the current account state.");
            }

            if (code == AuthenticationErrorCodes.InvalidParameters ||
                code == CommonErrorCodes.InvalidRequest ||
                code == CommonErrorCodes.Forbidden)
            {
                return new AccountBackendResult(
                    AccountBackendStatus.Rejected,
                    "Unity Authentication rejected the request.");
            }

            return new AccountBackendResult(
                AccountBackendStatus.Failed,
                "Authentication could not be completed.");
        }

        private static AccountBackendResult InvalidCredentials()
        {
            // Deliberately does not disclose whether a username exists.
            return new AccountBackendResult(
                AccountBackendStatus.InvalidCredentials,
                "The username or password is incorrect.");
        }
    }

    internal sealed class NetworkSessionAccountTerminator : IAccountSessionTerminator
    {
        public async Task TerminateActiveSessionAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NetworkSessionService session = NetworkSessionService.Instance;
            if (session == null || !session.IsConnected)
            {
                return;
            }

            await session.LeaveAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (session.IsConnected)
            {
                throw new InvalidOperationException(
                    "The active multiplayer session could not be closed before logout.");
            }
        }
    }

    /// <summary>Process-wide account session shared by multiplayer and Cloud Profile.</summary>
    public static class UnityAccountAuthenticationRuntime
    {
        private static readonly AccountAuthenticationService SharedService =
            new AccountAuthenticationService(
                new UnityAuthenticationBackend(),
                new NetworkSessionAccountTerminator());

        public static AccountAuthenticationService Service => SharedService;
        public static bool IsSignedIn => SharedService.IsAuthenticated;
        public static string PlayerId => SharedService.PlayerId;
    }
}
