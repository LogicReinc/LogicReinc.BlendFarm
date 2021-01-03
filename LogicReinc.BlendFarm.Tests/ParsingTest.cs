using LogicReinc.BlendFarm.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Tests for parsing
    /// </summary>
    [TestClass]
    public class ParsingTest
    {
        static string[] existing_versions = new string[] { "blender-2.91.0", "blender-2.83.9" };

        [TestMethod]
        public void GetBlenderVersions()
        {
            List<BlenderVersion> versions = BlenderVersion.GetBlenderVersions();

            Assert.IsTrue(versions.Count > 10);
            Assert.IsFalse(existing_versions.Any(x => !versions.Any(y => y.Name == x)));

            Console.WriteLine(string.Join('\n', versions.Select(x => x.Name)));
        }
    }
}
