using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Infrastructure.AgentBridge;

namespace Syncro.Desktop.Services.SyncroCLI
{
    public class AgentBridgeServer : IDisposable
    {
        private const string PipeName = "syncro-agent-bridge";
        private CancellationTokenSource? _cts;
        private Task? _serverTask;

        public event Action<AgentPacket>? OnPacketReceived;
        public event Action<string>? OnRawMessageReceived;

        public void Start()
        {
            if (_serverTask != null) return;

            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? serverStream = null;
                try
                {
                    serverStream = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await serverStream.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(serverStream, Encoding.UTF8);
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        OnRawMessageReceived?.Invoke(line);

                        try
                        {
                            var packet = JsonConvert.DeserializeObject<AgentPacket>(line);
                            if (packet != null)
                            {
                                OnPacketReceived?.Invoke(packet);
                            }
                        }
                        catch
                        {
                            // Could be legacy non-structured JSON log format, create a generic status packet
                            var genericPacket = new AgentPacket
                            {
                                PacketType = "Status",
                                Payload = line
                            };
                            OnPacketReceived?.Invoke(genericPacket);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var errorPacket = new AgentPacket
                    {
                        PacketType = "Error",
                        Payload = $"IPC Stream Error: {ex.Message}"
                    };
                    OnPacketReceived?.Invoke(errorPacket);
                    await Task.Delay(1000, token);
                }
                finally
                {
                    if (serverStream != null)
                    {
                        try
                        {
                            if (serverStream.IsConnected)
                            {
                                serverStream.Disconnect();
                            }
                        }
                        catch {}
                        serverStream.Dispose();
                    }
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _serverTask = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
