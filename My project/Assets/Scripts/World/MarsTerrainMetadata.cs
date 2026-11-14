using System;

namespace MarsSurvival.World
{
    /// <summary>
    /// Provenance and georeferencing written by tools/data/fetch_mars_dem.py.
    /// Field names match the JSON keys so JsonUtility can load it directly.
    /// All distances are real Mars metres; nothing here is scaled for the game.
    /// </summary>
    [Serializable]
    public class MarsTerrainMetadata
    {
        public string name;
        public int width;
        public int height;
        public string verticalReference;
        public int minElevationM;
        public int maxElevationM;
        public double latNorthDeg;
        public double latSouthDeg;
        public double lonWestDeg;
        public double lonEastDeg;
        public double metersPerSampleNorthSouth;
        public double metersPerSampleEastWest;
        public double eastWestScaleErrorFraction;
        public string sourceProductId;
        public string sourceUrl;
        public string sourceInstruments;
        public double sourceGridSpacingM;
        public long sourceFileBytes;
        public int noDataSamplesFilled;
        public string resampling;
        public long downloadedBytes;
        public int downloadRequests;
        public long heightmapBytes;
        public string heightmapSha256;
        public string retrievedUtc;
    }
}
