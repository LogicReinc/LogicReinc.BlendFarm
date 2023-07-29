using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Exceptions
{
    public class ClientStateException : Exception
    {
        public ClientStateException(string msg, Exception ex = null) : base(msg, ex) { }
    }
}
