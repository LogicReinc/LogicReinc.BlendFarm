using LogicReinc.BlendFarm.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Tests for rendering directly (no manager)
    /// </summary>
    [TestClass]
    public class BlenderTests
    {
        private static string BLEND_FILE = "BlendFarmDemo.blend";
        private static string BLEND_VERSION = "blender-2.91.0";

        private static string RESULTS_DIRECTORY = "BlendFrameTests_Results";

        public static BlenderManager Blender { get; set; } = new BlenderManager();

        public static bool WRITE_LOGS = false;
        private static StringWriter _writer = new StringWriter();

        [ClassInitialize]
        public static void Init(TestContext context)
        {
            Directory.CreateDirectory(RESULTS_DIRECTORY);

        }
        [ClassCleanup]
        public static void Cleanup()
        {
        }

        [TestMethod]
        public void RenderTest1()
        {
            if (WRITE_LOGS)
                Console.SetOut(_writer);

            Blender.Prepare(BLEND_VERSION);

            string result = Blender.Render(BLEND_VERSION, BLEND_FILE, new BlenderRenderSettings()
            {
                Width = 1920 / 4,
                Height = 1080 / 4,
                ComputeUnit = Shared.RenderType.CPU,
                TileWidth = 16,
                TileHeight = 16,
                Samples = 8
            });

            Assert.IsNotNull(result);

            File.Move(result, Path.Combine(RESULTS_DIRECTORY, "RenderTest1.png"), true);

            if (WRITE_LOGS)
                File.WriteAllText($"BlenderTests.txt", _writer.ToString());
        }
    }
}
