using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace LogicReinc.BlendFarm.Shared
{
    public static class Hash
    {
        public static string ComputeSyncHash(byte[] bytes)
        {
            using(MD5 md5 = MD5.Create())
            {
                return Convert.ToBase64String(md5.ComputeHash(bytes));
            }
        }
    }
}
