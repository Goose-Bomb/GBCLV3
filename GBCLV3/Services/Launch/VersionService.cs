﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using GBCLV3.Models.Download;
using GBCLV3.Models.Launch;
using GBCLV3.Services.Download;
using GBCLV3.Utils;
using GBCLV3.Utils.Native;
using StyletIoC;
using Version = GBCLV3.Models.Launch.Version;

namespace GBCLV3.Services.Launch
{
    public class VersionService
    {
        #region Events

        public event Action<bool> Loaded;

        public event Action<Version> Deleted;

        public event Action<Version> Created;

        #endregion

        #region Private Fields

        private readonly Dictionary<string, Version> _versions;

        // IoC
        private readonly GamePathService _gamePathService;
        private readonly DownloadUrlService _urlService;
        private readonly LibraryService _libraryService;
        private readonly LogService _logService;
        private readonly HttpClient _client;

        #endregion

        #region Constructor

        [Inject]
        public VersionService(
            GamePathService gamePathService,
            DownloadUrlService urlService,
            LibraryService libraryService,
            LogService logService,
            HttpClient client)
        {
            _gamePathService = gamePathService;
            _urlService = urlService;
            _libraryService = libraryService;
            _client = client;
            _logService = logService;

            _versions = new Dictionary<string, Version>(8);
        }

        #endregion

        #region Public Methods

        public bool LoadAll()
        {
            if (!Directory.Exists(_gamePathService.VersionsDir))
            {
                _logService.Warn(nameof(VersionService), "Cannot find game versions directory");

                Loaded?.Invoke(false);
                return false;
            }

            var availableVersions =
                Directory.EnumerateDirectories(_gamePathService.VersionsDir)
                    .Select(dir => $"{dir}/{Path.GetFileName(dir)}.json")
                    .Where(jsonPath => File.Exists(jsonPath))
                    .Select(jsonPath => Load(jsonPath))
                    .Where(version => version != null);

            var inheritVersions = new List<Version>(8);

            _versions.Clear();
            foreach (var version in availableVersions)
            {
                _versions.Add(version.ID, version);
                if (version.InheritsFrom != null)
                {
                    inheritVersions.Add(version);
                }
            }

            foreach (var version in inheritVersions)
            {
                InheritParentProperties(version);
            }

            Loaded?.Invoke(_versions.Any());
            return _versions.Any();
        }

        public bool Has(string id)
        {
            return id != null && _versions.ContainsKey(id);
        }

        public IEnumerable<Version> GetAll()
        {
            return _versions.Values.OrderBy(v => v.ID);
        }

        public Version GetByID(string id)
        {
            if (id != null && _versions.TryGetValue(id, out var version))
            {
                return version;
            }

            return null;
        }

        public Version AddNew(string jsonPath)
        {
            _logService.Info(nameof(VersionService), "Adding new version");

            var newVersion = Load(jsonPath);

            if (newVersion != null)
            {
                if (newVersion.InheritsFrom != null)
                {
                    InheritParentProperties(newVersion);
                }

                _versions.Add(newVersion.ID, newVersion);
                Created?.Invoke(newVersion);
            }
            else
            {
                // This version is invalid, cleanup
                File.Delete(jsonPath);
                Directory.Delete(Path.GetDirectoryName(jsonPath));
            }

            return newVersion;
        }

        public void DeleteFromDisk(string id, bool isDeleteLibs)
        {
            _logService.Info(nameof(VersionService), $"Deleting version {id}");

            if (_versions.TryGetValue(id, out var versionToDelete))
            {
                _versions.Remove(id);
                Deleted?.Invoke(versionToDelete);

                // Delete version directory
                RecycleBinUtil.Send(Enumerable.Repeat($"{_gamePathService.VersionsDir}/{id}", 1));

                // Delete unused libraries
                if (isDeleteLibs)
                {
                    _logService.Info(nameof(VersionService), $"Deleting version {id} libraries");

                    var libsToDelete = versionToDelete.Libraries as IEnumerable<Library>;

                    if (_versions.Any())
                    {
                        foreach (var version in _versions.Values)
                        {
                            libsToDelete = libsToDelete.Except(version.Libraries);
                        }
                    }

                    var paths = libsToDelete.Select(lib =>
                    {
                        string libPath = $"{_gamePathService.LibrariesDir}/{lib.Path}";

                        if (lib.Type == LibraryType.ForgeMain)
                        {
                            return Path.GetDirectoryName(libPath);
                        }

                        return libPath;
                    });

                    RecycleBinUtil.Send(paths);

                    // Clean up empty lib directories
                    static void CleanEmptyDirs(string dir)
                    {
                        foreach (string subDir in Directory.EnumerateDirectories(dir))
                        {
                            CleanEmptyDirs(subDir);
                        }

                        if (!Directory.GetFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir);
                        }
                    }

                    if (Directory.Exists(_gamePathService.LibrariesDir))
                    {
                        _logService.Info(nameof(VersionService), $"Cleaning up empty lib directories");

                        CleanEmptyDirs(_gamePathService.LibrariesDir);
                    }
                }
            }
        }

        public async ValueTask<IEnumerable<VersionDownload>> GetDownloadListAsync()
        {
            _logService.Info(nameof(VersionService), "Fetching version downloads list");

            try
            {
                byte[] json = await _client.GetByteArrayAsync(_urlService.Base.VersionList);
                var versionList = JsonSerializer.Deserialize<JVersionList>(json);

                return versionList.versions.Select(download =>
                    new VersionDownload
                    {
                        ID = download.id,
                        Url = download.url[32..],
                        ReleaseTime = download.releaseTime,
                        Type = download.type == "release" ? VersionType.Release : VersionType.Snapshot
                    });
            }
            catch (HttpRequestException ex)
            {
                _logService.Error(nameof(VersionService), $"Failed to fetch version downloads list: HTTP error\n{ex.Message}");

                return null;
            }
            catch (OperationCanceledException)
            {
                _logService.Error(nameof(VersionService), "Failed to fetch version downloads list: Timeout");

                return null;
            }
        }

        public async ValueTask<byte[]> GetJsonAsync(VersionDownload download)
        {
            _logService.Info(nameof(VersionService), $"Fetching json for version: \"{download.ID}\"");

            try
            {
                return await _client.GetByteArrayAsync(_urlService.Base.Json + download.Url)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logService.Error(nameof(VersionService), $"Failed to fetch version json: HTTP error\n{ex.Message}");

                return null;
            }
            catch (OperationCanceledException)
            {
                _logService.Error(nameof(VersionService), "Failed to fetch version json: Timeout");

                return null;
            }
        }

        public bool CheckIntegrity(Version version)
        {
            string jarPath = $"{_gamePathService.VersionsDir}/{version.JarID}/{version.JarID}.jar";
            return File.Exists(jarPath) && CryptoUtil.ValidateFileSHA1(jarPath, version.SHA1);
        }

        public IEnumerable<DownloadItem> GetDownload(Version version)
        {
            var item = new DownloadItem
            {
                Name = version.JarID + "jar",
                Path = $"{_gamePathService.VersionsDir}/{version.JarID}/{version.JarID}.jar",
                Url = _urlService.Base.Version + version.Url,
                Size = version.Size,
                IsCompleted = false,
                DownloadedBytes = 0
            };

            return Enumerable.Repeat(item, 1);
        }

        #endregion

        #region Private Methods

        private Version Load(string jsonPath)
        {
            JVersion jver;
            try
            {
                byte[] jsonData = File.ReadAllBytes(jsonPath);
                var json = CryptoUtil.RemoveUtf8BOM(jsonData);
                jver = JsonSerializer.Deserialize<JVersion>(json);
            }
            catch(JsonException ex)
            {
                _logService.Error(nameof(VersionService), $"Load aborted: Failed to read version json.\n{ex.Message}");

                return null;
            }

            if (!IsValidVersion(jver))
            {
                _logService.Warn(nameof(VersionService), "Load aborted: Not a valid version");
                return null;
            }

            var version = new Version
            {
                ID = jver.id,
                JarID = jver.jar ?? jver.id,
                Size = jver.downloads?.client.size ?? 0,
                SHA1 = jver.downloads?.client.sha1,
                Url = jver.downloads?.client.url[28..],
                InheritsFrom = jver.inheritsFrom,
                MainClass = jver.mainClass,
                Type = jver.type == "release" ? VersionType.Release : VersionType.Snapshot,
                CompatibilityVersion = (int)jver.minimumLauncherVersion, // Eww, fxxk you forge :(
            };

            // Process launch arguments
            string[] args = jver.arguments?.game // post-1.12.2 versions
                           .Where(element => element.ValueKind == JsonValueKind.String)
                           .Select(element => element.GetString())
                           .ToArray() ?? jver.minecraftArguments.Split(' '); // pre-1.12.2 versions

            version.MinecraftArgsDict = Enumerable.Range(0, args.Length / 2)
                .ToDictionary(i => args[i * 2], i => args[i * 2 + 1]);

            if (version.MinecraftArgsDict.TryGetValue("--tweakClass", out string tweakClass))
            {
                if (tweakClass.EndsWith("FMLTweaker"))
                {
                    // For 1.7.2 and earlier forge version, there's no 'inheritsFrom' property
                    // So it needs to be assigned in order to launch correctly
                    version.InheritsFrom ??= version.ID.Split('-')[0];
                    version.Type = VersionType.Forge;
                }

                if (tweakClass == "optifine.OptiFineTweaker")
                {
                    version.Type = VersionType.OptiFine;
                }
            }

            if (version.MainClass == "cpw.mods.modlauncher.Launcher")
            {
                version.Type = VersionType.NewForge;
            }

            if (version.MainClass == "net.fabricmc.loader.launch.knot.KnotClient")
            {
                version.Type = VersionType.Fabric;
            }

            // Process libraries
            version.Libraries = _libraryService.Process(jver.libraries).ToList();

            if (version.InheritsFrom == null)
            {
                // Process assets
                version.AssetsInfo = new AssetsInfo
                {
                    ID = jver.assetIndex.id,
                    IndexSize = jver.assetIndex.size,
                    IndexUrl = jver.assetIndex.url,
                    IndexSHA1 = jver.assetIndex.sha1,
                    TotalSize = jver.assetIndex.totalSize,
                    IsLegacy = jver.assetIndex.id == "legacy",
                };
            }

            _logService.Info(nameof(VersionService), $"Version: \"{version.ID}\" loaded. Type: \"{version.Type}\"");

            return version;
        }

        private static bool IsValidVersion(JVersion jver)
        {
            return !string.IsNullOrWhiteSpace(jver.id)
                   && !(string.IsNullOrWhiteSpace(jver.minecraftArguments) && jver.arguments == null)
                   && !string.IsNullOrWhiteSpace(jver.mainClass)
                   && jver.libraries != null;
        }

        private void InheritParentProperties(Version version)
        {
            if (_versions.TryGetValue(version.InheritsFrom, out var parent))
            {
                version.JarID = parent.JarID;

                foreach (var arg in parent.MinecraftArgsDict)
                {
                    if (!version.MinecraftArgsDict.ContainsKey(arg.Key))
                    {
                        version.MinecraftArgsDict.Add(arg.Key, arg.Value);
                    }
                }

                version.Libraries = version.Libraries.Union(parent.Libraries).ToList();
                version.AssetsInfo = parent.AssetsInfo;
                version.Size = parent.Size;
                version.SHA1 = parent.SHA1;
                version.Url = parent.Url;
                version.CompatibilityVersion = parent.CompatibilityVersion;

                _logService.Info(nameof(VersionService), $"Version: \"{version.ID}\" inherited properties from parent: \"{parent.ID}\"");
            }
            else
            {
                _logService.Warn(nameof(VersionService), $"Version: \"{version.ID}\" cannot find parent: \"{version.InheritsFrom}\"");
                _versions.Remove(version.ID);
            }
        }
    }

    #endregion
}