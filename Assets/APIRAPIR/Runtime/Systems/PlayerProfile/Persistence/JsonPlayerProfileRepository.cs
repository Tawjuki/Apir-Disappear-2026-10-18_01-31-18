using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ApirApir.PlayerProfiles
{
    /// <summary>
    /// Local-development persistence. This is not an authenticated online profile database;
    /// competitive deployments should replace IPlayerProfileRepository with a trusted service.
    /// </summary>
    public sealed class JsonPlayerProfileRepository : IPlayerProfileRepository
    {
        private const int CurrentSchemaVersion = 1;
        private const long MaximumSaveBytes = 4L * 1024L * 1024L;

        [Serializable]
        private sealed class StorePayload
        {
            public int SchemaVersion;
            public ProfilePayload[] Profiles;
            public string[] ProcessedMatchIds;
        }

        [Serializable]
        private sealed class ProfilePayload
        {
            public string PlayerId;
            public string PlayerName;
            public string SelectedCharacterId;
            public int GamesPlayed;
            public int GamesWon;
            public int ThirdPlaceFinishes;
        }

        private readonly string filePath;
        private readonly object fileLock = new object();

        public JsonPlayerProfileRepository(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A profile save path is required.", nameof(filePath));
            }

            this.filePath = Path.GetFullPath(filePath);
        }

        public string FilePath => filePath;

        public static JsonPlayerProfileRepository CreateDefault(
            string fileName = "apir-apir-player-profiles.json")
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                throw new ArgumentException("The profile save file must be a file name.", nameof(fileName));
            }

            return new JsonPlayerProfileRepository(
                Path.Combine(Application.persistentDataPath, fileName));
        }

        public ProfileRepositoryLoadResult Load()
        {
            lock (fileLock)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        return new ProfileRepositoryLoadResult(ProfileRepositoryLoadStatus.NotFound);
                    }

                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length <= 0 || fileInfo.Length > MaximumSaveBytes)
                    {
                        return Corrupt();
                    }

                    string json = File.ReadAllText(filePath, Encoding.UTF8);
                    StorePayload payload = JsonUtility.FromJson<StorePayload>(json);
                    if (payload == null || payload.SchemaVersion != CurrentSchemaVersion ||
                        payload.Profiles == null || payload.ProcessedMatchIds == null)
                    {
                        return Corrupt();
                    }

                    var profiles = new List<PlayerProfileSnapshot>(payload.Profiles.Length);
                    for (int i = 0; i < payload.Profiles.Length; i++)
                    {
                        ProfilePayload stored = payload.Profiles[i];
                        if (stored == null)
                        {
                            return Corrupt();
                        }

                        profiles.Add(new PlayerProfileSnapshot(
                            stored.PlayerId,
                            stored.PlayerName,
                            stored.SelectedCharacterId,
                            stored.GamesPlayed,
                            stored.GamesWon,
                            stored.ThirdPlaceFinishes));
                    }

                    return new ProfileRepositoryLoadResult(
                        ProfileRepositoryLoadStatus.Success,
                        new PlayerProfileRepositoryState(profiles, payload.ProcessedMatchIds));
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is ArgumentException ||
                    exception is NotSupportedException)
                {
                    return Corrupt();
                }
            }
        }

        public bool TrySave(PlayerProfileRepositoryState state)
        {
            if (state == null)
            {
                return false;
            }

            lock (fileLock)
            {
                string temporaryPath = filePath + ".tmp";
                try
                {
                    var payload = new StorePayload
                    {
                        SchemaVersion = CurrentSchemaVersion,
                        Profiles = new ProfilePayload[state.Profiles.Count],
                        ProcessedMatchIds = new string[state.ProcessedMatchIds.Count]
                    };

                    for (int i = 0; i < state.Profiles.Count; i++)
                    {
                        PlayerProfileSnapshot profile = state.Profiles[i];
                        if (profile == null)
                        {
                            return false;
                        }

                        payload.Profiles[i] = new ProfilePayload
                        {
                            PlayerId = profile.PlayerId,
                            PlayerName = profile.PlayerName,
                            SelectedCharacterId = profile.SelectedCharacterId,
                            GamesPlayed = profile.GamesPlayed,
                            GamesWon = profile.GamesWon,
                            ThirdPlaceFinishes = profile.ThirdPlaceFinishes
                        };
                    }

                    for (int i = 0; i < state.ProcessedMatchIds.Count; i++)
                    {
                        payload.ProcessedMatchIds[i] = state.ProcessedMatchIds[i];
                    }

                    string json = JsonUtility.ToJson(payload, false);
                    if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaximumSaveBytes)
                    {
                        return false;
                    }

                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                    if (File.Exists(filePath))
                    {
                        ReplaceFile(temporaryPath, filePath);
                    }
                    else
                    {
                        File.Move(temporaryPath, filePath);
                    }

                    return true;
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is ArgumentException ||
                    exception is NotSupportedException)
                {
                    return false;
                }
                finally
                {
                    TryDeleteTemporaryFile(temporaryPath);
                }
            }
        }

        private static void ReplaceFile(string temporaryPath, string destinationPath)
        {
            try
            {
                File.Replace(temporaryPath, destinationPath, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temporaryPath, destinationPath, true);
                File.Delete(temporaryPath);
            }
        }

        private static void TryDeleteTemporaryFile(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static ProfileRepositoryLoadResult Corrupt()
        {
            return new ProfileRepositoryLoadResult(ProfileRepositoryLoadStatus.CorruptOrUnsupported);
        }
    }
}
