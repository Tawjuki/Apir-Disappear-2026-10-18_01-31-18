using NUnit.Framework;

namespace ApirApir.Multiplayer.Tests
{
    public sealed class AuthoritativeSessionRosterTests
    {
        [Test]
        public void HostAndThreeClientsCanReadyAndStart()
        {
            var roster = CreateFullRoster();

            for (ulong clientId = 0; clientId < NetworkSessionService.MaximumPlayers; clientId++)
            {
                Assert.That(roster.TrySetReady(clientId, true, 1), Is.True);
            }

            Assert.That(roster.CanStart, Is.True);
            Assert.That(roster.TryBeginMatch(0, 0), Is.True);
            Assert.That(roster.State, Is.EqualTo(MultiplayerSessionState.InMatch));
        }

        [Test]
        public void FifthPlayerIsRejectedWithoutMutatingRoster()
        {
            var roster = CreateFullRoster();

            bool accepted = roster.TryAdd(4, "player-4", "session-4", 0, out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("maximum"));
            Assert.That(roster.Count, Is.EqualTo(NetworkSessionService.MaximumPlayers));
        }

        [Test]
        public void DuplicatePersistentOrSessionIdentityIsRejected()
        {
            var roster = new AuthoritativeSessionRoster(4);
            roster.BeginWaitingRoom();
            Assert.That(roster.TryAdd(0, "same-player", "session-a", 0, out _), Is.True);

            Assert.That(roster.TryAdd(1, "same-player", "session-b", 0, out _), Is.False);
            Assert.That(roster.TryAdd(2, "other-player", "session-a", 0, out _), Is.False);
            Assert.That(roster.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReadyRequestsRequireOwnershipAndMonotonicRequestIds()
        {
            var roster = new AuthoritativeSessionRoster(4);
            roster.BeginWaitingRoom();
            Assert.That(roster.TryAdd(0, "player-0", "session-0", 0, out _), Is.True);

            Assert.That(roster.TrySetReady(9, true, 1), Is.False, "Unknown clients cannot mutate lobby state.");
            Assert.That(roster.TrySetReady(0, true, 5), Is.True);
            Assert.That(roster.TrySetReady(0, false, 5), Is.False, "Duplicate requests must be ignored.");
            Assert.That(roster.TrySetReady(0, false, 4), Is.False, "Out-of-order requests must be ignored.");
            Assert.That(roster.Find(0).isReady, Is.True);
        }

        [Test]
        public void OnlyHostCanStartAndAllFourMustBeReady()
        {
            var roster = CreateFullRoster();
            for (ulong clientId = 0; clientId < 3; clientId++)
            {
                roster.TrySetReady(clientId, true, 1);
            }

            Assert.That(roster.TryBeginMatch(0, 0), Is.False, "One unready player blocks start.");
            roster.TrySetReady(3, true, 1);
            Assert.That(roster.TryBeginMatch(1, 0), Is.False, "A client cannot start the match.");
            Assert.That(roster.TryBeginMatch(0, 0), Is.True);
        }

        [Test]
        public void DisconnectAndShutdownRemoveSessionState()
        {
            var roster = CreateFullRoster();

            Assert.That(roster.Remove(2), Is.True);
            Assert.That(roster.Count, Is.EqualTo(3));
            Assert.That(roster.Find(2), Is.Null);
            Assert.That(roster.CanStart, Is.False);

            roster.Clear();
            Assert.That(roster.Count, Is.Zero);
            Assert.That(roster.State, Is.EqualTo(MultiplayerSessionState.Offline));
        }

        [Test]
        public void SnapshotPreservesDistinctPlayerAndSessionIdentities()
        {
            var roster = CreateFullRoster();

            SessionPlayerState[] snapshot = roster.CreateSnapshot();

            Assert.That(snapshot, Has.Length.EqualTo(4));
            Assert.That(snapshot[0].isHost, Is.True);
            Assert.That(snapshot[1].playerId, Is.EqualTo("player-1"));
            Assert.That(snapshot[1].sessionPlayerId, Is.EqualTo("session-1"));
            snapshot[1].playerId = "mutated-copy";
            Assert.That(roster.Find(1).playerId, Is.EqualTo("player-1"));
        }

        private static AuthoritativeSessionRoster CreateFullRoster()
        {
            var roster = new AuthoritativeSessionRoster(NetworkSessionService.MaximumPlayers);
            roster.BeginWaitingRoom();
            for (ulong clientId = 0; clientId < NetworkSessionService.MaximumPlayers; clientId++)
            {
                Assert.That(
                    roster.TryAdd(clientId, $"player-{clientId}", $"session-{clientId}", 0, out _),
                    Is.True);
            }

            return roster;
        }
    }
}
