using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RobleysQuestingFramework
{
    public class RewardDelivery
    {
        public string ID;
        public long PlayerID;
        public string Handler;
        public string Payload;
        public bool Acknowledged;

        public static string CreateID(string world, long player, string quest, string instance, string reward)
        {
            string identity = JsonConvert.SerializeObject(new object[] { world, player, quest, instance ?? "", reward });
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
            }
        }
    }
}
