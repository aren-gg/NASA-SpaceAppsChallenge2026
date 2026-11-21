namespace MarsTrainer.Sim
{
    public static class GameConstants
    {
        public const float SettingsLargeTextScale = 1.2f;
        public const float SettingsVolumeStep = 0.5f;
        public const int UiBadgeSegments = 12;
        public const float UiBadgeDiameter = 48;
        public const float UiBadgeInset = 8;
        public const uint UiIconInk = 0x15181C;
        // Vital colours from the Okabe-Ito colour-blind-safe palette, brightened for the HUD:
        // no red/green pair, and each vital also has its own pictogram and label.
        public const uint UiOxygen = 0xE6F2F5;   // near-white, like a helmet O2 readout
        public const uint UiHealth = 0xFF6B3D;   // vermilion
        public const uint UiFood = 0x2CC79A;     // bluish green
        public const uint UiWater = 0x56B4E9;    // sky blue
        public const uint UiRest = 0xE08AC0;     // reddish purple
        public const uint UiPower = 0xF5C451;
        public const uint UiRadiation = 0xC9A2FF;
        /// <summary>Historical name: now the amber CRT accent (see MarsTrainer.UI.UiTheme).</summary>
        public const uint UiCopper = 0xFFB547;
        // P10 presentation tokens, in reference-canvas pixels. No simulation balance here.
        public const float UiWidth = 1920;
        public const float UiHeight = 1080;
        public const float UiGap = 12;
        public const float UiBody = 30;
        public const float UiHeading = 36;
        public const float UiTitle = 48;
        public const float UiLine = 48;
        public const float UiTextLineSpacing = 4;
        public const float UiTextWordSpacing = 3;
        public const float UiControlRowHeight = 44;
        public const float UiControlIndent = 24;
        public const float UiControlKeyFraction = .43f;
        public const float UiButton = 64;
        public const float UiCard = 136;
        public const uint UiSurface = 0x15181C;
        public const uint UiText = 0xECE6DA;
        public const uint UiDanger = 0xFF5A4A;
        public const uint UiButtonColor = 0x3A3F46;
        public const double StatMaximum = 100;
        // Bundled Mars-year schedule (StreamingAssets) bounds and defaults, used by MarsTrainer.Data.
        public const int ScheduleSols = 668;
        public const int DataSeed = 668;
        public const float FullOrbitDegrees = 360f;
        public const float DustSeasonStart = 180f;
        public const float MinAirTemperature = -150f;
        public const float MaxAirTemperature = 50f;
        public const float MaxDustOpacity = 10f;
        public const float MaxGcrDose = 2000f;
        public const int MaxFlareLevel = 3;
        public const int AuroraFlareThreshold = 2;
        public const float DefaultMinTemperature = -85f;
        public const float TemperatureSeasonAmplitude = 12f;
        public const float DefaultTemperatureSpan = 65f;
        public const float DefaultClearTau = 0.35f;
        public const float DefaultDustSeasonTau = 1.2f;
        public const float DefaultTauNoise = 0.15f;
        public const float DefaultGcrDose = 210f;
        public const float DefaultGcrVariation = 18f;
        public const int FlarePeriodSols = 53;
        public const int FlareClusterSols = 4;
    }
}
