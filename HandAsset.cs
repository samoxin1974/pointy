using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pointy
{
    /// <summary>
    /// A hand image plus the anchors measured from its alpha channel.
    /// Port of TinyHand's HandAsset: every value is normalised to the image (0 at the
    /// top/left edge, 1 at the bottom/right edge). Row 0 is the fingertip.
    /// </summary>
    public sealed class HandAsset
    {
        public string Name { get; private set; } = "";
        public double Aspect { get; private set; }
        public double HandTop { get; private set; }
        public double Tip { get; private set; }
        public double Wrist { get; private set; }
        public double RodEnd { get; private set; }
        public double TipX { get; private set; }
        public double RodX { get; private set; }
        public double HandMinX { get; private set; }
        public double HandMaxX { get; private set; }

        /// <summary>Whole image, scaled once for drawing.</summary>
        public BitmapSource Image { get; private set; } = null!;

        /// <summary>Blurred black silhouette of <see cref="Image"/>, used for the drop shadow.</summary>
        public BitmapSource Shadow { get; private set; } = null!;

        /// <summary>Square crop around the hand, for the settings grid.</summary>
        public BitmapSource Thumbnail { get; private set; } = null!;

        private const int RenderHeight = 640;
        private const int ThumbSide = 128;
        private const int CacheLimit = 3;

        private static readonly Dictionary<string, HandAsset?> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Recent = new();

        public static string HandsDir => Path.Combine(Prefs.AssetsDir, "hands");

        public static HandAsset? Load(string name)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(name, out var cached))
                {
                    Touch(name);
                    return cached;
                }
            }

            HandAsset? asset = null;
            try
            {
                asset = Build(name);
            }
            catch
            {
                asset = null;
            }

            lock (Cache)
            {
                Cache[name] = asset;
                Touch(name);
            }

            return asset;
        }

        private static void Touch(string name)
        {
            Recent.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            Recent.Add(name);
            while (Recent.Count > CacheLimit)
            {
                Cache.Remove(Recent[0]);
                Recent.RemoveAt(0);
            }
        }

        private static HandAsset? Build(string name)
        {
            var path = Path.Combine(HandsDir, name + ".png");
            if (!File.Exists(path)) return null;

            var probe = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            int targetHeight = Math.Min(probe.PixelHeight, RenderHeight);

            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.UriSource = new Uri(path);
            if (targetHeight > 0) decoded.DecodePixelHeight = targetHeight;
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.EndInit();
            decoded.Freeze();

            BitmapSource src = decoded.Format == PixelFormats.Bgra32
                ? decoded
                : new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);

            int w = src.PixelWidth;
            int h = src.PixelHeight;
            var pixels = new byte[w * h * 4];
            src.CopyPixels(pixels, w * 4, 0);

            var asset = Measure(name, pixels, w, h);
            if (asset is null) return null;

            src.Freeze();
            asset.Image = src;
            asset.Shadow = MakeShadow(pixels, w, h);
            asset.Thumbnail = MakeThumbnail(src, asset);
            return asset;
        }

        private static HandAsset Measure(string name, byte[] bgra, int w, int h)
        {
            var counts = new int[h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                int n = 0;
                for (int x = 0; x < w; x++)
                {
                    if (bgra[row + x * 4 + 3] > 128) n++;
                }
                counts[y] = n;
            }

            double Center(double normalizedY)
            {
                int y = (int)Math.Round(normalizedY * (h - 1));
                y = Math.Clamp(y, 0, h - 1);
                int row = y * w * 4;
                int lo = -1, hi = -1;
                for (int x = 0; x < w; x++)
                {
                    if (bgra[row + x * 4 + 3] > 128)
                    {
                        if (lo < 0) lo = x;
                        hi = x;
                    }
                }
                return lo < 0 ? 0.5 : (lo + hi) / 2.0 / w;
            }

            int top = -1;
            for (int y = 0; y < h; y++)
            {
                if (counts[y] > 0) { top = y; break; }
            }
            if (top < 0) return null!;

            int searchEnd = Math.Min(h, top + (int)(0.45 * h));
            int maxW = 0;
            for (int y = top; y < searchEnd; y++) maxW = Math.Max(maxW, counts[y]);
            if (maxW <= 0) return null!;

            int fist = -1;
            for (int y = top; y < h; y++)
            {
                if (counts[y] > 0.6 * maxW) { fist = y; break; }
            }
            if (fist < 0) return null!;

            int wrist = -1;
            for (int y = fist; y < h; y++)
            {
                if (counts[y] < 0.35 * maxW) { wrist = y; break; }
            }
            if (wrist < 0) return null!;

            int rodW = counts[Math.Min(h - 1, wrist + h / 50)];

            int lastRow = h - 1;
            for (int y = h - 1; y >= 0; y--)
            {
                if (counts[y] > 0) { lastRow = y; break; }
            }

            int from = Math.Min(h - 1, wrist + h / 33);
            int rodEnd = lastRow;
            for (int y = from; y < h; y++)
            {
                if (counts[y] > 1.8 * rodW) { rodEnd = y; break; }
            }

            int minX = w, maxX = 0;
            for (int y = top; y < wrist; y++)
            {
                int row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    if (bgra[row + x * 4 + 3] > 128)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                    }
                }
            }
            if (maxX <= minX) { minX = 0; maxX = w - 1; }

            double fh = h;
            double tipRow = Math.Min(h - 1, top + h / 200) / fh;

            return new HandAsset
            {
                Name = name,
                Aspect = (double)w / fh,
                HandTop = Math.Max(0, (top - 0.004 * fh) / fh),
                Tip = top / fh,
                Wrist = wrist / fh,
                RodEnd = rodEnd / fh,
                TipX = Center(tipRow),
                RodX = Center((double)(wrist + rodEnd) / 2 / fh),
                HandMinX = (double)minX / w,
                HandMaxX = (double)maxX / w,
            };
        }

        private static BitmapSource MakeShadow(byte[] bgra, int w, int h)
        {
            int sw = Math.Max(1, w / 3);
            int sh = Math.Max(1, h / 3);
            var alpha = new float[sw * sh];

            for (int y = 0; y < sh; y++)
            {
                int sy = Math.Min(h - 1, y * 3);
                for (int x = 0; x < sw; x++)
                {
                    int sx = Math.Min(w - 1, x * 3);
                    alpha[y * sw + x] = bgra[(sy * w + sx) * 4 + 3] / 255f;
                }
            }

            BoxBlur(alpha, sw, sh, Math.Max(2, sw / 40), 3);

            var outPixels = new byte[sw * sh * 4];
            for (int i = 0; i < alpha.Length; i++)
            {
                // Premultiplied black: RGB stays 0, only alpha carries the silhouette.
                outPixels[i * 4 + 3] = (byte)Math.Clamp(alpha[i] * 255f * 0.85f, 0, 255);
            }

            var bmp = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, outPixels, sw * 4);
            bmp.Freeze();
            return bmp;
        }

        private static void BoxBlur(float[] buffer, int w, int h, int radius, int passes)
        {
            var temp = new float[buffer.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                BlurAxis(buffer, temp, w, h, radius, horizontal: true);
                BlurAxis(temp, buffer, w, h, radius, horizontal: false);
            }
        }

        private static void BlurAxis(float[] src, float[] dst, int w, int h, int radius, bool horizontal)
        {
            int outer = horizontal ? h : w;
            int inner = horizontal ? w : h;
            int span = radius * 2 + 1;

            for (int o = 0; o < outer; o++)
            {
                float sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    sum += Sample(src, w, h, o, i, inner, horizontal);
                }

                for (int i = 0; i < inner; i++)
                {
                    float value = sum / span;
                    if (horizontal) dst[o * w + i] = value;
                    else dst[i * w + o] = value;

                    sum -= Sample(src, w, h, o, i - radius, inner, horizontal);
                    sum += Sample(src, w, h, o, i + radius + 1, inner, horizontal);
                }
            }
        }

        private static float Sample(float[] src, int w, int h, int outer, int inner, int innerLen, bool horizontal)
        {
            int clamped = Math.Clamp(inner, 0, innerLen - 1);
            return horizontal ? src[outer * w + clamped] : src[clamped * w + outer];
        }

        private static BitmapSource MakeThumbnail(BitmapSource src, HandAsset asset)
        {
            int w = src.PixelWidth;
            int h = src.PixelHeight;

            double handW = (asset.HandMaxX - asset.HandMinX) * w;
            double handH = (asset.Wrist - asset.HandTop) * h;
            double side = Math.Max(handW, handH) * 1.06;
            double cx = (asset.HandMinX + asset.HandMaxX) / 2 * w;
            double cy = (asset.HandTop + asset.Wrist) / 2 * h;

            int x = (int)Math.Round(cx - side / 2);
            int y = (int)Math.Round(cy - side / 2);
            int size = (int)Math.Max(1, Math.Round(side));

            int x0 = Math.Clamp(x, 0, Math.Max(0, w - 1));
            int y0 = Math.Clamp(y, 0, Math.Max(0, h - 1));
            size = Math.Min(size, Math.Min(w - x0, h - y0));

            BitmapSource crop;
            try
            {
                crop = new CroppedBitmap(src, new Int32Rect(x0, y0, size, size));
            }
            catch
            {
                crop = src;
            }

            var thumb = new TransformedBitmap(crop,
                new ScaleTransform((double)ThumbSide / crop.PixelWidth, (double)ThumbSide / crop.PixelHeight));
            thumb.Freeze();
            return thumb;
        }

        public static void PreloadThumbnails(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                Load(name);
            }
        }
    }
}
