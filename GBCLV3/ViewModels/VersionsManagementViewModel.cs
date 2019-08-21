﻿using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using GBCLV3.Models;
using GBCLV3.Models.Launcher;
using GBCLV3.Services;
using GBCLV3.Services.Launcher;
using GBCLV3.Utils;
using Stylet;
using StyletIoC;
using Version = GBCLV3.Models.Launcher.Version;

namespace GBCLV3.ViewModels
{
    class VersionsManagementViewModel : Screen
    {
        #region Events

        public event Action<string> NavigateView;

        #endregion

        #region Private Members

        //IoC
        private readonly IWindowManager _windowManager;

        private readonly Config _config;
        private readonly GamePathService _gamePathService;
        private readonly VersionService _versionService;

        private readonly GameInstallViewModel _gameInstallVM;
        private readonly ForgeInstallViewModel _forgeInstallVM;

        #endregion

        #region Constructor

        [Inject]
        public VersionsManagementViewModel(
            IWindowManager windowManager,
            ConfigService configService,
            GamePathService gamePathService,
            VersionService versionService,
            
            GameInstallViewModel gameInstallVM,
            ForgeInstallViewModel forgeInstallVM)
        {
            _windowManager = windowManager;
            _config = configService.Entries;
            _gamePathService = gamePathService;
            _versionService = versionService;

            Versions = new BindableCollection<Version>();
            _versionService.Loaded += hasAny =>
            {
                Versions.Clear();
                Versions.AddRange(_versionService.GetAvailable());
            };
            _versionService.Created += version => Versions.Insert(0, version);
            _versionService.Deleted += version => Versions.Remove(version);

            _gameInstallVM = gameInstallVM;
            _forgeInstallVM = forgeInstallVM;
        }

        #endregion

        #region Bindings

        public BindableCollection<Version> Versions { get; set; }

        public bool IsSegregateVersions
        {
            get => _config.SegregateVersions;
            set => _config.SegregateVersions = value;
        }

        public string SelectedVersionID { get; set; }

        public void Reload() => _versionService.LoadAll();

        public void OpenVersionsDir()
        {
            Directory.CreateDirectory(_gamePathService.VersionsDir);
            Process.Start(_gamePathService.VersionsDir);
        }

        public void OpenVersionDir()
        {
            var versionsDir = $"{_gamePathService.VersionsDir}/{SelectedVersionID}";
            if (Directory.Exists(versionsDir)) Process.Start(versionsDir);
        }

        public void OpenVersionJson()
        {
            var jsonPath = $"{_gamePathService.VersionsDir}/{SelectedVersionID}/{SelectedVersionID}.json";
            if (File.Exists(jsonPath)) Process.Start(jsonPath);
        }

        public async void DeleteVersion()
        {
            if (_windowManager.ShowMessageBox("${WhetherDeleteVersion} " + SelectedVersionID + " ?", "${DeleteVersion}",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await _versionService.DeleteFromDiskAsync(SelectedVersionID, true);
            }
        }

        public void InstallNewVersion() => NavigateView?.Invoke(null);

        public void InstallForge()
        {
            var version = _versionService.GetByID(SelectedVersionID);
            NavigateView?.Invoke(version.JarID);
        }

        #endregion

        #region Private Methods

        #endregion
    }
}