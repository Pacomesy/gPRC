using Grpc.Net.Client;
using Grpc.Core;
using System.Net.Http;
using NationalInstruments.Grpc.NiDAQmx;
using NationalInstruments.Grpc.Device;
using DAQmxWebApp.Models;

namespace DAQmxWebApp.Services;

public enum DAQmxState { Disconnected, Connected, Running }

public class DAQmxService : IDisposable
{
    // ── Public state ────────────────────────────────────────────────────────
    public DAQmxState State { get; private set; } = DAQmxState.Disconnected;
    public string StatusMessage { get; private set; } = "Disconnected.";
    public DAQmxConfig Config { get; private set; } = new();

    // ── Events ───────────────────────────────────────────────────────────────
    /// <summary>Raised when new samples are available. (samples[], count)</summary>
    public event Action<double[], int>? DataReceived;
    /// <summary>Raised whenever State or StatusMessage change.</summary>
    public event Action? StateChanged;

    // ── Private gRPC members ─────────────────────────────────────────────────
    private SocketsHttpHandler? _grpcHttpHandler;
    private GrpcChannel? _channel;
    private NiDAQmx.NiDAQmxClient? _client;
    private Session? _task;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;

    // ── Connection ────────────────────────────────────────────────────────────
    public async Task<bool> ConnectAsync(DAQmxConfig config)
    {
        if (State != DAQmxState.Disconnected)
            await DisconnectAsync();

        Config = config;
        const int connectTimeoutSeconds = 15;
        try
        {
            _grpcHttpHandler?.Dispose();
            _grpcHttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true
            };
            var options = new GrpcChannelOptions
            {
                Credentials = Grpc.Core.ChannelCredentials.Insecure,
                HttpHandler = _grpcHttpHandler
            };
            _channel = GrpcChannel.ForAddress(config.GrpcAddress, options);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(connectTimeoutSeconds));
            await _channel.ConnectAsync(cts.Token);

            _client = new NiDAQmx.NiDAQmxClient(_channel);
            SetState(DAQmxState.Connected, $"Connected to {config.ServerAddress}:{config.ServerPort}");
            return true;
        }
        catch (OperationCanceledException)
        {
            // GrpcChannel.ConnectAsync uses the token for timeout; cancellation reads as "A task was canceled."
            SetState(
                DAQmxState.Disconnected,
                $"Connection timed out after {connectTimeoutSeconds}s at {config.GrpcAddress}. " +
                "Confirm the NI DAQmx gRPC server is running and the address/port match.");
            _channel?.Dispose();
            _channel = null;
            _grpcHttpHandler?.Dispose();
            _grpcHttpHandler = null;
            return false;
        }
        catch (Exception ex)
        {
            SetState(DAQmxState.Disconnected, $"Connection failed: {ex.Message}");
            _channel?.Dispose();
            _channel = null;
            _grpcHttpHandler?.Dispose();
            _grpcHttpHandler = null;
            return false;
        }
    }

    // ── Task lifecycle ────────────────────────────────────────────────────────
    public async Task<bool> StartAsync()
    {
        if (State != DAQmxState.Connected || _client is null)
        {
            SetState(State, "Cannot start: not connected.");
            return false;
        }

        // Create task
        CreateTaskResponse createReply;
        try
        {
            createReply = _client.CreateTask(new CreateTaskRequest { SessionName = Config.SessionName });
        }
        catch (RpcException ex)
        {
            SetState(State, $"Start failed (gRPC): {ex.Message}");
            return false;
        }
        catch (HttpRequestException ex)
        {
            SetState(State, $"Start failed (HTTP): {ex.Message}");
            return false;
        }

        if (createReply.Status != 0)
        {
            SetState(State, $"CreateTask failed (status {createReply.Status}).");
            return false;
        }
        _task = createReply.Task;

        // Configure AI voltage channel
        var chanReply = _client.CreateAIVoltageChan(new CreateAIVoltageChanRequest
        {
            Task = _task,
            PhysicalChannel = Config.PhysicalChannel,
            NameToAssignToChannel = "",
            TerminalConfig = InputTermCfgWithDefault.CfgDefault,
            MinVal = Config.MinVoltage,
            MaxVal = Config.MaxVoltage,
            Units = VoltageUnits2.Volts,
            CustomScaleName = ""
        });
        if (chanReply.Status != 0)
        {
            SetState(State, $"CreateAIVoltageChan failed (status {chanReply.Status}).");
            await ClearTaskAsync();
            return false;
        }

        // Configure sample clock
        var timingReply = _client.CfgSampClkTiming(new CfgSampClkTimingRequest
        {
            Task = _task,
            Source = "",
            Rate = Config.SampleRate,
            ActiveEdge = Edge1.Rising,
            SampleMode = AcquisitionType.ContSamps,
            SampsPerChan = (ulong)(Config.SampsPerRead * Config.BufferMultiplier)
        });
        if (timingReply.Status != 0)
        {
            SetState(State, $"CfgSampClkTiming failed (status {timingReply.Status}).");
            await ClearTaskAsync();
            return false;
        }

        // Start task
        var startReply = _client.StartTask(new StartTaskRequest { Task = _task });
        if (startReply.Status != 0)
        {
            SetState(State, $"StartTask failed (status {startReply.Status}).");
            await ClearTaskAsync();
            return false;
        }

        // Launch read loop
        _readCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token));

        SetState(DAQmxState.Running, $"Running — {Config.PhysicalChannel} @ {Config.SampleRate} Hz");
        return true;
    }

    public async Task StopAsync()
    {
        if (State != DAQmxState.Running || _client is null || _task is null)
            return;

        // Stop the hardware task first – this unblocks any in-progress ReadAnalogF64 call immediately.
        // Waiting for the read loop before stopping the task would cause a deadlock: the loop blocks in
        // ReadAnalogF64 waiting for samples that never arrive because the task is still running.
        try { _client.StopTask(new StopTaskRequest { Task = _task }); } catch { }

        // Now signal the read loop and wait for it to exit (should be nearly instant after StopTask).
        _readCts?.Cancel();
        if (_readLoop is not null)
        {
            try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
            _readLoop = null;
        }

        await ClearTaskAsync();
        SetState(DAQmxState.Connected, "Task stopped.");
    }

    public async Task DisconnectAsync()
    {
        if (State == DAQmxState.Running)
            await StopAsync();

        if (_channel is not null)
        {
            await _channel.ShutdownAsync();
            _channel.Dispose();
            _channel = null;
        }
        _grpcHttpHandler?.Dispose();
        _grpcHttpHandler = null;
        _client = null;
        SetState(DAQmxState.Disconnected, "Disconnected.");
    }

    // ── Read loop ─────────────────────────────────────────────────────────────
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _client is not null && _task is not null)
        {
            try
            {
                var readReply = _client.ReadAnalogF64(new ReadAnalogF64Request
                {
                    Task = _task,
                    NumSampsPerChan = Config.SampsPerRead,
                    Timeout = 5.0,
                    FillMode = GroupBy.GroupByScanNumber,
                    ArraySizeInSamps = (uint)Config.SampsPerRead
                });

                if (readReply.Status != 0)
                {
                    SetState(State, $"Read error (status {readReply.Status}).");
                    break;
                }

                var data = readReply.ReadArray.ToArray();
                DataReceived?.Invoke(data, readReply.SampsPerChanRead);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SetState(State, $"Read exception: {ex.Message}");
                break;
            }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void SetState(DAQmxState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke();
    }

    private Task ClearTaskAsync()
    {
        if (_client is not null && _task is not null)
            _client.ClearTask(new ClearTaskRequest { Task = _task });
        _task = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        _channel?.Dispose();
        _grpcHttpHandler?.Dispose();
    }
}
