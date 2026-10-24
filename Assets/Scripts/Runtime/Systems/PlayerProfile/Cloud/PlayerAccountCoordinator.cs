using System;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.Multiplayer;

namespace ApirApir.PlayerProfiles
{
    public enum PlayerAccountState : byte
    {
        Uninitialized,
        Initializing,
        AuthenticationRequired,
        Registering,
        LoggingIn,
        Linking,
        LoadingProfile,
        ProfileNeedsName,
        Ready,
        SigningOut,
        Failed
    }

    public readonly struct PlayerAccountResult
    {
        public PlayerAccountResult(
            AccountAuthenticationResult authentication,
            CloudProfileCommandStatus profileStatus = CloudProfileCommandStatus.NotReady)
        {
            Authentication = authentication;
            ProfileStatus = profileStatus;
        }

        public AccountAuthenticationResult Authentication { get; }
        public CloudProfileCommandStatus ProfileStatus { get; }
        public string PlayerId => Authentication.PlayerId;
        public string ErrorMessage => Authentication.ErrorMessage;
        public bool NeedsPlayerName =>
            ProfileStatus == CloudProfileCommandStatus.NeedsPlayerName;
        public bool Succeeded => Authentication.Succeeded &&
                                 (ProfileStatus == CloudProfileCommandStatus.Success ||
                                  ProfileStatus == CloudProfileCommandStatus.Unchanged ||
                                  ProfileStatus == CloudProfileCommandStatus.NeedsPlayerName);
    }

    /// <summary>
    /// Composes the shared account session with the existing Cloud Profile coordinator. It adds no
    /// second identity or profile store and keeps login usernames separate from player names.
    /// </summary>
    public sealed class PlayerAccountCoordinator
    {
        private readonly AccountAuthenticationService authentication;
        private readonly CloudPlayerProfileCoordinator profile;
        private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);

        public PlayerAccountCoordinator(
            AccountAuthenticationService authentication,
            CloudPlayerProfileCoordinator profile)
        {
            this.authentication = authentication ??
                throw new ArgumentNullException(nameof(authentication));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.profile.StateChanged += OnProfileStateChanged;
        }

        public PlayerAccountState State { get; private set; } = PlayerAccountState.Uninitialized;
        public AccountAuthenticationService Authentication => authentication;
        public CloudPlayerProfileCoordinator Profile => profile;

        public event Action<PlayerAccountState> StateChanged;

        public Task<PlayerAccountResult> InitializeAuthenticationAsync(
            CancellationToken cancellationToken = default)
        {
            return RunAuthenticationAsync(
                PlayerAccountState.Initializing,
                token => authentication.InitializeAuthenticationAsync(token),
                cancellationToken);
        }

        public Task<PlayerAccountResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return RunAuthenticationAsync(
                PlayerAccountState.Registering,
                token => authentication.RegisterAsync(username, password, token),
                cancellationToken);
        }

        public Task<PlayerAccountResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return RunAuthenticationAsync(
                PlayerAccountState.LoggingIn,
                token => authentication.LoginAsync(username, password, token),
                cancellationToken);
        }

        public Task<PlayerAccountResult> LinkUsernamePasswordAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return RunAuthenticationAsync(
                PlayerAccountState.Linking,
                token => authentication.LinkUsernamePasswordAsync(username, password, token),
                cancellationToken);
        }

        public Task<PlayerAccountResult> ResumeAnonymousAsync(
            CancellationToken cancellationToken = default)
        {
            return RunAuthenticationAsync(
                PlayerAccountState.LoggingIn,
                token => authentication.ResumeAnonymousAsync(token),
                cancellationToken);
        }

        public async Task<PlayerAccountResult> LogoutAsync(
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
                SetState(PlayerAccountState.SigningOut);
                AccountAuthenticationResult result = await authentication.LogoutAsync(
                    clearCachedCredentials,
                    confirmAnonymousCredentialLoss,
                    cancellationToken);
                if (result.Status != AccountAuthenticationStatus.Success &&
                    result.Status != AccountAuthenticationStatus.Unchanged)
                {
                    SetState(PlayerAccountState.Failed);
                    return new PlayerAccountResult(result);
                }

                // Once authentication is gone, clearing stale UI-facing data must complete even
                // when the caller cancelled immediately after the SDK sign-out.
                await profile.ClearSessionAsync(CancellationToken.None);
                SetState(PlayerAccountState.AuthenticationRequired);
                return new PlayerAccountResult(result, CloudProfileCommandStatus.NotReady);
            }
            finally
            {
                operationGate.Release();
            }
        }

        private async Task<PlayerAccountResult> RunAuthenticationAsync(
            PlayerAccountState operationState,
            Func<CancellationToken, Task<AccountAuthenticationResult>> operation,
            CancellationToken cancellationToken)
        {
            if (!await TryEnterAsync(cancellationToken))
            {
                return BusyOrCancelled(cancellationToken);
            }

            try
            {
                SetState(operationState);
                AccountAuthenticationResult authenticationResult =
                    await operation(cancellationToken);
                if (!authenticationResult.Succeeded)
                {
                    SetState(authenticationResult.Status ==
                             AccountAuthenticationStatus.AuthenticationRequired
                        ? PlayerAccountState.AuthenticationRequired
                        : PlayerAccountState.Failed);
                    return new PlayerAccountResult(authenticationResult);
                }

                SetState(PlayerAccountState.LoadingProfile);
                CloudProfileCommandStatus profileStatus =
                    await profile.InitializeAsync(cancellationToken);
                if (profileStatus == CloudProfileCommandStatus.NeedsPlayerName)
                {
                    SetState(PlayerAccountState.ProfileNeedsName);
                }
                else if (profileStatus == CloudProfileCommandStatus.Success ||
                         profileStatus == CloudProfileCommandStatus.Unchanged)
                {
                    SetState(PlayerAccountState.Ready);
                }
                else
                {
                    SetState(PlayerAccountState.Failed);
                }

                return new PlayerAccountResult(authenticationResult, profileStatus);
            }
            finally
            {
                operationGate.Release();
            }
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

        private static PlayerAccountResult BusyOrCancelled(CancellationToken cancellationToken)
        {
            AccountAuthenticationStatus status = cancellationToken.IsCancellationRequested
                ? AccountAuthenticationStatus.Cancelled
                : AccountAuthenticationStatus.Busy;
            string message = status == AccountAuthenticationStatus.Busy
                ? "Another account operation is already in progress."
                : string.Empty;
            return new PlayerAccountResult(
                new AccountAuthenticationResult(status, errorMessage: message));
        }

        private void SetState(PlayerAccountState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        private void OnProfileStateChanged(CloudProfileSessionState profileState)
        {
            switch (profileState)
            {
                case CloudProfileSessionState.AuthenticationStarted:
                    SetState(PlayerAccountState.Initializing);
                    break;
                case CloudProfileSessionState.ProfileLoading:
                    SetState(PlayerAccountState.LoadingProfile);
                    break;
                case CloudProfileSessionState.ProfileNeedsName:
                    SetState(PlayerAccountState.ProfileNeedsName);
                    break;
                case CloudProfileSessionState.ProfileReady:
                    SetState(PlayerAccountState.Ready);
                    break;
                case CloudProfileSessionState.Failed:
                    SetState(PlayerAccountState.Failed);
                    break;
                case CloudProfileSessionState.Uninitialized:
                    SetState(authentication.IsAuthenticated
                        ? PlayerAccountState.Uninitialized
                        : PlayerAccountState.AuthenticationRequired);
                    break;
            }
        }
    }
}
