using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

public class Program
{
    public static void Main(string[] args)
    {
        Console.Title = "Goofy SSH";

        Console.Write("Hostname: ");
        string hostname = Console.ReadLine();
        if (hostname == "")
            hostname = "ssh.marceldobehere.com";

        if (hostname.EndsWith(":443"))
            hostname = "wss://" + hostname;
        else
            hostname = "ws://" + hostname;

        Console.Write("Proxy only? (y/n): ");
        string proxyOnlyStr = Console.ReadLine();
        bool proxyOnly = proxyOnlyStr == "y";

        string username = "";
        if (!proxyOnly)
        {
            Console.Write("Username: ");
            username = Console.ReadLine();
            if (username == "")
                username = "marcel";
        }

        Console.WriteLine();


        try
        {
            StartLocalTcpServer().Wait();
            StartWebsocketClient(hostname).Wait();
            RunServer();
            RunClient();
            if (!proxyOnly)
                RunSSH(username);
            else
                while (!shouldExit)
                    Thread.Sleep(100);
        }
        catch (Exception e)
        {
            Console.WriteLine("> Exception: " + e.Message);
        }

        try
        {
            CloseServerAndClient().Wait();
        }
        catch (Exception e)
        {
            Console.WriteLine("\n> Exception on close: " + e.Message);
        }
    }
    static bool shouldExit = false;

    public static TcpListener tcpListener;
    public static NetworkStream networkStream;
    public static int tcpPort;
    public static Random rnd = new Random();
    public static async Task StartLocalTcpServer()
    {
        Console.WriteLine("> Starting TCP Server.");
        for (int i = 0; i < 10; i++)
        {
            try
            {
                tcpPort = rnd.Next(6000, 7000);
                tcpListener = new TcpListener(IPAddress.Loopback, tcpPort);
                tcpListener.Start();
                Console.WriteLine($"> TCP Server listening on port {tcpPort}");
                break;
            }
            catch (Exception e)
            {
                Console.WriteLine("> TCP Server Exception: " + e.Message);
                continue;
            }
        }
    }

    public static ClientWebSocket clientWebSocket;
    public static async Task StartWebsocketClient(string hostname)
    {
        Console.WriteLine("> Starting WS Client.");
        clientWebSocket = new ClientWebSocket();
        using SocketsHttpHandler handler = new();
        await clientWebSocket.ConnectAsync(new Uri(hostname), new HttpMessageInvoker(handler), CancellationToken.None);
        Console.WriteLine("> Connected to Websocket server: " + clientWebSocket.State);
        
    }

    public static async Task RunClient()
    {
        Console.WriteLine("> Running WS Client.");

        while (networkStream == null)
            await Task.Delay(100);
        
        try
        {
            // Proxy data from WS Client to TCP Server
            while (true)
            {
                // Read data from WS Client and send to TCP Server
                List<byte> data = new List<byte>();
                while (true)
                {
                    byte[] buffer = new byte[1024];
                    WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    data.AddRange(buffer.Take(result.Count));
                    if (result.EndOfMessage)
                        break;
                }

                await networkStream.WriteAsync(data.ToArray(), 0, data.Count);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("\n> WS Proxy Exception: " + e.Message);
            shouldExit = true;
        }
    }

    public static async Task RunServer()
    {
        Console.WriteLine("> Running TCP Server.");

        TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();

        networkStream = tcpClient.GetStream();
        try
        {
            // Proxy data between TCP Server and WS Client
            while (true)
            {
                // Read data from TCP Server and send to WS Client
                byte[] buffer = new byte[1024];
                int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                    continue;

                await clientWebSocket.SendAsync(new ArraySegment<byte>(buffer, 0, bytesRead), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("\n> TCP Proxy Exception: " + e.Message);
            shouldExit = true;
        }
    }

    public static async Task ReadAndPrintStream(StreamReader streamReader, bool error)
    {
        // We need to check each character because ReadLine() waits for a newline
        while (true)
        {
            char[] buffer = new char[1];
            int count = await streamReader.ReadAsync(buffer, 0, 1);
            if (count == 0)
                break;
            
            char c = buffer[0];
            if (error)
                Console.Error.Write(c);
            else
                Console.Write(c);
        }
    }

    public static void RunSSH(string username)
    {
        Console.WriteLine("> Starting SSH Client.");

        // Start SSH client
        ProcessStartInfo psi = new ProcessStartInfo("ssh", $"{username}@localhost -p {tcpPort} -o StrictHostKeyChecking=no -tt");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = false;
        psi.WindowStyle = ProcessWindowStyle.Hidden;

        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;


        Process process = new Process();
        process.StartInfo = psi;
        process.Start();

        Console.Clear();

        // Redirect SSH Output to Console
        Task cStdOut = ReadAndPrintStream(process.StandardOutput, false);
        Task cStdErr = ReadAndPrintStream(process.StandardError, true);


        // Redirect Console Input to SSH
        while (!process.HasExited && !shouldExit)
        {
            if (!Console.KeyAvailable)
                continue;

            ConsoleKeyInfo info = Console.ReadKey(true);
            process.StandardInput.Write(info.KeyChar);
        }

        cStdOut.Wait();
        cStdErr.Wait();
        Console.WriteLine("\n> Streams closed");
    }

    public static async Task CloseServerAndClient()
    {
        Console.WriteLine("> Closing TCP Server and WS Client.");

        await clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        networkStream.Close();
        tcpListener.Stop();
    }
}