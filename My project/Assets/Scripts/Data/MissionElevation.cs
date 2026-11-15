using System;
using Newtonsoft.Json.Linq;

namespace MarsTrainer.Data
{
    // Reuses the survey scene's bundled DEM. The mission deliberately compresses its relief
    // and horizontal extent to a walkable arena; this is not a georeferenced survey view.
    public sealed class MissionElevation
    {
        private readonly byte[] bytes;
        private readonly int width, height, minimum, range;

        public MissionElevation(byte[] bytes, string metadata)
        {
            var json = JObject.Parse(metadata);
            width = (int)json["width"]; height = (int)json["height"];
            minimum = (int)json["minElevationM"];
            range = (int)json["maxElevationM"] - minimum;
            if (width < 2 || height < 2 || range <= 0 || bytes == null ||
                (long)width * height * sizeof(short) != bytes.Length)
                throw new ArgumentException("Elevation data and metadata do not match.");
            this.bytes = bytes;
        }

        public float Sample(float east, float north)
        {
            float x = Math.Max(0, Math.Min(1, east)) * (width - 1);
            float z = Math.Max(0, Math.Min(1, north)) * (height - 1);
            int ix = Math.Min((int)x, width - 2), iz = Math.Min((int)z, height - 2);
            float south = At(ix, iz) + (At(ix + 1, iz) - At(ix, iz)) * (x - ix);
            float upper = At(ix, iz + 1) + (At(ix + 1, iz + 1) - At(ix, iz + 1)) * (x - ix);
            return Math.Max(0, Math.Min(1, (south + (upper - south) * (z - iz) - minimum) / range));
        }

        private short At(int x, int z)
        {
            int offset = ((height - 1 - z) * width + x) * sizeof(short);
            return unchecked((short)(bytes[offset] | bytes[offset + 1] << 8));
        }
    }
}
