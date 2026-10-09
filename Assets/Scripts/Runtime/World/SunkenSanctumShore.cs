using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// One bottom-up shore mask shared by painting and foot collision. Eight subcells per tile
    /// round the layout's corners without altering its centres, cliffs, stairs or entrances.
    /// Distance fields are computed over the entire mask, not a moving tile neighbourhood.
    /// </summary>
    public sealed class SunkenSanctumShore
    {
        public const int SamplesPerTile = 8;
        const byte Void = 0, Land = 1, Water = 2;
        const float DistanceCap = 4f;
        const float Diagonal = 1.41421356237f;
        readonly byte[] mask;
        readonly float[] landDistance, waterDistance;
        readonly char[,] source;
        readonly int columns, rows;

        public int Width { get; }
        public int Height { get; }
        public int SampleWidth => columns;
        public int SampleHeight => rows;

        public SunkenSanctumShore(char[,] cells, int width, int height)
        {
            if (cells == null || width <= 0 || height <= 0 ||
                cells.GetLength(0) != width || cells.GetLength(1) != height)
                throw new ArgumentException("Shore cells must match positive world bounds.");
            Width = width; Height = height;
            source = (char[,])cells.Clone();
            columns = width * SamplesPerTile; rows = height * SamplesPerTile;
            mask = new byte[columns * rows];
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                float wx = (x + .5f) / SamplesPerTile, wy = (y + .5f) / SamplesPerTile;
                int tx = x / SamplesPerTile, ty = y / SamplesPerTile;
                char cell = source[tx, ty];
                bool originalLand = SunkenSanctumArt.Land(cell);
                bool protectedCentre = Mathf.Abs(wx - tx - .5f) < .2f && Mathf.Abs(wy - ty - .5f) < .2f;
                bool protectedFloor = cell == 'L' || cell == 'P' || cell == '<' || cell == '>';
                // A low-frequency displacement of at most .09 tiles avoids mechanical arcs.
                float sx = wx + .07f * Mathf.Sin(wy * .93f + wx * .29f) + .02f * Mathf.Sin(wy * 2.17f);
                float sy = wy + .07f * Mathf.Sin(wx * .81f - wy * .35f) + .02f * Mathf.Sin(wx * 1.71f);
                bool isLand = originalLand;
                if (cell != 'W' && !protectedFloor && !protectedCentre)
                    isLand = Blend(sx, sy, true, originalLand ? 1 : -1) > 0;
                if (isLand) { mask[y * columns + x] = Land; continue; }
                // W remains unwalkable. Only its painted water/void boundary may round off.
                bool isWater = cell == '~' || originalLand;
                if (!originalLand && !protectedCentre)
                    isWater = Blend(sx, sy, false, cell == '~' ? 1 : -1) > 0;
                mask[y * columns + x] = isWater ? Water : Void;
            }
            landDistance = DistanceField(Land);
            waterDistance = DistanceField(Water);
        }

        char Cell(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? 'W' : source[x, y];

        float Value(int x, int y, bool forLand, int neutral)
        {
            char cell = Cell(x, y);
            if (forLand) return cell == '~' ? -1 : SunkenSanctumArt.Land(cell) ? 1 : neutral;
            return cell == '~' ? 1 : cell == 'W' ? -1 : neutral;
        }

        float Blend(float x, float y, bool forLand, int neutral)
        {
            x -= .5f; y -= .5f;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            return Mathf.Lerp(Mathf.Lerp(Value(ix, iy, forLand, neutral), Value(ix + 1, iy, forLand, neutral), fx),
                              Mathf.Lerp(Value(ix, iy + 1, forLand, neutral), Value(ix + 1, iy + 1, forLand, neutral), fx), fy);
        }

        byte At(int x, int y) => x < 0 || y < 0 || x >= columns || y >= rows ? Void : mask[y * columns + x];
        byte At(float x, float y) => At(Mathf.FloorToInt(x * SamplesPerTile), Mathf.FloorToInt(y * SamplesPerTile));

        public bool IsLand(float worldX, float worldY) => At(worldX, worldY) == Land;
        public bool IsWater(float worldX, float worldY) => At(worldX, worldY) == Water;
        public bool IsVoid(float worldX, float worldY) => At(worldX, worldY) == Void;

        /// <summary>
        /// Exact circle clearance against the shared subcell rectangles. The chamfer fields are
        /// for smooth artwork, not collision-distance assertions. Pass the physical query radius
        /// plus Physics2D.defaultContactOffset when comparing with tolerant physics overlaps.
        /// </summary>
        public bool IsLandCircleClear(float worldX, float worldY, float radius)
        {
            if (radius < 0 || worldX - radius < 0 || worldY - radius < 0 ||
                worldX + radius >= Width || worldY + radius >= Height || !IsLand(worldX, worldY)) return false;
            int left = Mathf.FloorToInt((worldX - radius) * SamplesPerTile);
            int right = Mathf.FloorToInt((worldX + radius) * SamplesPerTile);
            int bottom = Mathf.FloorToInt((worldY - radius) * SamplesPerTile);
            int top = Mathf.FloorToInt((worldY + radius) * SamplesPerTile);
            float radiusSquared = radius * radius;
            for (int y = bottom; y <= top; y++) for (int x = left; x <= right; x++)
            {
                if (At(x, y) == Land) continue;
                float dx = Mathf.Max(x / (float)SamplesPerTile - worldX, Mathf.Max(0, worldX - (x + 1f) / SamplesPerTile));
                float dy = Mathf.Max(y / (float)SamplesPerTile - worldY, Mathf.Max(0, worldY - (y + 1f) / SamplesPerTile));
                if (dx * dx + dy * dy <= radiusSquared) return false;
            }
            return true;
        }

        /// <summary>Positive on stone, negative outside. Continuous bilinear chamfer distance for shading.</summary>
        public float LandDistance(float worldX, float worldY) => SampleDistance(landDistance, worldX, worldY);
        /// <summary>Positive in water, negative outside, useful for the dark outer pool bank.</summary>
        public float WaterDistance(float worldX, float worldY) => SampleDistance(waterDistance, worldX, worldY);

        /// <summary>
        /// Nonland subcells not already covered by the unchanged W cliff tilemap. A rounded-off
        /// piece of a ~ tile remains blocked even when its appearance becomes the outer void bank.
        /// </summary>
        public bool NeedsWaterCollision(int sampleX, int sampleY) =>
            At(sampleX, sampleY) != Land && Cell(sampleX / SamplesPerTile, sampleY / SamplesPerTile) != 'W';

        float[] DistanceField(byte kind)
        {
            var field = new float[mask.Length];
            float cap = DistanceCap * SamplesPerTile;
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                bool inside = At(x, y) == kind;
                bool boundary = (At(x - 1, y) == kind) != inside || (At(x + 1, y) == kind) != inside ||
                                (At(x, y - 1) == kind) != inside || (At(x, y + 1) == kind) != inside;
                field[y * columns + x] = boundary ? .5f : cap;
            }
            // Two chamfer sweeps propagate to the closest boundary across the entire map.
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                int i = y * columns + x; float value = field[i];
                if (x > 0) value = Mathf.Min(value, field[i - 1] + 1);
                if (y > 0)
                {
                    value = Mathf.Min(value, field[i - columns] + 1);
                    if (x > 0) value = Mathf.Min(value, field[i - columns - 1] + Diagonal);
                    if (x + 1 < columns) value = Mathf.Min(value, field[i - columns + 1] + Diagonal);
                }
                field[i] = value;
            }
            for (int y = rows - 1; y >= 0; y--) for (int x = columns - 1; x >= 0; x--)
            {
                int i = y * columns + x; float value = field[i];
                if (x + 1 < columns) value = Mathf.Min(value, field[i + 1] + 1);
                if (y + 1 < rows)
                {
                    value = Mathf.Min(value, field[i + columns] + 1);
                    if (x > 0) value = Mathf.Min(value, field[i + columns - 1] + Diagonal);
                    if (x + 1 < columns) value = Mathf.Min(value, field[i + columns + 1] + Diagonal);
                }
                field[i] = value;
            }
            for (int i = 0; i < field.Length; i++) field[i] = Mathf.Min(cap, field[i]) / SamplesPerTile * (mask[i] == kind ? 1 : -1);
            return field;
        }

        float SampleDistance(float[] field, float worldX, float worldY)
        {
            if (worldX < 0 || worldY < 0 || worldX >= Width || worldY >= Height) return -DistanceCap;
            float x = Mathf.Clamp(worldX * SamplesPerTile - .5f, 0, columns - 1);
            float y = Mathf.Clamp(worldY * SamplesPerTile - .5f, 0, rows - 1);
            int ix = (int)x, iy = (int)y, nx = Mathf.Min(ix + 1, columns - 1), ny = Mathf.Min(iy + 1, rows - 1);
            return Mathf.Lerp(Mathf.Lerp(field[iy * columns + ix], field[iy * columns + nx], x - ix),
                              Mathf.Lerp(field[ny * columns + ix], field[ny * columns + nx], x - ix), y - iy);
        }
    }
}
