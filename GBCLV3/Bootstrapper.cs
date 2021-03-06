using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GBCLV3.Models;
using GBCLV3.Models.Authentication;
using GBCLV3.Services;
using GBCLV3.Services.Authentication;
using GBCLV3.Services.Auxiliary;
using GBCLV3.Services.Download;
using GBCLV3.Services.Installation;
using GBCLV3.Services.Launch;
using GBCLV3.Utils;
using GBCLV3.Utils.Binding;
using GBCLV3.Utils.Native;
using GBCLV3.ViewModels;
using GBCLV3.ViewModels.Tabs;
using GBCLV3.ViewModels.Windows;
using GBCLV3.Views.Windows;
using Stylet;
using StyletIoC;

namespace GBCLV3
{
    internal class Bootstrapper : Bootstrapper<MainViewModel>
    {
        protected override void ConfigureIoC(IStyletIoCBuilder builder)
        {
            // Configure the IoC container in here

            builder.Bind<HttpClient>()
                .ToFactory(builder => new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                .InSingletonScope();

            builder.Bind<LogService>().ToSelf().InSingletonScope();
            builder.Bind<ConfigService>().ToSelf().InSingletonScope();
            builder.Bind<LanguageService>().ToSelf().InSingletonScope();
            builder.Bind<UpdateService>().ToSelf().InSingletonScope();
            builder.Bind<ThemeService>().ToSelf().InSingletonScope();

            builder.Bind<GamePathService>().ToSelf().InSingletonScope();
            builder.Bind<DownloadUrlService>().ToSelf().InSingletonScope();
            builder.Bind<DownloadService>().ToSelf().InSingletonScope();
            builder.Bind<VersionService>().ToSelf().InSingletonScope();
            builder.Bind<LibraryService>().ToSelf().InSingletonScope();
            builder.Bind<AssetService>().ToSelf().InSingletonScope();
            builder.Bind<AuthService>().ToSelf().InSingletonScope();
            builder.Bind<AccountService>().ToSelf().InSingletonScope();
            builder.Bind<LaunchService>().ToSelf().InSingletonScope();
            builder.Bind<ForgeInstallService>().ToSelf().InSingletonScope();
            builder.Bind<FabricInstallService>().ToSelf().InSingletonScope();
            builder.Bind<ModService>().ToSelf().InSingletonScope();
            builder.Bind<ResourcePackService>().ToSelf().InSingletonScope();
            builder.Bind<ShaderPackService>().ToSelf().InSingletonScope();
            builder.Bind<SkinService>().ToSelf().InSingletonScope();

            // To share these two VMs among other VMs, they must be singletons
            builder.Bind<VersionsManagementViewModel>().ToSelf().InSingletonScope();
            builder.Bind<GreetingViewModel>().ToSelf().InSingletonScope();

            // Custom MessageBox
            builder.Bind<IMessageBoxViewModel>().To<MessageViewModel>();

            // Validation
            builder.Bind(typeof(IModelValidator<>)).ToAllImplementations();
        }

        protected override void OnStart()
        {
            base.OnStart();
        }

        protected override void Configure()
        {
            var logService = this.Container.Get<LogService>();
            logService.Info(nameof(Bootstrapper), $"GBCL verison: {AssemblyUtil.Version}");

            // Apply settings before start
            var configService = this.Container.Get<ConfigService>();
            configService.Load();

            var langService = this.Container.Get<LanguageService>();

            // Unable to inject the service using IoC, have to make it a static property
            LocalizedDescriptionAttribute.LanguageService = langService;

            // Override AccentColors
            var accentColor = AccentColorUtil.GetSystemColorByName("ImmersiveStartSelectionBackground");
            Application.Current.Resources[AdonisUI.Colors.AccentColor] = accentColor;

            Application.Current.Resources[AdonisUI.Colors.AccentInteractionColor]
                = AccentColorUtil.GetSystemColorByName("ImmersiveStartBackground");

            Application.Current.Resources[AdonisUI.Colors.AccentIntenseHighlightColor]
                = AccentColorUtil.GetSystemColorByName("ImmersiveStartFolderBackground");

            Application.Current.Resources[AdonisUI.Colors.DisabledAccentForegroundColor]
                = AccentColorUtil.GetSystemColorByName("ImmersiveStartDisabledText");

            Application.Current.Resources[AdonisUI.Colors.AccentHighlightColor]
                = AccentColorUtil.GetSystemColorByName("ImmersiveStartSecondaryText");

            var themeService = this.Container.Get<ThemeService>();

            // Why load the background icon needs accent color?
            // Well...you'll see
            themeService.LoadBackgroundIcon(accentColor);
        }

        protected override void OnLaunch()
        {
            var logService = this.Container.Get<LogService>();

            if (this.Args.Any() && this.Args[0] == "updated")
            {
                logService.Info(nameof(Bootstrapper), $"Auto updated to new version: {AssemblyUtil.Version}.");

                var windowManager = this.Container.Get<IWindowManager>();
                windowManager.ShowMessageBox("${SuccessfullyUpdatedTo} v" + AssemblyUtil.Version, "${UpdateSuccessful}",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var configService = this.Container.Get<ConfigService>();
                if (configService.Entries.AutoCheckUpdate)
                {
                    CheckUpdateAsync().ConfigureAwait(false);
                }
            }

            CheckAccount().ConfigureAwait(false);
            base.OnLaunch();
        }

        protected override void OnUnhandledException(DispatcherUnhandledExceptionEventArgs e)
        {
            var logService = this.Container.Get<LogService>();
            var windowManager = this.Container.Get<IWindowManager>();
            var errorReportVM = this.Container.Get<ErrorReportViewModel>();

            logService.Fatal("OnUnhandledException", e.Exception.Message);

            errorReportVM.Setup(ErrorReportType.UnhandledException);
            windowManager.ShowDialog(errorReportVM);

            this.Application.Shutdown(e.Exception.HResult);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var configService = this.Container.Get<ConfigService>();
            configService.Save();

            var logService = this.Container.Get<LogService>();
            logService.Info(nameof(Bootstrapper), "Exiting");
            logService.Finish();

#if !DEBUG
            logService.ClearLogs();
#endif

            base.OnExit(e);
        }

        private async Task CheckUpdateAsync()
        {
            var updateService = this.Container.Get<UpdateService>();
            var info = await updateService.CheckAsync();

            if (info != null)
            {
                var updateVM = this.Container.Get<UpdateViewModel>();
                var windowManager = this.Container.Get<IWindowManager>();
                updateVM.Setup(info);
                windowManager.ShowWindow(updateVM);
            }
        }

        private async Task CheckAccount()
        {
            var accountService = this.Container.Get<AccountService>();
            if (!accountService.Any())
            {
                var windowManager = this.Container.Get<IWindowManager>();
                var accountEditVM = this.Container.Get<AccountEditViewModel>();

                accountEditVM.Setup(AccountEditType.AddAccount);

                if (windowManager.ShowDialog(accountEditVM) ?? false)
                {
                    await accountService.LoadSkinsForAllAsync();

                    var greetingVM = this.Container.Get<GreetingViewModel>();
                    greetingVM.NotifyAccountChanged();
                    greetingVM.IsShown = true;
                }
            }
        }
    }
}