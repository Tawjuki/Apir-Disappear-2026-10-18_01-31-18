using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.Multiplayer;
using NUnit.Framework;

namespace ApirApir.PlayerProfiles.Tests
{
    public sealed class AccountAuthenticationTests
    {
        private const string ValidUsername = "juan_dev01";
        private const string ValidPassword = "Valid#123";

        [Test]
        public async Task Register_ValidCredentials_ReturnsStablePlayerId()
        {
            var backend = new FakeAuthenticationBackend();
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.RegisterAsync(ValidUsername, ValidPassword);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.PlayerId, Is.EqualTo(backend.PlayerId));
            Assert.That(service.State, Is.EqualTo(AccountAuthenticationState.Authenticated));
        }

        [Test]
        public async Task Register_NewAccount_RequestsSeparatePermanentPlayerName()
        {
            var backend = new FakeAuthenticationBackend();
            var authentication = new AccountAuthenticationService(backend);
            var profile = new CloudPlayerProfileCoordinator(
                new ServiceIdentity(authentication),
                new MemoryCloudStore());
            var accounts = new PlayerAccountCoordinator(authentication, profile);

            PlayerAccountResult registered =
                await accounts.RegisterAsync(ValidUsername, ValidPassword);

            Assert.That(registered.Succeeded, Is.True);
            Assert.That(registered.NeedsPlayerName, Is.True);
            Assert.That(accounts.State, Is.EqualTo(PlayerAccountState.ProfileNeedsName));
            Assert.That(profile.CurrentProfile, Is.Null);

            CloudProfileCommandStatus created = await profile.CreateProfileAsync("Juan");
            Assert.That(created, Is.EqualTo(CloudProfileCommandStatus.Success));
            Assert.That(accounts.State, Is.EqualTo(PlayerAccountState.Ready));
            Assert.That(profile.CurrentProfile.PlayerName, Is.EqualTo("Juan"));
            Assert.That(profile.CurrentProfile.PlayerName, Is.Not.EqualTo(ValidUsername));
            Assert.That(profile.CurrentProfile.GamesPlayed, Is.Zero);
        }

        [TestCase("ab")]
        [TestCase("abcdefghijklmnopqrstu")]
        [TestCase("has space")]
        [TestCase("bad!")]
        [TestCase("")]
        public async Task Register_InvalidUsername_IsRejectedBeforeSdk(string username)
        {
            var backend = new FakeAuthenticationBackend();
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.RegisterAsync(username, ValidPassword);

            Assert.That(result.Status, Is.EqualTo(AccountAuthenticationStatus.InvalidUsername));
            Assert.That(backend.RegisterCalls, Is.Zero);
        }

        [TestCase("Short1!")]
        [TestCase("nouppercase1!")]
        [TestCase("NOLOWERCASE1!")]
        [TestCase("NoNumber!")]
        [TestCase("NoSymbol123")]
        [TestCase("")]
        public async Task Register_InvalidPassword_IsRejectedBeforeSdk(string password)
        {
            var backend = new FakeAuthenticationBackend();
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.RegisterAsync(ValidUsername, password);

            Assert.That(result.Status, Is.EqualTo(AccountAuthenticationStatus.InvalidPassword));
            Assert.That(backend.RegisterCalls, Is.Zero);
        }

        [Test]
        public async Task Register_DuplicateUsername_ReturnsSafeStructuredError()
        {
            var backend = new FakeAuthenticationBackend();
            backend.AddAccount(ValidUsername, ValidPassword, "player-one");
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.RegisterAsync(ValidUsername.ToUpperInvariant(), ValidPassword);

            Assert.That(result.Status, Is.EqualTo(AccountAuthenticationStatus.UsernameUnavailable));
            Assert.That(result.ErrorMessage, Does.Not.Contain(ValidUsername));
            Assert.That(result.ErrorMessage, Does.Not.Contain(ValidPassword));
        }

        [Test]
        public async Task Login_ReturningUser_RetainsPlayerId()
        {
            var backend = new FakeAuthenticationBackend();
            backend.AddAccount(ValidUsername, ValidPassword, "stable-player-id");
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.LoginAsync(ValidUsername.ToUpperInvariant(), ValidPassword);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.PlayerId, Is.EqualTo("stable-player-id"));
        }

        [Test]
        public async Task Login_WrongCredentials_DoesNotRevealUsernameExistence()
        {
            var backend = new FakeAuthenticationBackend();
            backend.AddAccount(ValidUsername, ValidPassword, "player-one");
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult wrongPassword =
                await service.LoginAsync(ValidUsername, "Wrong#123");
            AccountAuthenticationResult missingAccount =
                await service.LoginAsync("missing_user", "Wrong#123");

            Assert.That(wrongPassword.Status, Is.EqualTo(AccountAuthenticationStatus.InvalidCredentials));
            Assert.That(missingAccount.Status, Is.EqualTo(AccountAuthenticationStatus.InvalidCredentials));
            Assert.That(missingAccount.ErrorMessage, Is.EqualTo(wrongPassword.ErrorMessage));
        }

        [Test]
        public async Task Initialize_DoesNotCreateAnonymousAccount()
        {
            var backend = new FakeAuthenticationBackend();
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.InitializeAuthenticationAsync();

            Assert.That(result.Status, Is.EqualTo(AccountAuthenticationStatus.AuthenticationRequired));
            Assert.That(backend.ResumeAnonymousCalls, Is.Zero);
            Assert.That(backend.RegisterCalls, Is.Zero);
        }

        [Test]
        public async Task Initialize_ReusesExistingAuthenticatedAccount()
        {
            var backend = new FakeAuthenticationBackend();
            backend.SetAnonymousSession("existing-player", true);
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.InitializeAuthenticationAsync();

            Assert.That(result.Status, Is.EqualTo(AccountAuthenticationStatus.Unchanged));
            Assert.That(result.PlayerId, Is.EqualTo("existing-player"));
            Assert.That(backend.ResumeAnonymousCalls, Is.Zero);
        }

        [Test]
        public async Task ResumeAnonymous_RequiresExistingCachedSession()
        {
            var backend = new FakeAuthenticationBackend();
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result = await service.ResumeAnonymousAsync();

            Assert.That(result.Status,
                Is.EqualTo(AccountAuthenticationStatus.NoRecoverableAnonymousSession));
            Assert.That(backend.ResumeAnonymousCalls, Is.Zero);
        }

        [Test]
        public async Task LinkAnonymous_PreservesPlayerIdAndCloudProfile()
        {
            var backend = new FakeAuthenticationBackend();
            backend.SetAnonymousSession("anonymous-player", true);
            var authentication = new AccountAuthenticationService(backend);
            var store = new MemoryCloudStore();
            store.Seed(new CloudPlayerProfileRecord(
                CloudPlayerProfileRecord.CurrentSchemaVersion,
                "anonymous-player",
                "Juan",
                5,
                new[] { "match-1" }));
            var profile = new CloudPlayerProfileCoordinator(
                new ServiceIdentity(authentication),
                store);
            var accounts = new PlayerAccountCoordinator(authentication, profile);

            PlayerAccountResult initialized =
                await accounts.InitializeAuthenticationAsync();
            PlayerAccountResult linked =
                await accounts.LinkUsernamePasswordAsync(ValidUsername, ValidPassword);

            Assert.That(initialized.Succeeded, Is.True);
            Assert.That(linked.Succeeded, Is.True);
            Assert.That(linked.PlayerId, Is.EqualTo("anonymous-player"));
            Assert.That(profile.CurrentProfile.PlayerName, Is.EqualTo("Juan"));
            Assert.That(profile.CurrentProfile.GamesPlayed, Is.EqualTo(5));
            Assert.That(backend.HasUsernamePassword, Is.True);
        }

        [Test]
        public async Task LinkCredentialsAlreadyUsedByAnotherAccount_IsRejected()
        {
            var backend = new FakeAuthenticationBackend();
            backend.AddAccount(ValidUsername, ValidPassword, "other-player");
            backend.SetAnonymousSession("anonymous-player", true);
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result =
                await service.LinkUsernamePasswordAsync(ValidUsername, ValidPassword);

            Assert.That(result.Status,
                Is.EqualTo(AccountAuthenticationStatus.CredentialAlreadyLinked));
            Assert.That(service.PlayerId, Is.EqualTo("anonymous-player"));
        }

        [Test]
        public async Task Logout_ClearsProfileButDoesNotDeleteCloudRecord()
        {
            var backend = new FakeAuthenticationBackend();
            backend.AddAccount(ValidUsername, ValidPassword, "player-one");
            var authentication = new AccountAuthenticationService(backend);
            var store = new MemoryCloudStore();
            store.Seed(new CloudPlayerProfileRecord(
                1, "player-one", "Juan", 8, Array.Empty<string>()));
            var profile = new CloudPlayerProfileCoordinator(
                new ServiceIdentity(authentication), store);
            var accounts = new PlayerAccountCoordinator(authentication, profile);
            await accounts.LoginAsync(ValidUsername, ValidPassword);

            PlayerAccountResult loggedOut = await accounts.LogoutAsync();

            Assert.That(loggedOut.Authentication.Status,
                Is.EqualTo(AccountAuthenticationStatus.Success));
            Assert.That(profile.CurrentProfile, Is.Null);
            Assert.That(profile.AuthenticatedPlayerId, Is.Empty);
            Assert.That(store.Contains("player-one"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AnonymousLogout_RequiresExplicitCredentialLossConfirmation(
            bool clearCachedCredentials)
        {
            var backend = new FakeAuthenticationBackend();
            backend.SetAnonymousSession("anonymous-player", true);
            var service = new AccountAuthenticationService(backend);

            AccountAuthenticationResult result = await service.LogoutAsync(
                clearCachedCredentials,
                confirmAnonymousCredentialLoss: false);

            Assert.That(result.Status,
                Is.EqualTo(AccountAuthenticationStatus.AnonymousAccountRequiresLinking));
            Assert.That(service.IsAuthenticated, Is.True);
        }

        [Test]
        public async Task SwitchingAccounts_RequiresLogoutAndDoesNotLeakProfile()
        {
            var backend = new FakeAuthenticationBackend();
            backend.AddAccount("account_one", ValidPassword, "player-one");
            backend.AddAccount("account_two", ValidPassword, "player-two");
            var authentication = new AccountAuthenticationService(backend);
            var store = new MemoryCloudStore();
            store.Seed(new CloudPlayerProfileRecord(
                1, "player-one", "Juan", 3, Array.Empty<string>()));
            store.Seed(new CloudPlayerProfileRecord(
                1, "player-two", "Maria", 7, Array.Empty<string>()));
            var profile = new CloudPlayerProfileCoordinator(
                new ServiceIdentity(authentication), store);
            var accounts = new PlayerAccountCoordinator(authentication, profile);

            await accounts.LoginAsync("account_one", ValidPassword);
            Assert.That(profile.CurrentProfile.PlayerName, Is.EqualTo("Juan"));
            await accounts.LogoutAsync(clearCachedCredentials: true);
            Assert.That(profile.CurrentProfile, Is.Null);
            await accounts.LoginAsync("account_two", ValidPassword);

            Assert.That(profile.AuthenticatedPlayerId, Is.EqualTo("player-two"));
            Assert.That(profile.CurrentProfile.PlayerName, Is.EqualTo("Maria"));
            Assert.That(profile.CurrentProfile.GamesPlayed, Is.EqualTo(7));
        }

        [Test]
        public async Task RepeatedLogin_DoesNotAuthenticateOrCreateProfileTwice()
        {
            var backend = new FakeAuthenticationBackend();
            backend.AddAccount(ValidUsername, ValidPassword, "player-one");
            var service = new AccountAuthenticationService(backend);

            await service.LoginAsync(ValidUsername, ValidPassword);
            AccountAuthenticationResult repeated =
                await service.LoginAsync(ValidUsername, ValidPassword);

            Assert.That(repeated.Status,
                Is.EqualTo(AccountAuthenticationStatus.AlreadyAuthenticated));
            Assert.That(backend.LoginCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task ConcurrentAuthenticationRequest_ReturnsBusy()
        {
            var backend = new FakeAuthenticationBackend { BlockLogin = true };
            backend.AddAccount(ValidUsername, ValidPassword, "player-one");
            var service = new AccountAuthenticationService(backend);

            Task<AccountAuthenticationResult> first =
                service.LoginAsync(ValidUsername, ValidPassword);
            await backend.LoginStarted.Task;
            AccountAuthenticationResult second =
                await service.LoginAsync(ValidUsername, ValidPassword);
            backend.ReleaseLogin.TrySetResult(true);
            AccountAuthenticationResult completed = await first;

            Assert.That(second.Status, Is.EqualTo(AccountAuthenticationStatus.Busy));
            Assert.That(completed.Succeeded, Is.True);
            Assert.That(backend.LoginCalls, Is.EqualTo(1));
        }

        [Test]
        public void AuthenticationService_DoesNotRetainCredentialStrings()
        {
            FieldInfo[] fields = typeof(AccountAuthenticationService).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.That(Array.Exists(fields, field => field.FieldType == typeof(string)), Is.False);
        }

        private sealed class ServiceIdentity : ICloudPlayerIdentity
        {
            private readonly AccountAuthenticationService service;

            public ServiceIdentity(AccountAuthenticationService service)
            {
                this.service = service;
            }

            public string SignedInPlayerId => service.PlayerId;

            public async Task<CloudAuthenticationResult> EnsureAuthenticatedAsync(
                CancellationToken cancellationToken = default)
            {
                AccountAuthenticationResult result =
                    await service.RequireAuthenticatedAsync(cancellationToken);
                return result.Succeeded
                    ? new CloudAuthenticationResult(
                        CloudAuthenticationStatus.Success,
                        result.PlayerId)
                    : new CloudAuthenticationResult(
                        CloudAuthenticationStatus.Unavailable,
                        errorMessage: result.ErrorMessage);
            }
        }

        private sealed class FakeAuthenticationBackend : IAccountAuthenticationBackend
        {
            private sealed class Account
            {
                public string Password;
                public string PlayerId;
            }

            private readonly Dictionary<string, Account> accounts =
                new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
            private string playerId = string.Empty;
            private bool hasUsernamePassword;
            private int nextPlayerId = 1;

            public bool IsInitialized { get; private set; }
            public bool IsSignedIn { get; private set; }
            public bool HasCachedSession { get; private set; }
            public bool HasUsernamePassword => IsSignedIn && hasUsernamePassword;
            public string PlayerId => IsSignedIn ? playerId : string.Empty;
            public int RegisterCalls { get; private set; }
            public int LoginCalls { get; private set; }
            public int ResumeAnonymousCalls { get; private set; }
            public bool BlockLogin { get; set; }
            public TaskCompletionSource<bool> LoginStarted { get; } =
                new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> ReleaseLogin { get; } =
                new TaskCompletionSource<bool>();

            public void AddAccount(string username, string password, string accountPlayerId)
            {
                accounts[username] = new Account
                {
                    Password = password,
                    PlayerId = accountPlayerId
                };
            }

            public void SetAnonymousSession(string accountPlayerId, bool signedIn)
            {
                playerId = accountPlayerId;
                IsSignedIn = signedIn;
                HasCachedSession = true;
                hasUsernamePassword = false;
                IsInitialized = true;
            }

            public Task<AccountBackendResult> InitializeAsync(
                CancellationToken cancellationToken = default)
            {
                IsInitialized = true;
                return Task.FromResult(new AccountBackendResult(AccountBackendStatus.Success));
            }

            public Task<AccountBackendResult> RegisterAsync(
                string username,
                string password,
                CancellationToken cancellationToken = default)
            {
                RegisterCalls++;
                if (accounts.ContainsKey(username))
                {
                    return Task.FromResult(new AccountBackendResult(
                        AccountBackendStatus.UsernameUnavailable,
                        "The account could not be registered with those credentials."));
                }

                string createdId = "registered-player-" + nextPlayerId++;
                AddAccount(username, password, createdId);
                SignIn(createdId, true);
                return Task.FromResult(new AccountBackendResult(AccountBackendStatus.Success));
            }

            public async Task<AccountBackendResult> LoginAsync(
                string username,
                string password,
                CancellationToken cancellationToken = default)
            {
                LoginCalls++;
                if (BlockLogin)
                {
                    LoginStarted.TrySetResult(true);
                    await ReleaseLogin.Task;
                }

                if (!accounts.TryGetValue(username, out Account account) ||
                    !string.Equals(account.Password, password, StringComparison.Ordinal))
                {
                    return new AccountBackendResult(
                        AccountBackendStatus.InvalidCredentials,
                        "The username or password is incorrect.");
                }

                SignIn(account.PlayerId, true);
                return new AccountBackendResult(AccountBackendStatus.Success);
            }

            public Task<AccountBackendResult> LinkUsernamePasswordAsync(
                string username,
                string password,
                CancellationToken cancellationToken = default)
            {
                if (accounts.ContainsKey(username))
                {
                    return Task.FromResult(new AccountBackendResult(
                        AccountBackendStatus.CredentialAlreadyLinked,
                        "Those credentials cannot be linked to this account."));
                }

                AddAccount(username, password, playerId);
                hasUsernamePassword = true;
                return Task.FromResult(new AccountBackendResult(AccountBackendStatus.Success));
            }

            public Task<AccountBackendResult> ResumeAnonymousAsync(
                CancellationToken cancellationToken = default)
            {
                ResumeAnonymousCalls++;
                IsSignedIn = true;
                hasUsernamePassword = false;
                return Task.FromResult(new AccountBackendResult(AccountBackendStatus.Success));
            }

            public void SignOut(bool clearCachedCredentials)
            {
                IsSignedIn = false;
                if (clearCachedCredentials)
                {
                    HasCachedSession = false;
                    playerId = string.Empty;
                }
            }

            private void SignIn(string accountPlayerId, bool linked)
            {
                playerId = accountPlayerId;
                IsSignedIn = true;
                HasCachedSession = true;
                hasUsernamePassword = linked;
                IsInitialized = true;
            }
        }

        private sealed class MemoryCloudStore : ICloudPlayerProfileStore
        {
            private readonly Dictionary<string, CloudPlayerProfileRecord> records =
                new Dictionary<string, CloudPlayerProfileRecord>(StringComparer.Ordinal);
            private int revision;

            public void Seed(CloudPlayerProfileRecord record)
            {
                records[record.PlayerId] = Copy(record);
            }

            public bool Contains(string playerId)
            {
                return records.ContainsKey(playerId);
            }

            public Task<CloudProfileStoreLoadResult> LoadAsync(
                string authenticatedPlayerId,
                CancellationToken cancellationToken = default)
            {
                if (!records.TryGetValue(authenticatedPlayerId, out CloudPlayerProfileRecord record))
                {
                    return Task.FromResult(new CloudProfileStoreLoadResult(
                        CloudProfileStoreLoadStatus.NotFound));
                }

                return Task.FromResult(new CloudProfileStoreLoadResult(
                    CloudProfileStoreLoadStatus.Success,
                    Copy(record),
                    revision.ToString()));
            }

            public Task<CloudProfileStoreSaveResult> SaveAsync(
                CloudPlayerProfileRecord record,
                string expectedWriteLock,
                CancellationToken cancellationToken = default)
            {
                records[record.PlayerId] = Copy(record);
                revision++;
                return Task.FromResult(new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Success,
                    revision.ToString()));
            }

            private static CloudPlayerProfileRecord Copy(CloudPlayerProfileRecord record)
            {
                return new CloudPlayerProfileRecord(
                    record.SchemaVersion,
                    record.PlayerId,
                    record.PlayerName,
                    record.GamesPlayed,
                    record.ProcessedMatchIds);
            }
        }
    }
}
