using System;
using System.ServiceProcess;
using System.Threading;
using Grpc.Core;
using NationalInstruments.Grpc.NiDAQmx;

namespace DAQmxClient
{
    internal class Program
    {
        static void Main()
        {
            var serverAddress = "localhost";
            var serverPort = "31763";
            var physicalChannel = "Dev1/ai0";
            var sampleRate = 1000.0;
            var sampsPerRead = 100;

            StartGrpcServerIfNeeded();

            Channel channel = new Channel(serverAddress + ":" + serverPort, ChannelCredentials.Insecure);
            var client = new NiDAQmx.NiDAQmxClient(channel);

            var createReply = client.CreateTask(new CreateTaskRequest { SessionName = "DAQmx-ai0" });
            if (createReply.Status != 0)
            {
                Console.WriteLine($"CreateTask failed with status: {createReply.Status}");
                channel.ShutdownAsync().Wait();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            var task = createReply.Task;
            Console.WriteLine("Task created.");

            try
            {
                var chanReply = client.CreateAIVoltageChan(new CreateAIVoltageChanRequest
                {
                    Task = task,
                    PhysicalChannel = physicalChannel,
                    NameToAssignToChannel = "",
                    TerminalConfig = InputTermCfgWithDefault.CfgDefault,
                    MinVal = -10.0,
                    MaxVal = 10.0,
                    Units = VoltageUnits2.Volts,
                    CustomScaleName = ""
                });

                if (chanReply.Status != 0)
                {
                    Console.WriteLine($"CreateAIVoltageChan failed with status: {chanReply.Status}");
                    return;
                }
                Console.WriteLine($"Channel {physicalChannel} configured.");

                var timingReply = client.CfgSampClkTiming(new CfgSampClkTimingRequest
                {
                    Task = task,
                    Source = "",
                    Rate = sampleRate,
                    ActiveEdge = Edge1.Rising,
                    SampleMode = AcquisitionType.ContSamps,
                    SampsPerChan = (ulong)sampsPerRead * 10
                });

                if (timingReply.Status != 0)
                {
                    Console.WriteLine($"CfgSampClkTiming failed with status: {timingReply.Status}");
                    return;
                }
                Console.WriteLine($"Sample clock configured at {sampleRate} Hz.");

                client.StartTask(new StartTaskRequest { Task = task });
                Console.WriteLine("Task started. Press any key to stop...");
                Console.WriteLine();

                while (!Console.KeyAvailable)
                {
                    var readReply = client.ReadAnalogF64(new ReadAnalogF64Request
                    {
                        Task = task,
                        NumSampsPerChan = sampsPerRead,
                        Timeout = 5.0,
                        FillMode = GroupBy.GroupByScanNumber,
                        ArraySizeInSamps = (uint)sampsPerRead
                    });

                    if (readReply.Status != 0)
                    {
                        Console.WriteLine($"Read failed with status: {readReply.Status}");
                        break;
                    }

                    if (readReply.ReadArray.Count > 0)
                    {
                        double sum = 0;
                        foreach (var v in readReply.ReadArray) sum += v;
                        double avg = sum / readReply.ReadArray.Count;
                        Console.Write($"\r  ai0 = {avg,10:F6} V  ({readReply.SampsPerChanRead} samples)   ");
                    }
                }

                Console.ReadKey(intercept: true);
                Console.WriteLine();
                Console.WriteLine("Stopping...");

                client.StopTask(new StopTaskRequest { Task = task });
                Console.WriteLine("Task stopped.");
            }
            finally
            {
                client.ClearTask(new ClearTaskRequest { Task = task });
                Console.WriteLine("Task cleared.");
            }

            channel.ShutdownAsync().Wait();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void StartGrpcServerIfNeeded()
        {
            const string serviceName = "NI gRPC Server";
            try
            {
                using (var svc = new ServiceController(serviceName))
                {
                    if (svc.Status == ServiceControllerStatus.Running)
                    {
                        Console.WriteLine("NI gRPC Device Server already running.");
                        return;
                    }
                    Console.WriteLine($"Starting NI gRPC Device Server (status: {svc.Status})...");
                    svc.Start();
                    svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    Console.WriteLine("NI gRPC Device Server started.");
                    Thread.Sleep(1000);
                }
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Warning: NI gRPC Device Server service not found — assuming server is already running.");
            }
        }
    }
}
