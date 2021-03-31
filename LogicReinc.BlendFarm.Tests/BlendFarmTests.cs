using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Tests for top-level manager
    /// TODO: Has to be split out
    /// REQUIRES INDIVIDUAL RUNS
    /// </summary>
    [TestClass]
    public class BlendFarmTests
    {
        private static bool REMOVE_BLENDER = false;
        private static bool REMOVE_RESULTS = false;

        private static string BLEND_FILE = "BlendFarmDemo.blend";
        private static string BLEND_VERSION = "blender-2.91.0";

        private static int PORT = 18585;
        private static string THIS_NAME = "This";
        private static string THIS_ADDRESS = $"127.0.0.1:{PORT}";

        private static string RESULTS_DIRECTORY = "BlendFrameTests_Results";

        private static string SESSION = "whatever";

        public static BlenderManager blender = null;
        public static RenderServer server = null;
        public static BlendFarmManager manager = null;

        [ClassInitialize]
        public static void Init(TestContext context)
        {
            blender = new BlenderManager();
            server = new RenderServer(PORT, -1, true);
            manager = new BlendFarmManager(BLEND_FILE, BLEND_VERSION);
            server.Start();
            Thread.Sleep(3000);

            if (!Directory.Exists(RESULTS_DIRECTORY))
                Directory.CreateDirectory(RESULTS_DIRECTORY);
        }
        [ClassCleanup]
        public static void Cleanup()
        {
            manager.DisconnectAll();
            server.Stop();

            if (REMOVE_BLENDER)
            {
                try
                {
                    if (Directory.Exists(blender.BlenderData))
                        Directory.Delete(blender.BlenderData, true);
                }
                catch (Exception ex)
                {

                }
            }
            try
            {
                if (Directory.Exists(blender.RenderData))
                    Directory.Delete(blender.RenderData, true);
            }
            catch (Exception ex)
            {

            }
            try
            {
                if (Directory.Exists(blender.RenderData))
                    Directory.Delete(blender.RenderData, true);
            }
            catch (Exception ex)
            {

            }

            if (REMOVE_RESULTS &&  Directory.Exists(RESULTS_DIRECTORY))
                Directory.Delete(RESULTS_DIRECTORY, true);
        }

        [TestMethod]
        public async Task BasicConnect()
        {
            manager.AddNode(THIS_NAME, THIS_ADDRESS);

            RenderNode node = await manager.Connect(THIS_NAME);

            string hostname = Environment.MachineName;

            Assert.AreEqual(hostname, node.ComputerName);
            Assert.AreEqual(Environment.ProcessorCount, node.Cores);
        }

        [TestMethod]
        public async Task Prepare()
        {
            await BasicConnect();

            RenderNode node = manager.GetNodeByName(THIS_NAME);

            PrepareResponse resp = await node.PrepareVersion(BLEND_VERSION);
            Assert.IsTrue((await node.PrepareVersion(BLEND_VERSION))?.Success ?? false, resp.Message);
            Assert.IsTrue(await node.CheckVersion(BLEND_VERSION));
        }

        [TestMethod]
        public async Task Sync()
        {
            await BasicConnect();

            RenderNode node = manager.GetNodeByName(THIS_NAME);

            long lastFileChange = new FileInfo(BLEND_FILE).LastWriteTime.Ticks;

            SyncResponse resp = null;
            using (FileStream stream = new FileStream(BLEND_FILE, FileMode.Open))
                resp = await node.SyncFile(SESSION, lastFileChange, stream, Compression.Raw);

            Assert.IsTrue(resp.Success);
        }
        [TestMethod]
        public async Task SyncCompressed()
        {
            await BasicConnect();

            RenderNode node = manager.GetNodeByName(THIS_NAME);

            long lastFileChange = new FileInfo(BLEND_FILE).LastWriteTime.Ticks;

            SyncResponse resp = null;
            using (MemoryStream str = new MemoryStream())
            using (GZipStream zip = new GZipStream(str, CompressionMode.Compress))
            using (FileStream stream = new FileStream(BLEND_FILE, FileMode.Open))
            {
                byte[] buffer = new byte[4096];
                int read = 0;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    zip.Write(buffer, 0, read);

                str.Seek(0, SeekOrigin.Begin);
                resp = await node.SyncFile(SESSION, lastFileChange, str, Compression.GZip);
            }
            Assert.IsTrue(resp.Success);
        }


        [TestMethod]
        public async Task Render()
        {
            await Prepare();

            RenderNode node = manager.GetNodeByName(THIS_NAME);

            long lastFileChange = new FileInfo(BLEND_FILE).LastWriteTime.Ticks;
            SyncResponse respSync = null;
            using (FileStream stream = new FileStream(BLEND_FILE, FileMode.Open))
                respSync = await node.SyncFile(SESSION, lastFileChange, stream, Compression.Raw);
            Assert.IsTrue(respSync.Success);


            RenderResponse respRender = await node.Render(new RenderRequest()
            {
                FileID = lastFileChange,
                SessionID = SESSION,
                TaskID = "Whatever",
                Version = BLEND_VERSION,
                Settings = new Shared.RenderPacketModel()
                {
                    Width = 640,
                    Height = 360,
                    Samples = 8
                }
            });

            Assert.IsTrue(respRender.Success);
            File.WriteAllBytes($"{RESULTS_DIRECTORY}/Test.Render.png", respRender.Data);
            Assert.IsTrue(respRender.Data != null && respRender.Data.Length > 0);
            //Check equality
        }

        private async Task PrepareManagedRender()
        {
            manager.AddNode(THIS_NAME, THIS_ADDRESS);
            await manager.ConnectAll();

            Assert.AreEqual(1, manager.Connected);

            await manager.Prepare(BLEND_VERSION);
            await manager.Sync();
        }

        [TestMethod]
        public async Task Render_Managed_Split()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();

            await PrepareManagedRender();

            long prepTime = watch.ElapsedMilliseconds;
            watch.Restart();

            int render = 0;
            Bitmap final = await manager.Render(new RenderManagerSettings()
            {
                OutputWidth = 640,
                OutputHeight = 360,
                Strategy = RenderStrategy.SplitHorizontal,
                Samples = 8
            }, (task, bitmap) =>
            {
                bitmap.Save($"{RESULTS_DIRECTORY}/Test.Render_Managed_Split.{render}.png");
            });
            long renderTime = watch.ElapsedMilliseconds;

            final.Save($"{RESULTS_DIRECTORY}/Test.Render_Managed_Split.png");

            File.WriteAllText($"{RESULTS_DIRECTORY}/Test.Render_Managed_Split.Info.json", JsonSerializer.Serialize(new Dictionary<string, object>()
            {
                { "PrepTime", prepTime },
                { "RenderTime", renderTime },
            }));
        }

        [TestMethod]
        public async Task Render_Managed_Chunked()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();

            await PrepareManagedRender();

            long prepTime = watch.ElapsedMilliseconds;
            watch.Restart();

            int render = 0;

            bool gotBitmap = false;
            Bitmap final = await manager.Render(new RenderManagerSettings()
            {
                Strategy = RenderStrategy.Chunked,
                ChunkHeight = 1,
                ChunkWidth = 0.5m,
                OutputWidth = 640,
                OutputHeight = 360,
                Samples = 8
            }, null,
            (task, bitmap) =>
            {
                bitmap.Save($"{RESULTS_DIRECTORY}/Test.Render_Managed_Chunked.Tile.{render++}.png");
                gotBitmap = true;
            });
            long renderTime = watch.ElapsedMilliseconds;

            final.Save($"{RESULTS_DIRECTORY}/Test.Render_Managed_Chunked.png");

            File.WriteAllText($"{RESULTS_DIRECTORY}/Test.Render_Managed_Chunked.Info.json", JsonSerializer.Serialize(new Dictionary<string, object>()
            {
                { "PrepTime", prepTime },
                { "RenderTime", renderTime },
            }));

            Assert.IsNotNull(final);
            Assert.IsTrue(gotBitmap);
        }

        [TestMethod]
        public async Task Render_Managed_SplitChunked()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();

            await PrepareManagedRender();

            long prepTime = watch.ElapsedMilliseconds;
            watch.Restart();

            int render = 0;

            bool gotBitmap = false;
            Bitmap final = await manager.Render(new RenderManagerSettings()
            {
                Strategy = RenderStrategy.SplitChunked,
                ChunkHeight = 1,
                ChunkWidth = 0.25m,
                OutputWidth = 640,
                OutputHeight = 360,
                Samples = 8
            }, null,
            (task, bitmap) =>
            {
                bitmap.Save($"{RESULTS_DIRECTORY}/Test.Render_Managed_SplitChunked.Tile.{render++}.png");
                gotBitmap = true;
            });
            long renderTime = watch.ElapsedMilliseconds;

            final.Save($"{RESULTS_DIRECTORY}/Test.Render_Managed_SplitChunked.png");

            File.WriteAllText($"{RESULTS_DIRECTORY}/Test.Render_Managed_SplitChunked.Info.json", JsonSerializer.Serialize(new Dictionary<string, object>()
            {
                { "PrepTime", prepTime },
                { "RenderTime", renderTime },
            }));

            Assert.IsNotNull(final);
            Assert.IsTrue(gotBitmap);
        }


        /*
        [TestMethod]
        public async Task Render_Testing()
        {
            await PrepareManagedRender();

            Bitmap final = await manager.Render(new RenderManagerSettings()
            {
                Strategy = RenderStrategy.Chunked,
                Width = 1920,
                Height = 1080
            }, null);


            final.Save("result.png");
        }*/
    }
}
