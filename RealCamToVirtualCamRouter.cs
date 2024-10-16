//
// RealCamToVirtualCamRouter (C#)
// dkxce.RealCamToVirtualCamRouter
// v 0.1, 15.10.2024
// https://github.com/dkxce
// en,ru,1251,utf-8
//

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// using OpenCVSharp
using OpenCvSharp; 

// using SixLabors.ImageSharp
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace dkxce
{
    public static class RealCamToVirtualCamRouter
    {
        private static Bitmap buffer_frame = null;
        private static Mutex buffer_mutex = new Mutex();

        // Using:
        //  https://github.com/webcamoid/akvirtualcamera
        // Need Camera Settings:
        //  AkVCamManager add-device "VirtualCamera0"
        //  AkVCamManager set-description VirtualCamera0 "dkxce.RealCamToVirtualCamRouter"
        //  AkVCamManager supported-formats --output
        //  AkVCamManager add-format VirtualCamera0 RGB24 720 480 25
        //  AkVCamManager add-format VirtualCamera0 RGB24 640 480 25        
        //  AkVCamManager set-picture "C:\Disk2\Images\il_1588xN.4792548473_b52j_640x480.png"
        //  AkVCamManager update
        //  AkVCamManager devices

        public static string akvcamman_path = @"C:\Program Files\AkVirtualCamera\x64\AkVCamManager.exe";
        public static string background_image = @"C:\Disk2\Images\il_1588xN.4792548473_b52j_640x480.png";

        public static bool chromakey_remove = true;
        public static int chromakey_velocity = 8;    // recommended value: 8
        public static int chromakey_treshold = 60;   // recommended value: 96
        public static ChromakeyRemover.Channel chromakey_channel = ChromakeyRemover.Channel.Green;
        
        public static int virtualcam_fps = 25;
        public static int virtualcam_num = 0;       
        public static string virtualcam_name = "VirtualCamera0";

        public static int default_width = 720;
        public static int default_height = 480;
        public static bool in_progress = false;

        // Entry Point
        public static void Route()
        {
            in_progress = true;
            using (var vid = new VideoCapture(virtualcam_num, VideoCaptureAPIs.DSHOW))
            {
                vid.Set(VideoCaptureProperties.FrameWidth, default_width);
                vid.Set(VideoCaptureProperties.FrameHeight, default_height);

                if (!vid.IsOpened())
                {
                    Console.WriteLine("Failed to open camera!");
                    return;
                };

                bool firstRun = true;
                while (in_progress)
                {
                    using (Mat frame = new Mat())
                    {
                        if (!vid.Read(frame))   break;

                        Bitmap img;
                        using (var ms = new MemoryStream(frame.ToBytes())) img = (Bitmap)Bitmap.FromStream(ms);

                        if (firstRun)
                        {
                            LaunchThread(img.Width, img.Height);
                            firstRun = false;
                        };

                        buffer_mutex.WaitOne();
                        if (buffer_frame != null) buffer_frame.Dispose();
                        buffer_frame = img;
                        buffer_mutex.ReleaseMutex();                                             
                    };
                };
            };
        }
        
        private static void LaunchThread(int width, int height)
        {
            Thread thr = new Thread(() => RouteThread((object)(new int[] { width, height })));
            thr.Start();
        }

        private static void RouteThread(object wihe)
        {
            int width = ((int[])wihe)[0];
            int height = ((int[])wihe)[1];
        
            // Init AKVCamManager
            ProcessStartInfo psi = new ProcessStartInfo(akvcamman_path, String.Format("stream --fps {0} {1} RGB24 {2} {3}", virtualcam_fps, virtualcam_name, width, height));
            psi.RedirectStandardInput = true;
            psi.UseShellExecute = false;
            Process akvcamproc = Process.Start(psi);

            // Init & Resize Background Image
            SixLabors.ImageSharp.Image background = SixLabors.ImageSharp.Image.Load(background_image);
            background.Mutate(i => i.Resize(width, height));

            // Init Frame Rate
            DateTime lastLoop = DateTime.MinValue;
            double delay = 1000 / virtualcam_fps;

            // Result Video Loop
            while (in_progress)
            {
                if ((DateTime.UtcNow - lastLoop).TotalMilliseconds < delay) continue;                

                // Read Buffer
                buffer_mutex.WaitOne();
                Bitmap img = buffer_frame;
                buffer_frame = null;
                buffer_mutex.ReleaseMutex();
                if (img == null) continue;
                lastLoop = DateTime.UtcNow;
                
                byte[] raw_data;
                int imline = 0;

                // Remove Background & Get Image Data
                if (chromakey_remove)
                {
                    (width, height, raw_data) = ChromakeyRemover.RemoveChromaKey2Bytes(img, chromakey_velocity, chromakey_treshold, chromakey_channel);
                }
                else // Get Image Data
                {
                    width = img.Width;
                    height = img.Height;
                    BitmapData iData = img.LockBits(new System.Drawing.Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    raw_data = new byte[Math.Abs(iData.Stride) * img.Height];
                    Marshal.Copy(iData.Scan0, raw_data, 0, raw_data.Length);
                    img.UnlockBits(iData);
                };
                img.Dispose();

                // Get Result Frame
                SixLabors.ImageSharp.Image<Rgb24> result_frame = background.CloneAs<Rgb24>();

                // Add Overlay
                SixLabors.ImageSharp.Image ovelay = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(raw_data, width, height);
                result_frame.Mutate(i => i.DrawImage(ovelay, new SixLabors.ImageSharp.Rectangle(0, 0, ovelay.Width, ovelay.Height), 1f));
                ovelay.Dispose();

                // Get RAW Data
                imline = result_frame.Width * 3;
                raw_data = new byte[imline * result_frame.Height];
                result_frame.CopyPixelDataTo(raw_data);

                // Clear Mem
                result_frame.Dispose();

                // Send Horizontal Lines to Virtual Camera
                for (int i = 0; i < raw_data.Length; i += imline)
                    akvcamproc.StandardInput.BaseStream.Write(raw_data, i, imline);
            };
        }
    }
}
