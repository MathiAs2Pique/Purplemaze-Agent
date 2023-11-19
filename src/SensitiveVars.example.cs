namespace Pagent
{
    public static class sensitiveVars
    {
        public const int bindPort = 3000;
        public const string backupIp = "1.1.1.1";
        public const string masterEndpoint = "";
        public const string sToken = ""; // Outgoing
        public const string keyHeader = ""; // Outgoing
        public const string reqKey = ""; // Incoming
        public const string forbiddenErrorStr = "";
        public static List<string> queryIPs = new List<string>()
        {
            "1.1.1.1/32",
            "2.2.2.2/32"
        };
    }
}
