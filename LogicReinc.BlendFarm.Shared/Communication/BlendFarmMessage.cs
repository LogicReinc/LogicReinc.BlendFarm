using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication
{
    /// <summary>
    /// Base class for all communicated packets
    /// </summary>
    public abstract class BlendFarmMessage
    {
        private static Dictionary<string, Type> PackageTypes { get; } = typeof(BlendFarmMessage).Assembly.GetTypes()
                .Where(x => typeof(BlendFarmMessage).IsAssignableFrom(x) && x.GetCustomAttribute<BlendFarmHeaderAttribute>() != null)
                .ToDictionary(x => x.GetCustomAttribute<BlendFarmHeaderAttribute>().Header, y => y);

        public string RequestID { get; set; }
        public string ResponseID { get; set; }


        public static bool HasPackageType(string name)
        {
            return PackageTypes.ContainsKey(name);
        }
        public static Type GetPackageType(string name)
        {
            if (PackageTypes.ContainsKey(name))
                return PackageTypes[name];
            return null;
        }
        public static string GetPackageName(Type type)
        {
            BlendFarmHeaderAttribute header = type.GetCustomAttribute<BlendFarmHeaderAttribute>();

            if (header == null)
                return null;
            return header.Header;
        }
    }
}
