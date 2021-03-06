﻿using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GBCLV3.Models;
using GBCLV3.Utils;
using GBCLV3.Utils.Native;
using Stylet;
using StyletIoC;

namespace GBCLV3.Services
{
    public class ThemeService : PropertyChangedBase
    {
        #region Binding Properties

        public static string[] ImageExtenstions { get; } = { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".tif", ".tiff", ".webp", ".heif", ".heic", ".avif" };

        public BitmapImage BackgroundImage { get; private set; }

        public StreamGeometry BackgroundIcon { get; private set; }

        public string FontFamily
        {
            get => _config.FontFamily;
            set => _config.FontFamily = value;
        }

        public string FontWeight
        {
            get => _config.FontWeight;
            set => _config.FontWeight = value;
        }

        public string[] FontWeights { get; } = { "Light", "Normal", "Medium", "SemiBold", "Bold" };

        #endregion

        #region Private Fields

        private const string ICONS_SOURCE = "/GBCL;component/Resources/Styles/Icons.xaml";
        private const string DEFAULT_BACKGROUND_IMAGE = "pack://application:,,,/Resources/Images/default_background.png";

        private static readonly Color REF_COLOR_SPIKE = Color.FromRgb(15, 105, 200);
        private static readonly Color REF_COLOR_BULLZEYE = Color.FromRgb(210, 50, 55);
        private static readonly Color REF_COLOR_TBONE = Color.FromRgb(165, 125, 10);
        private static readonly Color REF_COLOR_STEGZ = Color.FromRgb(105, 175, 15);

        private readonly Config _config;
        private readonly LogService _logService;

        #endregion

        #region Constructor

        [Inject]
        public ThemeService(ConfigService configService, LogService logService)
        {
            _logService = logService;
            _config = configService.Entries;

            if (string.IsNullOrEmpty(_config.FontFamily))
            {
                if (_config.Language.StartsWith("zh"))
                {
                    _config.FontFamily = "Microsoft YaHei UI";
                }
                else
                {
                    _config.FontFamily = "Segoe UI";
                }
            }
        }

        #endregion

        #region Public Methods

        public void UpdateBackgroundImage()
        {
            if (!_config.UseBackgroundImage)
            {
                BackgroundImage = null;
                return;
            }

            string imgPath = null;

            if (File.Exists(_config.BackgroundImagePath))
            {
                imgPath = _config.BackgroundImagePath;
            }
            else
            {
                _config.BackgroundImagePath = null;

                string imgSearchDir = Environment.CurrentDirectory + "/bg";
                if (Directory.Exists(imgSearchDir))
                {
                    string[] imgFiles = Directory.EnumerateFiles(imgSearchDir)
                                                 .Where(file => ImageExtenstions.Any(file.ToLower().EndsWith))
                                                 .ToArray();

                    if (imgFiles.Any())
                    {
                        var rand = new Random();
                        imgPath = imgFiles[rand.Next(imgFiles.Length)];
                    }
                }
            }

            _logService.Info(nameof(ThemeService), $"Changing background image: \"{imgPath ?? "DEFAULT"}\"");

            BackgroundImage = new BitmapImage();
            BackgroundImage.BeginInit();
            BackgroundImage.UriSource = new Uri(imgPath ?? DEFAULT_BACKGROUND_IMAGE);
            BackgroundImage.CacheOption = BitmapCacheOption.OnLoad;
            BackgroundImage.EndInit();
            BackgroundImage.Freeze();
        }

        public void SetBackgroundEffect(Window window)
        {
            _logService.Info(nameof(ThemeService), $"Changing background effect: \"{_config.BackgroundEffect}\"");

            var handle = new WindowInteropHelper(window).Handle;
            WindowEffectUtil.Apply(handle, _config.BackgroundEffect);
        }

        public ImmutableArray<string> GetSystemFontNames()
        {
            return Fonts.SystemFontFamilies
                .AsParallel()
                .Select(fontFamily =>
                {
                    var nameDict = fontFamily.FamilyNames;

                    if (nameDict.TryGetValue(XmlLanguage.GetLanguage(_config.Language), out string fontName))
                    {
                        return fontName;
                    }

                    if (nameDict.TryGetValue(XmlLanguage.GetLanguage("en-US"), out fontName))
                    {
                        return fontName;
                    }

                    return null;
                })
                .Where(fontName => !string.IsNullOrEmpty(fontName))
                .OrderBy(fontName => fontName)
                .ToImmutableArray();
        }

        public void LoadBackgroundIcon(Color accentColor)
        {
            ResourceDictionary iconsDict = null;
            foreach (var dict in Application.Current.Resources.MergedDictionaries)
            {
                if (dict.Source?.ToString() == ICONS_SOURCE)
                {
                    iconsDict = dict;
                    break;
                }
            }

            float l2NormSpike = ColorUtil.CalcL2Norm(accentColor, REF_COLOR_SPIKE);
            float l2NormBullzeye = ColorUtil.CalcL2Norm(accentColor, REF_COLOR_BULLZEYE);
            float l2NormTBone = ColorUtil.CalcL2Norm(accentColor, REF_COLOR_TBONE);
            float l2NormStegz = ColorUtil.CalcL2Norm(accentColor, REF_COLOR_STEGZ);

#if DEBUG
            _logService.Debug(nameof(ThemeService), $"Theme color L2 norm to the Triceratop: {l2NormSpike:F4}");
            _logService.Debug(nameof(ThemeService), $"Theme color L2 norm to the Pteranodon: {l2NormBullzeye:F4}");
            _logService.Debug(nameof(ThemeService), $"Theme color L2 norm to the Tyrannosaurus: {l2NormTBone:F4}");
            _logService.Debug(nameof(ThemeService), $"Theme color L2 norm to the Stegosaurus: {l2NormStegz:F4}");
#endif

            if (l2NormSpike < 0.0075f)
            {
                BackgroundIcon = iconsDict["Spike"] as StreamGeometry;
            }
            else if (l2NormBullzeye < 0.0005f)
            {
                BackgroundIcon = iconsDict["Bullzeye"] as StreamGeometry;
            }
            else if (l2NormTBone < 0.0082f)
            {
                BackgroundIcon = iconsDict["T-Bone"] as StreamGeometry;
            }
            else if (l2NormStegz < 0.0090f)
            {
                BackgroundIcon = iconsDict["Stegz"] as StreamGeometry;
            }
            else
            {
                BackgroundIcon = iconsDict["DragonIcon"] as StreamGeometry;
            }
        }

        #endregion
    }
}
