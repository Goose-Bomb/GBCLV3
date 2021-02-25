﻿using GBCLV3.Models.Auxiliary;
using GBCLV3.Services.Launch;
using GBCLV3.Utils.Native;
using StyletIoC;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GBCLV3.Services.Auxiliary
{
    public class ShaderPackService
    {
        #region Private Fields

        // IoC
        private readonly GamePathService _gamePathService;

        #endregion

        #region Constructor

        [Inject]
        public ShaderPackService(GamePathService gamePathService)
        {
            _gamePathService = gamePathService;
        }

        #endregion

        #region Public Methods

        public Task<ImmutableArray<ShaderPack>> LoadAllAsync()
        {
            string opttionsFile = _gamePathService.RootDir + "/optionsshaders.txt";
            string enabledPackId = null;

            if (File.Exists(opttionsFile))
            {
                using var reader = new StreamReader(opttionsFile, Encoding.Default);
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("shaderPack="))
                    {
                        enabledPackId = line[11..];
                        break;
                    }
                }
            }

            // Make sure "gameroot/shaderpacks" dir exists
            Directory.CreateDirectory(_gamePathService.ShaderPacksDir);

            var query = Directory.EnumerateFileSystemEntries(_gamePathService.ShaderPacksDir)
                .Select(path => Load(path, enabledPackId))
                .Where(pack => pack != null);

            return Task.FromResult(query.ToImmutableArray());
        }

        public Task<ImmutableArray<ShaderPack>> MoveLoadAllAsync(IEnumerable<string> paths, bool isCopy)
        {
            var query = paths.Select(path =>
            {
                string dstPath = $"{_gamePathService.ShaderPacksDir}/{Path.GetFileName(path)}";
                if (File.Exists(dstPath)) return null;

                var pack = Load(path);
                if (pack == null) return null;

                try
                {
                    if (isCopy)
                    {
                        File.Copy(path, dstPath);
                    }
                    else
                    {
                        File.Move(path, dstPath);
                    }
                }
                catch (IOException ex)
                {
                    // Maybe the file is being accessed by another process
                    return null;
                }

                pack.Path = dstPath;

                return pack;
            }).Where(pack => pack != null);

            return Task.FromResult(query.ToImmutableArray());
        }

        public void WriteToOptions(ShaderPack enabledPack)
        {
            string opttionsFile = _gamePathService.RootDir + "/optionsshaders.txt";
            if (!File.Exists(opttionsFile)) return;

            string options = File.ReadAllText(opttionsFile, Encoding.Default);
            string enabledPackId = enabledPack?.Id ?? "(internal)";
            options = Regex.Replace(options, "shaderPack=.*", $"shaderPack={enabledPackId}");
            File.WriteAllText(opttionsFile, options, Encoding.Default);
        }

        public void DeleteFromDiskAsync(ShaderPack pack)
        {
            RecycleBinUtil.Send(Enumerable.Repeat(pack.Path, 1));
        }

        #endregion

        #region Helper Methods

        private static ShaderPack Load(string path, string enabledPackId = null)
        {
            bool isZip = path.EndsWith(".zip");

            if (isZip)
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.GetEntry("shaders/composite.fsh") == null &&
                    archive.GetEntry("shaders/world0/composite.fsh") == null)
                {
                    return null;
                }
            }
            else if (!File.Exists(path + "/shaders/composite.fsh") &&
                     !File.Exists(path + "/shaders/world0/composite.fsh"))
            {
                return null;
            }

            return new ShaderPack
            {
                Path = path,
                IsEnabled = (Path.GetFileName(path) == enabledPackId),
                IsExtracted = !isZip
            };
        }

        #endregion
    }
}