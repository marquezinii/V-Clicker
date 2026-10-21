using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VClicker
{
    /// <summary>What a search for a captured image found in a screenshot.</summary>
    internal sealed class ImageMatch
    {
        /// <summary>Where the image is (screenshot coordinates): only when exactly one place matches.</summary>
        public Rectangle? Found;
        /// <summary>Places that look like the image (0 = nowhere, 2 or more = ambiguous).</summary>
        public int Count;
        /// <summary>The best similarity seen (-1 = no candidate at all).</summary>
        public double Best = -1;
        /// <summary>The similarity of the next most similar place (-1 = none): how distinct the image is.</summary>
        public double Second = -1;
    }

    /// <summary>One region size offered at F7.</summary>
    internal sealed class ImageOption
    {
        public int Side;
        /// <summary>The region around the point (null when it does not fit in the screenshot).</summary>
        public Bitmap Crop;
        /// <summary>None = usable: recognizable, found once, and clearly different from everything else on the screen.</summary>
        public ImageProblem Problem;
        /// <summary>How many places look like it (when it is not unique).</summary>
        public int Count;
    }

    internal enum ImageProblem { None, OutOfWindow, Plain, NotUnique, TooSimilar }

    /// <summary>
    /// Finds a small captured image in a screenshot (see docs/superpowers/specs/2026-10-23-smart-target-design.md).
    /// A coarse pass at 1/4 resolution proposes places by normalized cross-correlation of gray levels; each place is
    /// then judged at full resolution in cells of 8x8 pixels: every cell must correlate, so a similar button with
    /// a different word fails, while a uniform change of brightness and contrast inside a cell (a hover highlight)
    /// does not matter. A loose check of the mean colour separates shapes in very different colours. Same scale only.
    /// Pure functions over bitmaps: tested without a screen. Limits calibrated on Chrome and Windows button renders.
    /// </summary>
    internal static class ImageTarget
    {
        /// <summary>Region sizes offered at F7 (pixels per side).</summary>
        public static readonly int[] Sides = { 32, 64, 96 };
        /// <summary>Worst-cell similarity needed to count as the target.</summary>
        public const double FoundScore = 0.80;
        /// <summary>Worst-cell similarity from which another place counts as a look-alike (the target is then ambiguous).</summary>
        public const double LookAlikeScore = 0.70;
        /// <summary>Largest per-channel difference of the mean colour.</summary>
        public const int ColorTolerance = 64;
        /// <summary>Gray-level standard deviation below which a region is too plain to be recognized.</summary>
        public const double MinDetail = 10;
        /// <summary>
        /// At F7, the next most similar place on the screen must stay below this: a region that only differs from
        /// something else by a sliver (a crop with "xar P" next to "xar C") could later match a look-alike.
        /// </summary>
        public const double DistinctScore = 0.40;

        private const int Cell = 8;             // side of one grid cell: a letter's width, so one changed letter shows
        private const double FlatStd = 4;       // a cell this plain carries no shape, only "plain"
        private const int CoarseSide = 16;      // the coarse pass shrinks the image to about 16x16
        private const int Stride = 4;           // cells overlap by half, so a difference on a cell border still shows
        private const double CoarseScore = 0.5; // loose: the coarse pass only proposes places
        private const int MaxCandidates = 400;

        private sealed class Gray
        {
            public int W, H;
            public float[] P;
            public int[] Argb;
            public double[] Sum, Sq; // integral images, (W + 1) x (H + 1): only for the small coarse image

            /// <summary>The sum and the sum of squares of a region's gray levels.</summary>
            public void Stats(int x, int y, int w, int h, out double sum, out double sq)
            {
                if (Sum != null)
                {
                    sum = Region(Sum, x, y, w, h);
                    sq = Region(Sq, x, y, w, h);
                    return;
                }
                // Full resolution: summed where needed instead of two integral images of doubles (about 130 MB at 4K).
                sum = sq = 0;
                for (int j = 0; j < h; j++)
                {
                    int row = (y + j) * W + x;
                    for (int i = 0; i < w; i++)
                    {
                        double v = P[row + i];
                        sum += v;
                        sq += v * v;
                    }
                }
            }

            private double Region(double[] table, int x, int y, int w, int h)
            {
                int stride = W + 1;
                return table[(y + h) * stride + x + w] - table[y * stride + x + w] - table[(y + h) * stride + x] + table[y * stride + x];
            }
        }

        private sealed class Template
        {
            public int W, H;
            public float[] Zero; // gray levels minus their mean
            public double Norm2, Detail;
            public int R, G, B;
            public int Columns, Rows;
            public float[][] CellZero;
            public double[] CellNorm2, CellStd;
        }

        /// <summary>The gray-level standard deviation of a region: below <see cref="MinDetail"/> it cannot be recognized.</summary>
        public static double Detail(Bitmap image) => Prepare(ToGray(image), false).Detail;

        /// <summary>
        /// The region sizes around <paramref name="at"/> (screenshot coordinates), each checked against the
        /// screenshot itself. The first usable one (the smallest) is the default: a small region carries less of the
        /// surroundings, which may change.
        /// </summary>
        public static List<ImageOption> Options(Bitmap screen, Point at)
        {
            var options = new List<ImageOption>();
            foreach (int side in Sides)
            {
                var option = new ImageOption { Side = side };
                options.Add(option);
                var area = new Rectangle(at.X - side / 2, at.Y - side / 2, side, side);
                if (!new Rectangle(Point.Empty, screen.Size).Contains(area))
                {
                    option.Problem = ImageProblem.OutOfWindow;
                    continue;
                }
                option.Crop = screen.Clone(area, PixelFormat.Format32bppArgb);
                if (Detail(option.Crop) < MinDetail)
                {
                    option.Problem = ImageProblem.Plain;
                    continue;
                }
                ImageMatch self = Search(screen, option.Crop);
                option.Count = self.Count;
                if (!self.Found.HasValue || self.Found.Value.Location != area.Location) option.Problem = ImageProblem.NotUnique;
                else if (self.Second >= DistinctScore) option.Problem = ImageProblem.TooSimilar;
            }
            return options;
        }

        /// <summary>Searches the whole screenshot for the image.</summary>
        public static ImageMatch Search(Bitmap screen, Bitmap image)
        {
            Gray big = ToGray(screen), small = ToGray(image);
            Template t = Prepare(small, true);
            var result = new ImageMatch();
            if (t.Detail < MinDetail || t.W > big.W || t.H > big.H) return result;

            // Coarse pass: every place at 1/4 resolution, keeping local maxima above a loose score.
            int factor = Math.Max(1, Math.Min(t.W, t.H) / CoarseSide);
            Gray coarseBig = Down(big, factor), coarseSmall = Down(small, factor);
            Template ct = Prepare(coarseSmall, false);
            if (ct.Detail <= 0 || ct.W > coarseBig.W || ct.H > coarseBig.H) return result;
            int cw = coarseBig.W - ct.W + 1, ch = coarseBig.H - ct.H + 1;
            var scores = new double[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    scores[y * cw + x] = Score(coarseBig, x, y, ct);
            var candidates = new List<Point>();
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    if (scores[y * cw + x] >= CoarseScore && LocalMax(scores, cw, ch, x, y)) candidates.Add(new Point(x, y));
            if (candidates.Count > MaxCandidates)
            {
                result.Count = candidates.Count; // too many look-alikes to tell apart: ambiguous, never a guess
                return result;
            }

            // Fine pass: each candidate aligned at full resolution around the coarse place, then judged cell by cell.
            var places = new List<KeyValuePair<double, Point>>();
            foreach (Point candidate in candidates)
            {
                double aligned = double.MinValue;
                Point at = Point.Empty;
                int x0 = Math.Max(0, candidate.X * factor - factor), x1 = Math.Min(big.W - t.W, candidate.X * factor + factor);
                int y0 = Math.Max(0, candidate.Y * factor - factor), y1 = Math.Min(big.H - t.H, candidate.Y * factor + factor);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        double s = Score(big, x, y, t);
                        if (s > aligned) { aligned = s; at = new Point(x, y); }
                    }
                if (aligned > double.MinValue) places.Add(new KeyValuePair<double, Point>(CellScore(big, at.X, at.Y, t), at));
            }

            // Distinct places: a place overlapping a better one by more than half is the same place.
            places.Sort((a, b) => b.Key.CompareTo(a.Key));
            var kept = new List<KeyValuePair<double, Point>>();
            foreach (KeyValuePair<double, Point> place in places)
            {
                bool same = false;
                foreach (KeyValuePair<double, Point> other in kept)
                {
                    Rectangle overlap = Rectangle.Intersect(new Rectangle(place.Value, new Size(t.W, t.H)), new Rectangle(other.Value, new Size(t.W, t.H)));
                    if (overlap.Width * overlap.Height * 2 > t.W * t.H) { same = true; break; }
                }
                if (!same) kept.Add(place);
            }
            if (kept.Count > 0) result.Best = kept[0].Key;
            if (kept.Count > 1) result.Second = kept[1].Key;
            kept.RemoveAll(place => place.Key < LookAlikeScore);
            result.Count = kept.Count;
            if (kept.Count == 1 && kept[0].Key >= FoundScore && ColorClose(big, kept[0].Value, t))
                result.Found = new Rectangle(kept[0].Value, new Size(t.W, t.H));
            return result;
        }

        /// <summary>Whether the image is exactly at <paramref name="at"/> (the last look right before a click).</summary>
        public static bool IsAt(Bitmap screen, Bitmap image, Point at)
        {
            Gray big = ToGray(screen);
            Template t = Prepare(ToGray(image), true);
            if (t.Detail < MinDetail || at.X < 0 || at.Y < 0 || at.X + t.W > big.W || at.Y + t.H > big.H) return false;
            return CellScore(big, at.X, at.Y, t) >= FoundScore && ColorClose(big, at, t);
        }

        private static bool LocalMax(double[] scores, int w, int h, int x, int y)
        {
            double s = scores[y * w + x];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    double n = scores[ny * w + nx];
                    // Ties go to the first in reading order, so a flat plateau yields one place, not several.
                    if (n > s || (n == s && (dy < 0 || (dy == 0 && dx < 0)))) return false;
                }
            return true;
        }

        /// <summary>Normalized cross-correlation of the whole template with the region at (x, y); 0 for a plain region.</summary>
        private static double Score(Gray big, int x, int y, Template t)
        {
            int n = t.W * t.H;
            big.Stats(x, y, t.W, t.H, out double sum, out double sq);
            double variance = sq - sum * sum / n;
            if (variance <= 1e-6 * n) return 0;
            return Correlate(big, x, y, t.W, t.H, t.Zero) / Math.Sqrt(variance * t.Norm2);
        }

        /// <summary>The worst cell of the grid: one cell that differs fails the whole place.</summary>
        private static double CellScore(Gray big, int x, int y, Template t)
        {
            const int cs = Cell, n = Cell * Cell;
            double min = 1;
            for (int k = 0; k < t.CellZero.Length; k++)
            {
                int ox = x + (k % t.Columns) * Stride, oy = y + (k / t.Columns) * Stride;
                big.Stats(ox, oy, cs, cs, out double sum, out double sq);
                double variance = Math.Max(0, sq - sum * sum / n);
                double std = Math.Sqrt(variance / n);
                double score;
                if (t.CellStd[k] < FlatStd) score = std < 2 * FlatStd ? 1 : 0; // plain in the image too, or not
                else if (std < FlatStd) score = 0;
                else score = Correlate(big, ox, oy, cs, cs, t.CellZero[k]) / Math.Sqrt(variance * t.CellNorm2[k]);
                if (score < min) min = score;
            }
            return min;
        }

        private static double Correlate(Gray big, int x, int y, int w, int h, float[] zero)
        {
            float[] p = big.P;
            double num = 0;
            for (int j = 0; j < h; j++)
            {
                int row = (y + j) * big.W + x, trow = j * w;
                float acc = 0;
                for (int i = 0; i < w; i++) acc += p[row + i] * zero[trow + i];
                num += acc;
            }
            return num;
        }

        private static bool ColorClose(Gray big, Point at, Template t)
        {
            long r = 0, g = 0, b = 0;
            for (int j = 0; j < t.H; j++)
                for (int i = 0; i < t.W; i++)
                {
                    int c = big.Argb[(at.Y + j) * big.W + at.X + i];
                    r += (c >> 16) & 0xFF;
                    g += (c >> 8) & 0xFF;
                    b += c & 0xFF;
                }
            int n = t.W * t.H;
            return Math.Abs(r / n - t.R) <= ColorTolerance && Math.Abs(g / n - t.G) <= ColorTolerance && Math.Abs(b / n - t.B) <= ColorTolerance;
        }

        private static Template Prepare(Gray g, bool withCells)
        {
            int n = g.W * g.H;
            var t = new Template { W = g.W, H = g.H };
            t.Zero = ZeroMean(g, 0, 0, g.W, g.H, out t.Norm2);
            t.Detail = n == 0 ? 0 : Math.Sqrt(t.Norm2 / n);
            if (g.Argb != null && n > 0)
            {
                long r = 0, gr = 0, b = 0;
                foreach (int c in g.Argb)
                {
                    r += (c >> 16) & 0xFF;
                    gr += (c >> 8) & 0xFF;
                    b += c & 0xFF;
                }
                t.R = (int)(r / n);
                t.G = (int)(gr / n);
                t.B = (int)(b / n);
            }
            if (!withCells) return t;
            t.Columns = (g.W - Cell) / Stride + 1;
            t.Rows = (g.H - Cell) / Stride + 1;
            int cells = t.Columns * t.Rows;
            t.CellZero = new float[cells][];
            t.CellNorm2 = new double[cells];
            t.CellStd = new double[cells];
            for (int k = 0; k < cells; k++)
            {
                t.CellZero[k] = ZeroMean(g, (k % t.Columns) * Stride, (k / t.Columns) * Stride, Cell, Cell, out t.CellNorm2[k]);
                t.CellStd[k] = Math.Sqrt(t.CellNorm2[k] / (Cell * Cell));
            }
            return t;
        }

        /// <summary>A region's gray levels minus their mean, and the sum of their squares.</summary>
        private static float[] ZeroMean(Gray g, int x, int y, int w, int h, out double norm2)
        {
            var zero = new float[w * h];
            norm2 = 0;
            if (zero.Length == 0) return zero;
            double mean = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++) mean += g.P[(y + j) * g.W + x + i];
            mean /= zero.Length;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float v = (float)(g.P[(y + j) * g.W + x + i] - mean);
                    zero[j * w + i] = v;
                    norm2 += v * v;
                }
            return zero;
        }

        private static Gray ToGray(Bitmap image)
        {
            int w = image.Width, h = image.Height;
            var argb = new int[w * h];
            BitmapData data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * w, w);
            }
            finally { image.UnlockBits(data); }
            var p = new float[w * h];
            for (int k = 0; k < p.Length; k++)
            {
                int c = argb[k];
                p[k] = 0.299f * ((c >> 16) & 0xFF) + 0.587f * ((c >> 8) & 0xFF) + 0.114f * (c & 0xFF);
            }
            return new Gray { W = w, H = h, P = p, Argb = argb };
        }

        private static Gray Down(Gray g, int factor)
        {
            int w = g.W / factor, h = g.H / factor;
            var p = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int j = 0; j < factor; j++)
                        for (int i = 0; i < factor; i++) s += g.P[(y * factor + j) * g.W + x * factor + i];
                    p[y * w + x] = s / (factor * factor);
                }
            return Integrate(new Gray { W = w, H = h, P = p });
        }

        private static Gray Integrate(Gray g)
        {
            int stride = g.W + 1;
            g.Sum = new double[stride * (g.H + 1)];
            g.Sq = new double[stride * (g.H + 1)];
            for (int y = 0; y < g.H; y++)
            {
                double row = 0, rowSq = 0;
                for (int x = 0; x < g.W; x++)
                {
                    double v = g.P[y * g.W + x];
                    row += v;
                    rowSq += v * v;
                    g.Sum[(y + 1) * stride + x + 1] = g.Sum[y * stride + x + 1] + row;
                    g.Sq[(y + 1) * stride + x + 1] = g.Sq[y * stride + x + 1] + rowSq;
                }
            }
            return g;
        }
    }
}
