using System;
using Grpc.Core;
using NationalInstruments.Grpc.DCPower;

namespace NIDCPowerClient
{
    class Program
    {
        public static void Main(string[] args)
        {
            var server_address = "localhost";
            var server_port = "31763";
            var session_name = "NI-DCPower-Session";

            // Resource name, channel name, and options for a simulated 4147 client.
            var resource = "SimulatedDCPower";
            var options = "Simulate=1,DriverSetup=Model:4147;BoardType:PXIe";
            var channels = "0";

            Channel channel = new Channel(server_address + ":" + server_port, ChannelCredentials.Insecure);

            var client = new NiDCPower.NiDCPowerClient(channel);

            var initialize_reply = client.InitializeWithChannels(new InitializeWithChannelsRequest
            {
                SessionName = session_name,
                ResourceName = resource,
                Channels = channels,
                Reset = false,
                OptionString = options
            });
            var vi = initialize_reply.Vi;
            if (initialize_reply.Status == 0)
            {
                Console.WriteLine("Initialization was successful.");
            }
            else
            {
                Console.WriteLine($"Initialization was not successful. Status is {initialize_reply.Status}");
            }

            client.Close(new CloseRequest
            {
                Vi = vi
            });

            channel.ShutdownAsync().Wait();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}