using System.Net;
using System.Text;

namespace Pagent
{
    public class webServer
    {
        public static HttpListener listener;
        public string key;

        public webServer(){
            key = sensitiveVars.reqKey;
        }

        public static long getTimestamp()
        {
            return DateTimeOffset.Now.ToUnixTimeSeconds();
        }

        public static async Task HandleIncomingConnections(List<int> ports, @interface linterface ,webServer server)
        {
            bool run = true;
            while (run)
            {
                try
                {
                    HttpListenerContext ctx = await listener.GetContextAsync();
                    HttpListenerRequest req = ctx.Request;
                    HttpListenerResponse resp = ctx.Response;

                    string returnStr = "{\"success\":false, \"error\":\"Generic Error.\"}";
                    bool doContinue = ((req.Headers.AllKeys.Contains("Key") && req.Headers["Key"] == server.key) || (req.Headers.AllKeys.Contains("key") && req.Headers["key"] == server.key));

                    if (!doContinue)
                    {                
                        returnStr = "{\"success\":false, \"error\":\"Wrong / missing authorization.\"}";        
                        Console.WriteLine("[!] Wrong / missing authorization");
                    }
                    
                    if (!sensitiveVars.queryIPs.Contains(req.RemoteEndPoint.Address.ToString()+"/32"))
                    {
                        doContinue = true;
                        Console.WriteLine("[!] Wrong query IP: " + req.RemoteEndPoint.Address.ToString());
                    }

                    // Check the path
                    if (doContinue && req.Url.AbsolutePath.Contains("/alive"))
                    {
                        Console.WriteLine("[.] Alive request received.");
                        returnStr = "{\"success\":true}";
                    }
                    else if (doContinue && req.Url.AbsolutePath.Contains("/wl"))
                    {
                        Console.WriteLine($"[.] Whitelist request received. [Method:{req.HttpMethod}  IP: {req.RemoteEndPoint}]");
                        // check method
                        if (req.HttpMethod == "POST")
                        {
                            // get POST data
                            string? ip = req.QueryString["ip"];

                            if (ip != null)
                            {
                                foreach (int port in ports)
                                    linterface.AddIP(ip, port);
                                returnStr = "{\"success\":true}";
                            }
                            else
                                doContinue = false;
                        }
                        else if (req.HttpMethod == "DELETE")
                        {
                            // get POST data
                            string? ip = req.QueryString["ip"];
                            
                            if(ip != null)
                            {
                                foreach (int port in ports)
                                    linterface.RemoveIP(ip, port);
                                returnStr = "{\"success\":true}";
                            }
                            else
                                doContinue = false;
                        }
                        else
                        {
                            doContinue = false;
                        }
                    }
                    else
                    {
                        doContinue = false;
                    }

                    if (!doContinue)
                    {
                        resp.StatusCode = 400;
                        resp.ContentType = "application/json";
                        byte[] buffer = Encoding.UTF8.GetBytes(returnStr);
                        resp.ContentLength64 = buffer.Length;
                        await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        resp.Close();
                    }
                    else
                    {
                        resp.StatusCode = 200;
                        resp.ContentType = "application/json";
                        byte[] buffer = Encoding.UTF8.GetBytes(returnStr);
                        resp.ContentLength64 = buffer.Length;
                        await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        resp.Close();
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        public static Task startWebServer(List<int> ports, @interface linterface)
        {

            string url = $"http://*:{sensitiveVars.bindPort}/";
            listener = new HttpListener();
            listener.Prefixes.Add(url);

            while (true)
            {
                try
                {
                    Console.WriteLine(" [\\] Starting web server...");
                    listener.Start();
                    Console.WriteLine(" [\\] Web server started.");
                    Console.WriteLine(" [\\] Listening.");
                    Task listenTask = HandleIncomingConnections(ports, linterface, new webServer());
                    listenTask.GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    Console.WriteLine("[!] Error:\n" + e.Message);
                    Console.WriteLine("[!] Exiting...");
                    Environment.Exit(1);
                }
                finally
                {
                    if (listener.IsListening)
                        listener.Stop();
                }
                listener.Close();
            }
        }
    }
}
