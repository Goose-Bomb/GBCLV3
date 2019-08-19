using System.Linq;
using System.Windows;
using System.Windows.Threading;
using GBCLV3.Services;
using GBCLV3.Services.Launcher;
using GBCLV3.Utils;
using GBCLV3.ViewModels.Windows;
using GBCLV3.Views.Windows;
using Stylet;
using StyletIoC;

namespace GBCLV3
{
    class Bootstrapper : Bootstrapper<MainViewModel>
    {
        protected override void ConfigureIoC(IStyletIoCBuilder builder)
        {
            // Configure the IoC container in here
            builder.Bind<ConfigService>().ToSelf().InSingletonScope();
            builder.Bind<LanguageService>().ToSelf().InSingletonScope();
            builder.Bind<ThemeService>().ToSelf().InSingletonScope();

            builder.Bind<GamePathService>().ToSelf().InSingletonScope();
            builder.Bind<UrlService>().ToSelf().InSingletonScope();
            builder.Bind<VersionService>().ToSelf().InSingletonScope();
            builder.Bind<LibraryService>().ToSelf().InSingletonScope();
            builder.Bind<AssetService>().ToSelf().InSingletonScope();
            builder.Bind<LaunchService>().ToSelf().InSingletonScope();

            //Custom MessageBox
            builder.Bind<IMessageBoxViewModel>().To<MessageViewModel>();
        }

        protected override void OnStart()
        {
            base.OnStart();
        }

        protected override void Configure()
        {
            // Apply settings before start

            var configService = this.Container.Get<ConfigService>();
            configService.Load();

            var langService = this.Container.Get<LanguageService>();
            langService.Change(configService.Entries.Language);

            // Unable to inject the service using IoC, have to make it a static property
            LocalizedDescriptionAttribute.LanguageService = langService;

            var backgroundImageService = this.Container.Get<ThemeService>();
            backgroundImageService.UpdateBackgroundImage();

            // Override AccentColors
            Application.Current.Resources[AdonisUI.Colors.AccentColor]
                = NativeUtil.GetSystemColorByName("ImmersiveStartSelectionBackground");

            Application.Current.Resources[AdonisUI.Colors.AccentInteractionColor]
                = NativeUtil.GetSystemColorByName("ImmersiveStartBackground");

            Application.Current.Resources[AdonisUI.Colors.AccentIntenseHighlightColor]
                = NativeUtil.GetSystemColorByName("ImmersiveStartFolderBackground");

            Application.Current.Resources[AdonisUI.Colors.DisabledAccentForegroundColor]
                = NativeUtil.GetSystemColorByName("ImmersiveStartDisabledText");

            Application.Current.Resources[AdonisUI.Colors.AccentHighlightColor]
                = NativeUtil.GetSystemColorByName("ImmersiveStartSecondaryText");
        }

        protected override void OnUnhandledException(DispatcherUnhandledExceptionEventArgs e)
        {
            base.OnUnhandledException(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var configService = this.Container.Get<ConfigService>();
            configService.Save();
            base.OnExit(e);
        }
    }
}
