using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Client.Exceptions
{
    public class SyncException : Exception
    {
        public SyncException(string msg) : base(msg)
        {

        }
    }
}
