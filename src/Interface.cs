using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pagent
{
    // Really smart class name !
    public class @interface
    {
        private string masterEndpoint;
        private string sToken;
        private string keyHeader;
        private string forbiddenErrorStr;
        private int bindPort;
        private List<string> queryIPs;
        private string outgoingAddress = null;

        public @interface(string specialAddress = null)
        {    
            masterEndpoint = sensitiveVars.masterEndpoint;
            sToken = sensitiveVars.sToken;
            keyHeader = sensitiveVars.keyHeader;
            forbiddenErrorStr = sensitiveVars.forbiddenErrorStr;
            bindPort = sensitiveVars.bindPort;
            queryIPs = sensitiveVars.queryIPs;
            outgoingAddress = specialAddress;
        }

        // Check IPv4
        private bool IsIPValid(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }

        // Check IPv4 or Range
        public bool IsIpOrRangeValid(string ipOrRange)
        {
            // In case of a range
            if (ipOrRange.Contains("/"))
            {
                // Check IPv4
                if (!IsIPValid(ipOrRange.Split("/")[0]))
                    return false;

                // Check range
                int range = -1;
                if (!Int32.TryParse(ipOrRange.Split("/")[1], out range) && (range >= 0 && range <= 32))
                    return false;

                return true;
            }
            else
                return IsIPValid(ipOrRange);
        }

        // Execute an OS command
        private void ExecuteCommand(string command)
        {
            try
            {
#if LINUX
                var psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "/bin/bash";
                psi.Arguments = "-c \"" + command + "\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var process = System.Diagnostics.Process.Start(psi);
                process.WaitForExit();
#elif WINDOWS
                var psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/C " + command;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var process = System.Diagnostics.Process.Start(psi);
                process.WaitForExit();
#endif
            }
            catch(Exception e)
            {
                Console.WriteLine($"[!] Error while executing {command}:\n{e.Message}");
                Environment.Exit(1);
            }
        }

        // Add an IP (range) to the whitelist
        public void AddIP(string ip, int port, bool isSpecial = false)
        {
            // Validate IP/range
            if (!IsIpOrRangeValid(ip))
                return;

            // Single IP ? Convert to /32
            if (!ip.Contains("/"))
                ip += "/32";

#if LINUX
            ExecuteCommand($"iptables -I INPUT -s {ip} -p tcp --dport {port} -j ACCEPT");
            ExecuteCommand($"iptables -I INPUT -s {ip} -p udp --dport {port} -j ACCEPT");
#elif WINDOWS
            ExecuteCommand($"netsh advfirewall firewall add rule name=\"{((!isSpecial)?"PPM":"PPMQIP")}\" dir=in action=allow protocol=TCP localport={port} remoteip=\"{ip}\"");
            ExecuteCommand($"netsh advfirewall firewall add rule name=\"{((!isSpecial)?"PPM":"PPMQIP")}\" dir=in action=allow protocol=UDP localport={port} remoteip=\"{ip}\"");
#endif

            Console.WriteLine($" [+] Added {ip} to the whitelist (port {port}))");
        }

        public void RemoveIP(string ip, int port)
        {
            // Validate IP/range
            if (!IsIpOrRangeValid(ip))
                return;

            // Single IP ? Convert to /32
            if (!ip.Contains("/"))
                ip += "/32";

#if LINUX
            ExecuteCommand($"iptables -D INPUT -s {ip} -p tcp --dport {port} -j ACCEPT");
            ExecuteCommand($"iptables -D INPUT -s {ip} -p udp --dport {port} -j ACCEPT");
#elif WINDOWS
            ExecuteCommand($"netsh advfirewall firewall delete rule name=\"PPM\" dir=in action=allow protocol=TCP localport={port} remoteip=\"{ip}\"");
            ExecuteCommand($"netsh advfirewall firewall delete rule name=\"PPM\" dir=in action=allow protocol=UDP localport={port} remoteip=\"{ip}\"");
#endif
        }

        public void ClearList()
        {

#if LINUX

            ExecuteCommand("iptables -F; iptables -X");
            Console.WriteLine(" [/] Cleared iptables");
            
#elif WINDOWS

            ExecuteCommand("netsh advfirewall firewall delete rule name=\"PPM\"");
            ExecuteCommand("netsh advfirewall firewall delete rule name=\"PPMQIP\"");
            Console.WriteLine(" [/] Cleared firewall");
#endif

        }

        // Ref: https://stackoverflow.com/questions/65930192/how-to-bind-httpclient-to-a-specific-source-ip-address-in-net-5
        public static HttpClient GetHttpClient(IPAddress address)
        {
            Console.WriteLine($" [\\] Using outgoing IP address {address}");
            if (IPAddress.Any.Equals(address))
                return new HttpClient();

            SocketsHttpHandler handler = new SocketsHttpHandler();

            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

                socket.Bind(new IPEndPoint(address, 0));

                socket.NoDelay = true;

                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);

                    return new NetworkStream(socket, true);
                }
                catch
                {
                    socket.Dispose();

                    throw;
                }
            };

            return new HttpClient(handler);
        }

        // Call Master server to get the list of IP ranges to be whitelisted
        public List<string> getRangesToBeWhitelisted(string slug)
        {
            // List that will be returned
            List<string> returnList = new();

            // Get token
            byte[] data = Convert.FromBase64String(sToken);
            string token = Encoding.UTF8.GetString(data) + slug;

            // Craft the request
            string url = $"{masterEndpoint}?slug={slug}&token={token}";
            HttpClient client = new HttpClient();

            // In case of specific IP address
            if(outgoingAddress != null)
            {
                client = GetHttpClient(IPAddress.Parse(outgoingAddress));
            }
            client.DefaultRequestHeaders.Add("key", keyHeader + slug);

#if WINDOWS
            // Ignore SSL errors on Windows
            System.Net.ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#endif

            // Get response from the server
            var response = client.GetAsync(url).Result;
            string responseString = response.Content.ReadAsStringAsync().Result;
            if (responseString == forbiddenErrorStr)
            {
                Console.WriteLine(" [!] Error: Request refused.");
                Environment.Exit(1);
            }

            // Parse the result and store each IP range in the list.
            string[] lines = responseString.Split("\n");

            foreach (string line in lines)
                if (line != "")
                    returnList.Add(line);

            return returnList;
        }


        // Init WL IP on the port of __this__ software's web server
        public void AntiAntiAntiScan()
        {
            // Allow query IPs
            foreach(string ip in queryIPs)
            {
                AddIP(ip, bindPort, true);
            }

            // Block trafic on port
#if LINUX
            // block tcp
            ExecuteCommand($"iptables -A INPUT -p tcp --dport {bindPort} -j DROP");
            // block udp
            ExecuteCommand($"iptables -A INPUT -p udp --dport {bindPort} -j DROP");

            // We don't need to block on Windows, as the default policy is to block all trafic.
#endif
            Console.WriteLine(" [\\] Init ok");
        }

        public void InitWL(string slug, List<int> ports)
        {
            // Flush
            ClearList();

            // Check if file exists for base iptables
            if (File.Exists("iptables.txt"))
            {
                Console.WriteLine(" [\\] Executing commands from iptables.txt");
                // execute each line
                string[] lines = File.ReadAllLines("iptables.txt");
                foreach (string line in lines)
                {
                    ExecuteCommand(line);
                }
            }

            List<string> ranges = getRangesToBeWhitelisted(slug);
            // For each IP range, add it to the whitelist
            foreach (string range in ranges)
            {
                foreach (int port in ports)
                    AddIP(range, port);
            }

#if LINUX
                // block tcp
                foreach(int port in ports)
                    ExecuteCommand($"iptables -A INPUT -p tcp --dport {port} -j DROP");
                // block udp
                foreach(int port in ports)
                    ExecuteCommand($"iptables -A INPUT -p udp --dport {port} -j DROP");

                // We don't need to block on Windows, as the default policy is to block all trafic.
#endif
                Console.WriteLine(" [\\] Whitelist initialized");

        }
    }
}
