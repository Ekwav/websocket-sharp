using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Server;

sealed class Received
{
    public int Count;
    public int Invalid;
    public void Record(string payload)
    {
        if (payload != Count.ToString(CultureInfo.InvariantCulture)) Interlocked.Increment(ref Invalid);
        Interlocked.Increment(ref Count);
    }
}

sealed class Receiver : WebSocketBehavior
{
    public static Receiver Instance;
    public readonly Received Messages = new Received();
    public int Errors;
    protected override void OnError(ErrorEventArgs args) => Interlocked.Increment(ref Errors);
    protected override void OnOpen() => Instance = this;
    protected override void OnMessage(MessageEventArgs args) => Messages.Record(args.Data);
    public void SendFrame(int sequence) => Send(sequence.ToString(CultureInfo.InvariantCulture));
    public bool CheckPing() => Sessions.PingTo(ID);
}

static class Program
{
    static int Main(string[] args)
    {
        bool testClient = args[0] == "client";
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var server = new WebSocketServer(IPAddress.Loopback, port) { KeepClean = false };
        server.AddWebSocketService<Receiver>("/test");
        server.Start();
        using var client = new WebSocket($"ws://127.0.0.1:{port}/test") { WaitTime = TimeSpan.FromSeconds(1) };
        var clientMessages = new Received();
        int clientErrors = 0;
        bool cleanClose = false;
        client.OnMessage += (_, e) => clientMessages.Record(e.Data);
        client.OnError += (_, e) => { Interlocked.Increment(ref clientErrors); Console.WriteLine("client error: " + e.Message); };
        client.OnClose += (_, e) => { cleanClose = e.WasClean && e.Code == 1000; Console.WriteLine($"client close: {e.Code} clean={e.WasClean}"); };
        client.Connect();
        try
        {
            if (!SpinWait.SpinUntil(() => Receiver.Instance != null, 1000))
                throw new Exception("Server did not open");
            for (int i = 0; i < 8200; i++)
            {
                try { if (testClient) Receiver.Instance.SendFrame(i); else client.Send(i.ToString(CultureInfo.InvariantCulture)); }
                catch (InvalidOperationException) { break; }
                if (args.Length < 2 || args[1] != "burst") Thread.Sleep(1);
            }
            bool received = SpinWait.SpinUntil(() => (testClient ? Volatile.Read(ref clientMessages.Count) : Volatile.Read(ref Receiver.Instance.Messages.Count)) == 8200, 5000);
            bool ping = false;
            try { ping = testClient ? Receiver.Instance.CheckPing() : client.Ping(); } catch (InvalidOperationException) { }
            Console.WriteLine($"receiver={args[0]} frames={(testClient ? clientMessages.Count : Receiver.Instance.Messages.Count)} open={client.ReadyState} ping={ping}");
            if (!received || !ping || client.ReadyState != WebSocketState.Open) return 1;
            if (testClient) Receiver.Instance.SendFrame(8200); else client.Send("8200");
            if (!SpinWait.SpinUntil(() => (testClient ? Volatile.Read(ref clientMessages.Count) : Volatile.Read(ref Receiver.Instance.Messages.Count)) == 8201, 1000)) return 1;
            client.Close(1000, "regression completed");
            if (!cleanClose || client.ReadyState != WebSocketState.Closed
                || clientMessages.Invalid != 0 || Receiver.Instance.Messages.Invalid != 0
                || clientErrors != 0 || Receiver.Instance.Errors != 0) return 1;
            Console.WriteLine("PASS: 8201 messages in order, ping succeeds, close is clean, no callback errors");
            return 0;
        }
        finally { client.Close(); server.Stop(); }
    }
}
