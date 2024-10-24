//
// ChromakeyRemover (C#)
// dkxce.ChromakeyRemover
// v 0.3, 24.10.2024
// https://github.com/dkxce
// en,ru,1251,utf-8
//

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace dkxce
{
    public class ChromakeyRemover
    {
        public enum Channel { Red, Green, Blue }

        public static (int, int, byte[]) RemoveChromaKey2Bytes(Bitmap image, 
            int velocity = 8, int threshold = 96,
            Channel? channel = Channel.Green, Color? transparentColor = null, 
            int? min_threshold = null)
        {
            float delta_threshold = min_threshold.HasValue ? 1.0f / ((float)threshold - (float)min_threshold) : 0.0f;

            int width = image.Width; 
            int height = image.Height;
            byte[] rgba;
            
            // Get Bitmap Array
            {
                BitmapData iData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(iData.Stride) * image.Height;
                rgba = new byte[bytes];
                Marshal.Copy(iData.Scan0, rgba, 0, bytes);
                image.UnlockBits(iData);
            };

            if (transparentColor.HasValue)
            {
                var pixels = Enumerable.Range(0, rgba.Length / 4).Select(x => new
                {
                    B = rgba[x * 4],
                    G = rgba[(x * 4) + 1],
                    R = rgba[(x * 4) + 2],
                    A = rgba[(x * 4) + 3],
                    MakeTransparent = new Action<float>((alpha) =>
                    {
                        if (alpha >= 1)
                        {
                            rgba[(x * 4)] = transparentColor.Value.B;
                            rgba[(x * 4) + 1] = transparentColor.Value.G;
                            rgba[(x * 4) + 2] = transparentColor.Value.R;
                            rgba[(x * 4) + 3] = transparentColor.Value.A;
                        }
                        else
                        {
                            rgba[(x * 4)] = (byte)(transparentColor.Value.B * alpha + rgba[x * 4] * (1 - alpha));
                            rgba[(x * 4) + 1] = (byte)(transparentColor.Value.G * alpha + rgba[(x * 4) + 1] * (1 - alpha));
                            rgba[(x * 4) + 2] = (byte)(transparentColor.Value.R * alpha + rgba[(x * 4) + 2] * (1 - alpha));
                            rgba[(x * 4) + 3] = (byte)(transparentColor.Value.A * alpha + rgba[(x * 4) + 3] * (1 - alpha));
                        };
                    })
                });
                pixels.AsParallel().ForAll(pixel =>
                {
                    byte max = Math.Max(Math.Max(pixel.R, pixel.G), pixel.B);
                    byte min = Math.Min(Math.Min(pixel.R, pixel.G), pixel.B);
                    byte pColor = pixel.G;
                    if (channel.HasValue)
                    {
                        if (channel == Channel.Red) pColor = pixel.R;
                        if (channel == Channel.Blue) pColor = pixel.B;
                    };
                    if (pColor != min && (pColor == max || max - pColor < velocity))
                    {
                        byte mm = (byte)(max - min);
                        if (mm > threshold) pixel.MakeTransparent(1.0f);
                        else if (min_threshold.HasValue && mm >= min_threshold) pixel.MakeTransparent((mm - min_threshold.Value) * delta_threshold);
                    };
                });
            }
            else
            {
                var pixels = Enumerable.Range(0, rgba.Length / 4).Select(x => new
                {
                    B = rgba[x * 4],
                    G = rgba[(x * 4) + 1],
                    R = rgba[(x * 4) + 2],
                    A = rgba[(x * 4) + 3],
                    //MakeTransparent = new Action(() => rgba[(x * 4) + 3] = 0),
                    MakeTransparent = new Action<float>((a) => rgba[(x * 4) + 3] = (byte)((float)rgba[(x * 4) + 3] * a)) // or a/2 .. a/5
                });
                pixels.AsParallel().ForAll(pixel =>
                {
                    byte max = Math.Max(Math.Max(pixel.R, pixel.B), pixel.G);
                    byte min = Math.Min(Math.Min(pixel.R, pixel.B), pixel.G);
                    byte pColor = pixel.G;
                    if (channel.HasValue)
                    {
                        if (channel == Channel.Red) pColor = pixel.R;
                        if (channel == Channel.Blue) pColor = pixel.B;
                    };
                    if (pColor != min && (pColor == max || max - pColor < velocity))
                    {
                        byte mm = (byte)(max - min);
                        if (mm > threshold) pixel.MakeTransparent(0.0f);
                        else if (min_threshold.HasValue && mm >= min_threshold) pixel.MakeTransparent(1.0f - ((mm - min_threshold.Value) * delta_threshold));
                    };
                });
            };

            return (width, height, rgba);
        }

        public static Bitmap RemoveChromaKey2Bitmap(Bitmap image, 
            int velocity = 8, int threshold = 96,
            Channel? channel = Channel.Green, Color? transparentColor = null,
            int? min_threshold = null)
        {
            int width, height;
            byte[] rgba;

            (width, height, rgba) = RemoveChromaKey2Bytes(image, velocity, threshold, channel, transparentColor, min_threshold);

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData oData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(rgba, 0, oData.Scan0, rgba.Length);
            output.UnlockBits(oData);
            return output;
        }
    }

    // RGB Direct Proximity
    public class RGBChromakeyRemover
    {
        public enum Channel { Red, Green, Blue }

        private Channel base_color = Channel.Green;
        private Color? transparent_color = null;
        private bool full_mask = false;
        private int[] mask_color = new int[] { 150, 44, 21 };
        private byte min_treshold = 8;
        private byte max_treshold = 96;
        private byte? avg_treshold = null;
        private float delta_threshold = 0f;

        public Channel MaskColor { set { base_color = value; } get { return base_color; } }
        public Color? TransparentColor { set { transparent_color = value; } get { return transparent_color; } }
        public bool FullMask { set { full_mask = value; } get { return full_mask; } }
        public byte MinTreshold { set { min_treshold = value; } get { return min_treshold; } }
        public byte MaxTreshold { set { max_treshold = value; SetDelta(); } get { return max_treshold; } }
        public byte? AvgTreshold { set { avg_treshold = value; SetDelta(); } get { return avg_treshold; } }

        private void SetDelta() => delta_threshold = avg_treshold.HasValue ? 1.0f / ((float)max_treshold - (float)avg_treshold) : 0.0f;


        public (int, int, byte[]) RemoveChromaKey2Bytes(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgba;

            // Get Bitmap Array
            {
                BitmapData iData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(iData.Stride) * image.Height;
                rgba = new byte[bytes];
                Marshal.Copy(iData.Scan0, rgba, 0, bytes);
                image.UnlockBits(iData);
            };

            if (transparent_color.HasValue)
            {
                var pixels = Enumerable.Range(0, rgba.Length / 4).Select(x => new
                {
                    B = rgba[x * 4],
                    G = rgba[(x * 4) + 1],
                    R = rgba[(x * 4) + 2],
                    A = rgba[(x * 4) + 3],
                    MakeTransparent = new Action<float>((alpha) =>
                    {
                        if (alpha >= 1)
                        {
                            rgba[(x * 4)]     = transparent_color.Value.B;
                            rgba[(x * 4) + 1] = transparent_color.Value.G;
                            rgba[(x * 4) + 2] = transparent_color.Value.R;
                            rgba[(x * 4) + 3] = transparent_color.Value.A;
                        }
                        else
                        { 
                            rgba[(x * 4)]     = (byte)(transparent_color.Value.B * alpha + rgba[x * 4] * (1 - alpha));
                            rgba[(x * 4) + 1] = (byte)(transparent_color.Value.G * alpha + rgba[(x * 4) + 1] * (1 - alpha));
                            rgba[(x * 4) + 2] = (byte)(transparent_color.Value.R * alpha + rgba[(x * 4) + 2] * (1 - alpha));
                            rgba[(x * 4) + 3] = (byte)(transparent_color.Value.A * alpha + rgba[(x * 4) + 3] * (1 - alpha));
                        };
                    })
                });
                pixels.AsParallel().ForAll(pixel =>
                {
                    byte max = Math.Max(Math.Max(pixel.R, pixel.G), pixel.B);
                    byte min = Math.Min(Math.Min(pixel.R, pixel.G), pixel.B);
                    byte pColor = pixel.G;
                    if (pColor != min && (pColor == max || max - pColor < min_treshold))
                    {
                        byte mm = (byte)(max - min);
                        if (mm > max_treshold) pixel.MakeTransparent(1.0f);
                        else if (avg_treshold.HasValue && mm >= avg_treshold) pixel.MakeTransparent((mm - avg_treshold.Value) * delta_threshold);
                    };
                });
            }
            else
            {
                var pixels = Enumerable.Range(0, rgba.Length / 4).Select(x => new
                {
                    B = rgba[x * 4],
                    G = rgba[(x * 4) + 1],
                    R = rgba[(x * 4) + 2],
                    A = rgba[(x * 4) + 3],
                    MakeTransparent = new Action<float>((a) => rgba[(x * 4) + 3] = (byte)((float)rgba[(x * 4) + 3] * a))
                });
                pixels.AsParallel().ForAll(pixel =>
                {
                    byte max = Math.Max(Math.Max(pixel.R, pixel.B), pixel.G);
                    byte min = Math.Min(Math.Min(pixel.R, pixel.B), pixel.G);
                    byte pColor = pixel.G;
                    if (pColor != min && (pColor == max || max - pColor < min_treshold))
                    {
                        byte mm = (byte)(max - min);
                        if (mm > max_treshold) pixel.MakeTransparent(0.0f);
                        else if (avg_treshold.HasValue && mm >= avg_treshold) pixel.MakeTransparent(1.0f - ((mm - avg_treshold.Value) * delta_threshold));
                    };
                });
            };

            return (width, height, rgba);
        }

        public Bitmap RemoveChromaKey2Bitmap(Bitmap image)
        {
            int width, height;
            byte[] rgba;

            (width, height, rgba) = RemoveChromaKey2Bytes(image);

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData oData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(rgba, 0, oData.Scan0, rgba.Length);
            output.UnlockBits(oData);
            return output;
        }

        public static void Test()
        {
            string current_path = Directory.GetCurrentDirectory();
            RGBChromakeyRemover converter = new RGBChromakeyRemover()
            {
                FullMask = true,
                MaskColor = Channel.Green,
                TransparentColor = Color.Fuchsia,
                MinTreshold = 6,
                MaxTreshold = 48,
                AvgTreshold = 32
            };
            string suffix = $"_(rgb_{converter.MinTreshold}_{converter.MaxTreshold})";
            Bitmap source_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "greenbox.jpg"));

            using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                destination_image.Save(Path.Combine(current_path, $"greenbox_color_replace{suffix}.jpg"));


            converter.FullMask = false;
            converter.TransparentColor = Color.Transparent;
            using (Bitmap background_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "background.jpg")))
            using (Bitmap overlayed_image = new Bitmap(background_image.Width, background_image.Height))
            using (Graphics g = Graphics.FromImage(overlayed_image))
            {
                g.DrawImage(background_image, new Rectangle(0, 0, background_image.Width, background_image.Height));

                using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                    g.DrawImage(destination_image, 0, 200, new Rectangle(0, 200, background_image.Width, background_image.Height), GraphicsUnit.Pixel);

                overlayed_image.Save(Path.Combine(current_path, $"greenbox_with_background{suffix}.jpg"));
            };

            source_image.Dispose();
        }
    }

    // Good and Fast. http://gc-films.com/chromakey.html
    public class YCbCrChromakeyRemover
    {
        private Color base_color = Color.Green;
        private Color? transparent_color = null;
        private bool full_mask = false;
        private int[] mask_color = new int[] { 150, 44, 21 };  
        private byte min_treshold = 8;
        private byte max_treshold = 96;

        public Color MaskColor
        {
            set
            {
                base_color = value;
                mask_color = new int[] { rgb2y(value.R, value.G, value.B), rgb2cb(value.R, value.G, value.B), rgb2cr(value.R, value.G, value.B) };
            }
            get
            {
                return base_color;
            }
        }
        public Color? TransparentColor { set { transparent_color = value; } get { return transparent_color; } }
        public bool FullMask { set { full_mask = value; } get { return full_mask; } }
        public byte MinTreshold { set { min_treshold = value; } get { return min_treshold; } }
        public byte MaxTreshold { set { max_treshold = value; } get { return max_treshold; } }


        private static int rgb2y(int r, int g, int b) => (int)Math.Round(0.299 * (double)r + 0.587 * (double)g + 0.114 * (double)b);

        private static int rgb2cb(int r, int g, int b) => (int)Math.Round(128.0 + -0.168736 * (double)r - 0.331264 * (double)g + 0.5 * (double)b);

        private static int rgb2cr(int r, int g, int b) => (int)Math.Round(128.0 + 0.5 * (double)r - 0.418688 * (double)g - 0.081312 * (double)b);


        private double colorclose(int R, int G, int B)
        {
            double d = Math.Sqrt(Math.Pow(mask_color[1] - rgb2cb(R, G, B), 2) + Math.Pow(mask_color[2] - rgb2cr(R, G, B), 2));
            return 1 - (d < min_treshold ? 0 : d > max_treshold ? 1 : (d - min_treshold) / (max_treshold - min_treshold));
        }
        

        public (int, int, byte[]) RemoveChromaKey2Bytes(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgba_foreground;

            // Get Bitmap Array
            {
                BitmapData iData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(iData.Stride) * image.Height;
                rgba_foreground = new byte[bytes];
                Marshal.Copy(iData.Scan0, rgba_foreground, 0, bytes);
                image.UnlockBits(iData);
            };

            if (transparent_color.HasValue)
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)]     = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)]     - mask * base_color.B, 0) + mask * transparent_color.Value.B), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * transparent_color.Value.G), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * transparent_color.Value.R), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * base_color.A, 0) + mask * transparent_color.Value.A), 255);     //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            }
            else
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)]     = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)]     - mask * base_color.B, 0) + mask * 0), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * 0), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * 0), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * 255, 0) + mask * 0), 255);               //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            };

            return (width, height, rgba_foreground);
        }

        public Bitmap RemoveChromaKey2Bitmap(Bitmap image)
        {
            int width, height;
            byte[] rgba;

            (width, height, rgba) = RemoveChromaKey2Bytes(image);

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData oData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(rgba, 0, oData.Scan0, rgba.Length);
            output.UnlockBits(oData);
            return output;
        }

        public static void Test()
        {
            string current_path = Directory.GetCurrentDirectory();
            YCbCrChromakeyRemover converter = new YCbCrChromakeyRemover()
            {
                FullMask = true,
                MaskColor = Color.FromArgb(21, 238, 211),
                TransparentColor = Color.Fuchsia,
                MinTreshold = 74,
                MaxTreshold = 92
            };
            string suffix = $"_(ycbcr_{converter.MinTreshold}_{converter.MaxTreshold})";
            Bitmap source_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "greenbox.jpg"));

            using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                destination_image.Save(Path.Combine(current_path, $"greenbox_color_replace{suffix}.jpg"));


            converter.FullMask = false;
            converter.TransparentColor = Color.Transparent;
            using (Bitmap background_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "background.jpg")))
            using (Bitmap overlayed_image = new Bitmap(background_image.Width, background_image.Height))
            using (Graphics g = Graphics.FromImage(overlayed_image))
            {
                g.DrawImage(background_image, new Rectangle(0, 0, background_image.Width, background_image.Height));

                using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                    g.DrawImage(destination_image, 0, 200, new Rectangle(0, 200, background_image.Width, background_image.Height), GraphicsUnit.Pixel);

                overlayed_image.Save(Path.Combine(current_path, $"greenbox_with_background{suffix}.jpg"));
            };

            source_image.Dispose();
        }
    }

    // 3D Vector
    public class RGB3DChromakeyRemover
    {
        private const double max_color_distance = 441.0f;
        private Color base_color = Color.Green;
        private Color? transparent_color = null;
        private bool full_mask = false;
        private float min_treshold = 0.31f;
        private float max_treshold = 0.37f;
        private double max_distance = 0.37f * max_color_distance;

        public Color MaskColor
        {
            set
            {
                base_color = value;
            }
            get
            {
                return base_color;
            }
        }
        public Color? TransparentColor { set { transparent_color = value; } get { return transparent_color; } }
        public bool FullMask { set { full_mask = value; } get { return full_mask; } }
        public byte MinTreshold { set { min_treshold = value / 255f; } get { return (byte)(min_treshold * 255f); } }
        public byte MaxTreshold { set { max_treshold = value / 255f; max_distance = max_treshold * max_color_distance; } get { return (byte)(max_treshold * 255f); } }

        private double colorclose(int R, int G, int B)
        {
            
            double delta = Math.Sqrt(Math.Pow(R - base_color.R, 2) + Math.Pow(G - base_color.G, 2) + Math.Pow(B - base_color.B, 2));
            // ++ STANDARD LOGIC ++ //
            delta = delta / max_distance;
            if (delta < min_treshold / max_treshold) return 1;
            if (delta > 1) return 0;
            return 1 - delta;
            // -- STANDARD LOGIC -- //
        }

        public (int, int, byte[]) RemoveChromaKey2Bytes(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgba_foreground;

            // Get Bitmap Array
            {
                BitmapData iData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(iData.Stride) * image.Height;
                rgba_foreground = new byte[bytes];
                Marshal.Copy(iData.Scan0, rgba_foreground, 0, bytes);
                image.UnlockBits(iData);
            };

            if (transparent_color.HasValue)
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)]     = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * transparent_color.Value.B), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * transparent_color.Value.G), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * transparent_color.Value.R), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * base_color.A, 0) + mask * transparent_color.Value.A), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            }
            else
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)]     = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * 0), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * 0), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * 0), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * 255, 0) + mask * 0), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            };

            return (width, height, rgba_foreground);
        }

        public Bitmap RemoveChromaKey2Bitmap(Bitmap image)
        {
            int width, height;
            byte[] rgba;

            (width, height, rgba) = RemoveChromaKey2Bytes(image);

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData oData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(rgba, 0, oData.Scan0, rgba.Length);
            output.UnlockBits(oData);
            return output;
        }

        public static void Test()
        {
            string current_path = Directory.GetCurrentDirectory();
            RGB3DChromakeyRemover converter = new RGB3DChromakeyRemover()
            {
                FullMask = true,
                MaskColor = Color.FromArgb(21, 238, 211),
                TransparentColor = Color.Fuchsia,
                MinTreshold = 86,
                MaxTreshold = 92
            };
            string suffix = $"_(rgb3d_{converter.MinTreshold}_{converter.MaxTreshold})";
            Bitmap source_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "greenbox.jpg"));

            using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                destination_image.Save(Path.Combine(current_path, $"greenbox_color_replace{suffix}.jpg"));


            converter.FullMask = false;
            converter.TransparentColor = Color.Transparent;
            using (Bitmap background_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "background.jpg")))
            using (Bitmap overlayed_image = new Bitmap(background_image.Width, background_image.Height))
            using (Graphics g = Graphics.FromImage(overlayed_image))
            {
                g.DrawImage(background_image, new Rectangle(0, 0, background_image.Width, background_image.Height));

                using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                    g.DrawImage(destination_image, 0, 200, new Rectangle(0, 200, background_image.Width, background_image.Height), GraphicsUnit.Pixel);

                overlayed_image.Save(Path.Combine(current_path, $"greenbox_with_background{suffix}.jpg"));
            };

            source_image.Dispose();
        }
    }

    // Grayscale proximity
    public class GrayScaleChromakeyRemover
    {
        private double gray_color = 150.45f;
        private Color base_color = Color.Green;
        private Color? transparent_color = null;
        private bool full_mask = false;
        private double min_treshold = 3f;
        private double max_treshold = 68f;

        public Color MaskColor { set { base_color = value; gray_color = RGB2toGray(value); } get { return base_color; } }
        public Color? TransparentColor { set { transparent_color = value; } get { return transparent_color; } }
        public bool FullMask { set { full_mask = value; } get { return full_mask; } }
        public byte MinTreshold { set { min_treshold = (value / 2.5f); } get { return (byte)(min_treshold * 2.5f); } }
        public byte MaxTreshold { set { max_treshold = (value / 2.5f); } get { return (byte)(max_treshold * 2.5f); } }

        private static double RGB2toGray(System.Drawing.Color c) => .11 * c.B + .59 * c.G + .30 * c.R;
        private static double RGB2toGray(int B, int G, int R) => .11 * B + .59 * G + .30 * R;

        private double colorclose(double gray1)
        {
            // ++ STANDARD LOGIC ++ //
            double delta = Math.Abs(gray1 - gray_color) / max_treshold;
            if (delta < min_treshold / max_treshold) return 1;
            if (delta > 1) return 0;
            return 1 - delta;
            // -- STANDARD LOGIC -- //
        }

        public (int, int, byte[]) RemoveChromaKey2Bytes(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgba_foreground;

            // Get Bitmap Array
            {
                BitmapData iData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(iData.Stride) * image.Height;
                rgba_foreground = new byte[bytes];
                Marshal.Copy(iData.Scan0, rgba_foreground, 0, bytes);
                image.UnlockBits(iData);
            };

            if (transparent_color.HasValue)
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * transparent_color.Value.B), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * transparent_color.Value.G), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * transparent_color.Value.R), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * base_color.A, 0) + mask * transparent_color.Value.A), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(RGB2toGray(pixel.R, pixel.G, pixel.B))));
            }
            else
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * 0), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * 0), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * 0), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * 255, 0) + mask * 0), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(RGB2toGray(pixel.R, pixel.G, pixel.B))));
            };

            return (width, height, rgba_foreground);
        }

        public Bitmap RemoveChromaKey2Bitmap(Bitmap image)
        {
            int width, height;
            byte[] rgba;

            (width, height, rgba) = RemoveChromaKey2Bytes(image);

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData oData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(rgba, 0, oData.Scan0, rgba.Length);
            output.UnlockBits(oData);
            return output;
        }

        public static void Test()
        {
            string current_path = Directory.GetCurrentDirectory();
            GrayScaleChromakeyRemover converter = new GrayScaleChromakeyRemover()
            {
                FullMask = true,
                MaskColor = Color.FromArgb(21, 238, 211),
                TransparentColor = Color.Fuchsia,
                MinTreshold = 86,
                MaxTreshold = 96
            };
            string suffix = $"_(grayscale_{converter.MinTreshold}_{converter.MaxTreshold})";
            Bitmap source_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "greenbox.jpg"));

            using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                destination_image.Save(Path.Combine(current_path, $"greenbox_color_replace{suffix}.jpg"));


            converter.FullMask = false;
            converter.TransparentColor = Color.Transparent;
            using (Bitmap background_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "background.jpg")))
            using (Bitmap overlayed_image = new Bitmap(background_image.Width, background_image.Height))
            using (Graphics g = Graphics.FromImage(overlayed_image))
            {
                g.DrawImage(background_image, new Rectangle(0, 0, background_image.Width, background_image.Height));

                using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                    g.DrawImage(destination_image, 0, 200, new Rectangle(0, 200, background_image.Width, background_image.Height), GraphicsUnit.Pixel);

                overlayed_image.Save(Path.Combine(current_path, $"greenbox_with_background{suffix}.jpg"));
            };

            source_image.Dispose();
        }
    }

    // https://www.compuphase.com/cmetric.htm
    public class ColorMetricChromakeyRemover
    {
        private const double max_color_distance = 767f;
        private Color base_color = Color.Green;
        private Color? transparent_color = null;
        private bool full_mask = false;
        private float min_treshold = 0.05f;
        private float max_treshold = 0.1f;
        private double max_distance = 0.1f * max_color_distance;

        public Color MaskColor { set { base_color = value; } get { return base_color; } }
        public Color? TransparentColor { set { transparent_color = value; } get { return transparent_color; } }
        public bool FullMask { set { full_mask = value; } get { return full_mask; } }
        public byte MinTreshold { set { min_treshold = value / 255f; } get { return (byte)(min_treshold * 255f); } }
        public byte MaxTreshold { set { max_treshold = value / 255f; max_distance = max_treshold * max_color_distance; } get { return (byte)(max_treshold * 255f); } }

        private double colorclose(int R, int G, int B)
        {            
            long rmean = ((long)R + (long)base_color.R) / 2;
            long r = (long)R - (long)base_color.R;
            long g = (long)G - (long)base_color.G;
            long b = (long)B - (long)base_color.B;

            double dist = Math.Sqrt((((512 + rmean) * r * r) >> 8) + 4 * g * g + (((767 - rmean) * b * b) >> 8));
            
            // ++ STANDARD LOGIC ++ //
            double delta = dist / max_distance;
            if (delta < min_treshold / max_treshold) return 1;
            if (delta > 1) return 0;
            return 1 - delta;
            // -- STANDARD LOGIC -- //
        }

        public (int, int, byte[]) RemoveChromaKey2Bytes(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgba_foreground;

            // Get Bitmap Array
            {
                BitmapData iData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(iData.Stride) * image.Height;
                rgba_foreground = new byte[bytes];
                Marshal.Copy(iData.Scan0, rgba_foreground, 0, bytes);
                image.UnlockBits(iData);
            };

            if (transparent_color.HasValue)
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * transparent_color.Value.B), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * transparent_color.Value.G), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * transparent_color.Value.R), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * base_color.A, 0) + mask * transparent_color.Value.A), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            }
            else
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * 0), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * 0), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * 0), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * 255, 0) + mask * 0), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            };

            return (width, height, rgba_foreground);
        }

        public Bitmap RemoveChromaKey2Bitmap(Bitmap image)
        {
            int width, height;
            byte[] rgba;

            (width, height, rgba) = RemoveChromaKey2Bytes(image);

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData oData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(rgba, 0, oData.Scan0, rgba.Length);
            output.UnlockBits(oData);
            return output;
        }
        public static void Test()
        {
            string current_path = Directory.GetCurrentDirectory();
            ColorMetricChromakeyRemover converter = new ColorMetricChromakeyRemover()
            {
                FullMask = true,
                MaskColor = Color.FromArgb(21, 238, 211),
                TransparentColor = Color.Fuchsia,
                MinTreshold = 90,
                MaxTreshold = 108
            };
            string suffix = $"_(colormetric_{converter.MinTreshold}_{converter.MaxTreshold})";
            Bitmap source_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "greenbox.jpg"));

            using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                destination_image.Save(Path.Combine(current_path, $"greenbox_color_replace{suffix}.jpg"));


            converter.FullMask = false;
            converter.TransparentColor = Color.Transparent;
            using (Bitmap background_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "background.jpg")))
            using (Bitmap overlayed_image = new Bitmap(background_image.Width, background_image.Height))
            using (Graphics g = Graphics.FromImage(overlayed_image))
            {
                g.DrawImage(background_image, new Rectangle(0, 0, background_image.Width, background_image.Height));

                using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                    g.DrawImage(destination_image, 0, 200, new Rectangle(0, 200, background_image.Width, background_image.Height), GraphicsUnit.Pixel);

                overlayed_image.Save(Path.Combine(current_path, $"greenbox_with_background{suffix}.jpg"));
            };

            source_image.Dispose();
        }
    }

    public class HSVChromakeyRemover
    {
        public enum ProcessMode: byte {Semifull, Full}

        private const double max_color_distance = 180f;
        private float[] hsv_color = rgbToHSV(Color.Green);
        private Color base_color = Color.Green;
        private Color? transparent_color = null;
        private bool full_mask = false;
        private float min_treshold = 0.05f;
        private float max_treshold = 0.1f;
        private HSVChromakeyRemover.ProcessMode mode = HSVChromakeyRemover.ProcessMode.Full;

        public Color MaskColor { set { base_color = value; } get { return base_color; } }
        public Color? TransparentColor { set { transparent_color = value; } get { return transparent_color; } }
        public bool FullMask { set { full_mask = value; } get { return full_mask; } }
        public byte MinTreshold { set { min_treshold = value / 255f; } get { return (byte)(min_treshold * 255f); } }
        public byte MaxTreshold { set { max_treshold = value / 255f; } get { return (byte)(max_treshold * 255f); } }
        public HSVChromakeyRemover.ProcessMode Mode { set { mode = value; } get { return mode; } }

        private static float GetHue(int R, int G, int B)
        {
            if (R == G && G == B) return 0f;

            float num = (float)(int)R / 255f;
            float num2 = (float)(int)G / 255f;
            float num3 = (float)(int)B / 255f;
            float num4 = 0f;
            float num5 = num;
            float num6 = num;
            if (num2 > num5) num5 = num2;
            if (num3 > num5) num5 = num3;
            if (num2 < num6) num6 = num2;
            if (num3 < num6) num6 = num3;
            float num7 = num5 - num6;
            if (num == num5) num4 = (num2 - num3) / num7;
            else if (num2 == num5) num4 = 2f + (num3 - num) / num7;
            else if (num3 == num5) num4 = 4f + (num - num2) / num7;
            num4 *= 60f;
            if (num4 < 0f) num4 += 360f;

            return num4;
        }

        public static float[] rgbToHSV(Color color)
        {
            double[] output = new double[3];
            double hue, saturation, value;
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));

            hue = GetHue(color.R, color.G, color.B);
            saturation = (max == 0) ? 0 : 1d - (1d * min / max);
            value = max / 255d;

            float[] res = new float[] { (float)hue, (float)saturation, (float)value, 0f};
            return res;
        }

        public static float[] rgbToHSV(int R, int G, int B)
        {
            double[] output = new double[3];
            double hue, saturation, value;
            int max = Math.Max(R, Math.Max(G, B));
            int min = Math.Min(R, Math.Min(G, B));

            hue = GetHue(R, G, B);
            saturation = (max == 0) ? 0 : 1d - (1d * min / max);
            value = max / 255d;

            float[] res = new float[] { (float)hue, (float)saturation, (float)value, 0f };
            return res;
        }

        private double colorclose(int R, int G, int B)
        {            
            float[] c1 = rgbToHSV(R, G, B);
            double delta = Math.Min(Math.Abs(c1[0] - hsv_color[0]), 360 - Math.Abs(c1[0] - hsv_color[0]));

            if (mode == HSVChromakeyRemover.ProcessMode.Full)
            {
                delta = delta / max_color_distance;
                double ds = Math.Abs(c1[1] - hsv_color[1]);
                double dv = Math.Abs(c1[2] - hsv_color[2]) / 255.0f;
                delta = Math.Sqrt(delta * delta + ds * ds + dv * dv);
                delta = delta / max_treshold;
            }
            else
            {
                delta = delta / max_color_distance;
                delta = delta / max_treshold;
            };

            // ++ STANDARD LOGIC ++ //
            if (delta < min_treshold / max_treshold) return 1;
            if (delta > 1) return 0;
            return 1 - delta;
            // -- STANDARD LOGIC -- //
        }

        public (int, int, byte[]) RemoveChromaKey2Bytes(Bitmap image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgba_foreground;

            // Get Bitmap Array
            {
                BitmapData iData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(iData.Stride) * image.Height;
                rgba_foreground = new byte[bytes];
                Marshal.Copy(iData.Scan0, rgba_foreground, 0, bytes);
                image.UnlockBits(iData);
            };

            if (transparent_color.HasValue)
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * transparent_color.Value.B), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * transparent_color.Value.G), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * transparent_color.Value.R), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * base_color.A, 0) + mask * transparent_color.Value.A), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            }
            else
            {
                var pixels = Enumerable.Range(0, rgba_foreground.Length / 4).Select(x => new
                {
                    B = rgba_foreground[(x * 4)],
                    G = rgba_foreground[(x * 4) + 1],
                    R = rgba_foreground[(x * 4) + 2],
                    MakeTransparent = new Action<double>((mask) =>
                    {
                        if (full_mask)
                        {
                            rgba_foreground[(x * 4)] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4)] - mask * base_color.B, 0) + mask * 0), 255); //B
                            rgba_foreground[(x * 4) + 1] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 1] - mask * base_color.G, 0) + mask * 0), 255); //G
                            rgba_foreground[(x * 4) + 2] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 2] - mask * base_color.R, 0) + mask * 0), 255); //R
                        };
                        rgba_foreground[(x * 4) + 3] = (byte)Math.Min((Math.Max(rgba_foreground[(x * 4) + 3] - mask * 255, 0) + mask * 0), 255); //A
                    })
                });
                pixels.AsParallel().ForAll(pixel => pixel.MakeTransparent(colorclose(pixel.R, pixel.G, pixel.B)));
            };

            return (width, height, rgba_foreground);
        }

        public Bitmap RemoveChromaKey2Bitmap(Bitmap image)
        {
            int width, height;
            byte[] rgba;

            (width, height, rgba) = RemoveChromaKey2Bytes(image);

            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData oData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(rgba, 0, oData.Scan0, rgba.Length);
            output.UnlockBits(oData);
            return output;
        }

        public static void Test()
        {
            string current_path = Directory.GetCurrentDirectory();
            HSVChromakeyRemover converter = new HSVChromakeyRemover()
            {
                FullMask = true,
                MaskColor = Color.FromArgb(21, 238, 211),
                TransparentColor = Color.Fuchsia,
                MinTreshold = 120,
                MaxTreshold = 128,
                Mode = ProcessMode.Full
            };
            string suffix = $"_(hsv{converter.Mode}_{converter.MinTreshold}_{converter.MaxTreshold})";
            Bitmap source_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "greenbox.jpg"));

            using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                destination_image.Save(Path.Combine(current_path, $"greenbox_color_replace{suffix}.jpg"));


            converter.FullMask = false;
            converter.TransparentColor = Color.Transparent;
            using (Bitmap background_image = (Bitmap)Bitmap.FromFile(Path.Combine(current_path, "background.jpg")))
            using (Bitmap overlayed_image = new Bitmap(background_image.Width, background_image.Height))
            using (Graphics g = Graphics.FromImage(overlayed_image))
            {
                g.DrawImage(background_image, new Rectangle(0, 0, background_image.Width, background_image.Height));

                using (Bitmap destination_image = converter.RemoveChromaKey2Bitmap(source_image))
                    g.DrawImage(destination_image, 0, 200, new Rectangle(0, 200, background_image.Width, background_image.Height), GraphicsUnit.Pixel);

                overlayed_image.Save(Path.Combine(current_path, $"greenbox_with_background{suffix}.jpg"));
            };

            source_image.Dispose();
        }
    }
}
