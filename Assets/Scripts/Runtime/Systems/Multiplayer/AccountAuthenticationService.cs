using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApirApir.Multiplayer
{
    /// <summary>
    /// Serializes account operations and owns no credentials. Username and password values are
    /// forwarded directly to the authentication backend and are never cached or logged.
    /// </summary>
    public sealed class AccountAuthenticationService
    {
        private readonly IAccountAuthenticationBackend backend;
        private readonly IAccountSessionTerminator sessionTerminator;
        private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);

        public AccountAuthenticationService(
            IAccountAuthenticationBackend backend,
            IAccountSessionTerminator sessionTerminator = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.sessionTerminator = sessionTerminator;
        }

        public AccountAuthenticationState State { get; private set; } =
            AccountAuthenticationState.Uninitialized;

        public string PlayerId => backend.IsSignedIn
            ? backend.PlayerId ?? string.Empty
            : string.Empty;

        public bool IsAuthenticated => backend.IsSignedIn && PlayerId.Length > 0;
        public bool HasUsernamePassword => IsAuthenticated && backend.HasUsernamePassword;
        public bool IsAnonymous => IsAuthenticated && !backend.HasUsernamePassword;
        public bool HasRecoverableCachedSession => backend.HasCachedSession;

        public event Action<AccountAuthenticationState> StateChanged;
        public event Action<string> Authenticated;
        public event Action SignedOut;
        public event Action<AccountAuthenticationResult> OperationFailed;

        public async Task<AccountAuthenticationResult> InitializeAuthenticationAsync(
            CancellationToken cancellationToken = default)
        {
            if (!await TryEnterAsync(cancellationToken))
            {
                return BusyOrCancelled(cancellationToken);
            }

            try
            {
                SetState(AccountAuthenticationState.Initializing);
                AccountBackendResult initialized = await backend.InitializeAsync(cancellationToken);
                if (!initialized.Succeeded)
                {
                    return Fail(Map(initialized), initialized.ErrorMessage);
                }

                if (!backend.IsSignedIn || string.IsNullOrWhiteSpace(backend.PlayerId))
                {
                    SetState(AccountAuthenticationState.AuthenticationRequired);
                    return new AccountAuthenticationResult(
                        AccountAuthenticationStatus.AuthenticationRequired);
                }

                return CompleteAuthentication(AccountAuthenticationStatus.Unchanged);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public Task<AccountAuthenticationResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return AuthenticateWithCredentialsAsync(
                username,
                password,
                true,
                cancellationToken);
        }

        public Task<AccountAuthenticationResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return AuthenticateWithCredentialsAsync(
                username,
                password,
                false,
                cancellationToken);
        }

        public async Task<AccountAuthenticationResult> LinkUsernamePasswordAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            AccountAuthenticationResult validation = ValidateCredentials(username, password);
            if (validation.Status != AccountAuthenticationStatus.Success)
            {
                return validation;
            }

            if (!await TryEnterAsync(cancellationToken))
            {
                return BusyOrCancelled(cancellationToken);
            }

            try
            {
                AccountBackendResult initialized = await EnsureInitializedAsync(cancellationToken);
                if (!initialized.Succeeded)
                {
                    return Fail(Map(initialized), initialized.ErrorMessage);
                }

                if (!IsAuthenticated)
                {
                    return Fail(
                        AccountAuthenticationStatus.AuthenticationRequired,
                        "Sign in to the anonymous account before linking credentials.");
                }

                if (backend.HasUsernamePassword)
                {
                    return Fail(
                        AccountAuthenticationStatus.CredentialAlreadyLinked,
                        "This account already has username and password credentials.");
                }

                string originalPlayerId = PlayerId;
                SetState(AccountAuthenticationState.Linking);
                AccountBackendResult linked = await backend.LinkUsernamePasswordAsync(
                    username,
                    password,
                    cancellationToken);
                if (!linked.Succeeded)
                {
                    return Fail(Map(linked), linked.ErrorMessage);
                }

                if (!backend.IsSignedIn ||
                    !string.Equals(originalPlayerId, backend.PlayerId, StringComparison.Ordinal))
                {
                    return Fail(
                        AccountAuthenticationStatus.Failed,
                        "The linked account did not retain its authenticated Player ID.");
                }

                return CompleteAuthentication(AccountAuthenticationStatus.Success);
            }
            finally
            {
                operationGate.Release();
            }
        }

        /// <summary>
        /// Explicitly restores a cached anonymous session. It refuses to create a new anonymous
        /// account when no recoverable session token exists.
        /// </summary>
        public async Task<AccountAuthenticationResult> ResumeAnonymousAsync(
            CancellationToken cancellationToken = default)
        {
            if (!await TryEnterAsync(cancellationToken))
            {
                return BusyOrCancelled(cancellationToken);
            }

            try
            {
                AccountBackendResult initialized = await EnsureInitializedAsync(cancellationToken);
                if (!initialized.Succeeded)
                {
                    return Fail(Map(initialized), initialized.ErrorMessage);
                }

                if (IsAuthenticated)
                {
                    return CompleteAuthentication(AccountAuthenticationStatus.Unchanged);
                }

                if (!backend.HasCachedSession)
                {
                    return Fail(
                        AccountAuthenticationStatus.NoRecoverableAnonymousSession,
                        "No cached anonymous account is available to restore.");
                }

                SetState(AccountAuthenticationState.LoggingIn);
                AccountBackendResult resumed = await backend.ResumeAnonymousAsync(cancellationToken);
                if (!resumed.Succeeded)
                {
                    return Fail(Map(resumed), resumed.ErrorMessage);
                }

                return CompleteAuthentication(AccountAuthenticationStatus.Success);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<AccountAuthenticationResult> RequireAuthenticatedAsync(
            CancellationToken cancellationToken = default)
        {
            AccountAuthenticationResult initialized =
                await InitializeAuthenticationAsync(cancellationToken);
            return initialized.Status == AccountAuthenticationStatus.AuthenticationRequired
                ? new AccountAuthenticationResult(
                    AccountAuthenticationStatus.AuthenticationRequired,
                    errorMessage: "Login or registration is required for online play.")
                : initialized;
        }

        public async Task<AccountAuthenticationResult> LogoutAsync(
            bool clearCachedCredentials = false,
            bool confirmAnonymousCredentialLoss = false,
            CancellationToken cancellationToken = default)
        {
            if (!await TryEnterAsync(cancellationToken))
            {
                return BusyOrCancelled(cancellationToken);
            }

            try
            {
                AccountBackendResult initialized = await EnsureInitializedAsync(cancellationToken);
                if (!initialized.Succeeded)
                {
                    return Fail(Map(initialized), initialized.ErrorMessage);
                }

                if (!IsAuthenticated)
                {
                    SetState(AccountAuthenticationState.AuthenticationRequired);
                    return new AccountAuthenticationResult(AccountAuthenticationStatus.Unchanged);
                }

                if (IsAnonymous && !confirmAnonymousCredentialLoss)
                {
                    return Fail(
                        AccountAuthenticationStatus.AnonymousAccountRequiresLinking,
                        "Link the anonymous account or explicitly confirm losing its only recoverable credentials before logout.");
                }

                if (IsAnonymous)
                {
                    // Once another account signs in, Unity's single cached session may be replaced.
                    // Explicitly confirmed anonymous logout therefore clears it deterministically.
                    clearCachedCredentials = true;
                }

                SetState(AccountAuthenticationState.SigningOut);
                if (sessionTerminator != null)
                {
                    await sessionTerminator.TerminateActiveSessionAsync(cancellationToken);
                }

                backend.SignOut(clearCachedCredentials);
                SetState(AccountAuthenticationState.AuthenticationRequired);
                SignedOut?.Invoke();
                return new AccountAuthenticationResult(AccountAuthenticationStatus.Success);
            }
            catch (OperationCanceledException)
            {
                return Fail(AccountAuthenticationStatus.Cancelled, string.Empty);
            }
            catch (Exception)
            {
                return Fail(
                    AccountAuthenticationStatus.Failed,
                    "The account could not be signed out safely.");
            }
            finally
            {
                operationGate.Release();
            }
        }

        private async Task<AccountAuthenticationResult> AuthenticateWithCredentialsAsync(
            string username,
            string password,
            bool register,
            CancellationToken cancellationToken)
        {
            AccountAuthenticationResult validation = ValidateCredentials(username, password);
            if (validation.Status != AccountAuthenticationStatus.Success)
            {
                return validation;
            }

            if (!await TryEnterAsync(cancellationToken))
            {
                return BusyOrCancelled(cancellationToken);
            }

            try
            {
                AccountBackendResult initialized = await EnsureInitializedAsync(cancellationToken);
                if (!initialized.Succeeded)
                {
                    return Fail(Map(initialized), initialized.ErrorMessage);
                }

                if (IsAuthenticated)
                {
                    return Fail(
                        AccountAuthenticationStatus.AlreadyAuthenticated,
                        IsAnonymous
                            ? "Link the current anonymous account or explicitly sign out before changing accounts."
                            : "Sign out before authenticating a different account.");
                }

                SetState(register
                    ? AccountAuthenticationState.Registering
                    : AccountAuthenticationState.LoggingIn);
                AccountBackendResult result = register
                    ? await backend.RegisterAsync(username, password, cancellationToken)
                    : await backend.LoginAsync(username, password, cancellationToken);
                if (!result.Succeeded)
                {
                    return Fail(Map(result), result.ErrorMessage);
                }

                return CompleteAuthentication(AccountAuthenticationStatus.Success);
            }
            finally
            {
                operationGate.Release();
            }
        }

        private async Task<AccountBackendResult> EnsureInitializedAsync(
            CancellationToken cancellationToken)
        {
            return backend.IsInitialized
                ? new AccountBackendResult(AccountBackendStatus.Success)
                : await backend.InitializeAsync(cancellationToken);
        }

        private AccountAuthenticationResult CompleteAuthentication(
            AccountAuthenticationStatus status)
        {
            string playerId = backend.PlayerId == null ? string.Empty : backend.PlayerId.Trim();
            if (!backend.IsSignedIn || playerId.Length == 0)
            {
                return Fail(
                    AccountAuthenticationStatus.Failed,
                    "Unity Authentication completed without a valid Player ID.");
            }

            SetState(AccountAuthenticationState.Authenticated);
            Authenticated?.Invoke(playerId);
            return new AccountAuthenticationResult(status, playerId);
        }

        private AccountAuthenticationResult ValidateCredentials(string username, string password)
        {
            if (!AccountCredentialPolicy.IsValidUsername(username))
            {
                return Fail(
                    AccountAuthenticationStatus.InvalidUsername,
                    "Username must be 3-20 characters using letters, numbers, period, hyphen, underscore, or @.");
            }

            if (!AccountCredentialPolicy.IsValidPassword(password))
            {
                return Fail(
                    AccountAuthenticationStatus.InvalidPassword,
                    "Password must be 8-30 characters and include lowercase, uppercase, number, and symbol characters.");
            }

            return new AccountAuthenticationResult(AccountAuthenticationStatus.Success);
        }

        private async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await operationGate.WaitAsync(0, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static AccountAuthenticationResult BusyOrCancelled(
            CancellationToken cancellationToken)
        {
            return cancellationToken.IsCancellationRequested
                ? new AccountAuthenticationResult(AccountAuthenticationStatus.Cancelled)
                : new AccountAuthenticationResult(
                    AccountAuthenticationStatus.Busy,
                    errorMessage: "Another authentication operation is already in progress.");
        }

        private AccountAuthenticationResult Fail(
            AccountAuthenticationStatus status,
            string message)
        {
            if (status == AccountAuthenticationStatus.AuthenticationRequired ||
                status == AccountAuthenticationStatus.NoRecoverableAnonymousSession)
            {
                SetState(AccountAuthenticationState.AuthenticationRequired);
            }
            else if (IsAuthenticated &&
                     status != AccountAuthenticationStatus.SessionExpired)
            {
                SetState(AccountAuthenticationState.Authenticated);
            }
            else
            {
                SetState(AccountAuthenticationState.Failed);
            }

            var result = new AccountAuthenticationResult(status, errorMessage: message);
            OperationFailed?.Invoke(result);
            return result;
        }

        private static AccountAuthenticationStatus Map(AccountBackendResult result)
        {
            switch (result.Status)
            {
                case AccountBackendStatus.UsernameUnavailable:
                    return AccountAuthenticationStatus.UsernameUnavailable;
                case AccountBackendStatus.InvalidCredentials:
                    return AccountAuthenticationStatus.InvalidCredentials;
                case AccountBackendStatus.CredentialAlreadyLinked:
                    return AccountAuthenticationStatus.CredentialAlreadyLinked;
                case AccountBackendStatus.NetworkUnavailable:
                    return AccountAuthenticationStatus.NetworkUnavailable;
                case AccountBackendStatus.ServiceUnavailable:
                    return AccountAuthenticationStatus.ServiceUnavailable;
                case AccountBackendStatus.RateLimited:
                    return AccountAuthenticationStatus.RateLimited;
                case AccountBackendStatus.SessionExpired:
                    return AccountAuthenticationStatus.SessionExpired;
                case AccountBackendStatus.Cancelled:
                    return AccountAuthenticationStatus.Cancelled;
                default:
                    return AccountAuthenticationStatus.Failed;
            }
        }

        private void SetState(AccountAuthenticationState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
