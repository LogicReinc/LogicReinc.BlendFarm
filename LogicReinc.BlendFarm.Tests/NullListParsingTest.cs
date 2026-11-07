using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// A null list or array must serialize (as empty), a failure response with a null list used to throw
    /// during serialization, so no reply was sent and the client waited forever.
    /// </summary>
    [TestClass]
    public class NullListParsingTest
    {
        private static T RoundTrip<T>(T obj)
        {
            byte[] data = BinaryParser.Serialize(obj);
            return BinaryParser.Deserialize<T>(new MemoryStream(data));
        }

        [TestMethod]
        public void NullListRoundTripsAsEmpty()
        {
            RenderBatchResponse resp = new RenderBatchResponse()
            {
                Success = false,
                TaskID = "task",
                Message = "Exception: failed",
                SubTaskIDs = null
            };

            RenderBatchResponse result = RoundTrip(resp);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("task", result.TaskID);
            Assert.AreEqual("Exception: failed", result.Message);
            Assert.IsNotNull(result.SubTaskIDs);
            Assert.AreEqual(0, result.SubTaskIDs.Count);
        }

        [TestMethod]
        public void NullArrayRoundTripsAsEmpty()
        {
            RecoverRequest req = new RecoverRequest() { SessionIDs = null };

            RecoverRequest result = RoundTrip(req);

            Assert.IsNotNull(result.SessionIDs);
            Assert.AreEqual(0, result.SessionIDs.Length);
        }

        [TestMethod]
        public void NullListIsWireCompatibleWithEmptyList()
        {
            byte[] withNull = BinaryParser.Serialize(new RenderBatchResponse() { TaskID = "t", SubTaskIDs = null });
            byte[] withEmpty = BinaryParser.Serialize(new RenderBatchResponse() { TaskID = "t", SubTaskIDs = new List<string>() });

            CollectionAssert.AreEqual(withEmpty, withNull);
        }

        [TestMethod]
        public void ListValuesRoundTrip()
        {
            RenderBatchResponse result = RoundTrip(new RenderBatchResponse()
            {
                Success = true,
                SubTaskIDs = new List<string>() { "a", "b" }
            });

            CollectionAssert.AreEqual(new List<string>() { "a", "b" }, result.SubTaskIDs);
        }

        [TestMethod]
        public void SubTaskIDsDefaultsToEmpty()
        {
            Assert.IsNotNull(new RenderBatchResponse().SubTaskIDs);
        }
    }
}
