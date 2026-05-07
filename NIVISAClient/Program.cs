using System;
using System.ServiceProcess;
using System.Threading;
using Grpc.Core;
using NationalInstruments.Grpc.Device;
using NationalInstruments.Grpc.Visa;

namespace NIVISAClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var serverAddress = "localhost";
            var serverPort = "31763";
            var sessionName = "NI-VISA-Session";
            var resourceName = "ASRL1::INSTR";

            StartGrpcServerIfNeeded();

            Channel channel = new Channel(serverAddress + ":" + serverPort, ChannelCredentials.Insecure);
            var client = new Visa.VisaClient(channel);

            var openReply = client.Open(new OpenRequest
            {
                SessionName = sessionName,
                InstrumentDescriptor = resourceName,
                OpenTimeout = 5000
            });

            if (openReply.Status == 0)
            {
                Console.WriteLine("COM1 opened successfully.");
                Console.WriteLine();

                PrintSerialAttribute(client, openReply.Vi, VisaAttribute.AsrlBaud,      "Baud rate");
                PrintSerialAttribute(client, openReply.Vi, VisaAttribute.AsrlDataBits,  "Data bits");
                PrintSerialAttribute(client, openReply.Vi, VisaAttribute.AsrlParity,    "Parity");
                PrintSerialAttribute(client, openReply.Vi, VisaAttribute.AsrlStopBits,  "Stop bits");
                PrintSerialAttribute(client, openReply.Vi, VisaAttribute.AsrlFlowCntrl, "Flow control");

                Console.WriteLine();

                client.SetAttribute(new SetAttributeRequest
                {
                    Vi = openReply.Vi,
                    AttributeName = VisaAttribute.TermcharEn,
                    AttributeValue = new AttributeValueData { ValueBool = true }
                });

                client.SetAttribute(new SetAttributeRequest
                {
                    Vi = openReply.Vi,
                    AttributeName = VisaAttribute.Termchar,
                    AttributeValue = new AttributeValueData { ValueU8 = 0x0A }
                });

                // Read baud rate before change
                uint baudBefore = GetBaudRate(client, openReply.Vi);
                Console.WriteLine($"Baud rate before change: {baudBefore}");
                Console.WriteLine();

                // Prompt user for new baud rate
                uint newBaud = 0;
                while (newBaud == 0)
                {
                    Console.Write("Enter new baud rate: ");
                    string input = Console.ReadLine();
                    if (!uint.TryParse(input, out newBaud) || newBaud == 0)
                    {
                        Console.WriteLine("Invalid baud rate. Please enter a positive integer.");
                        newBaud = 0;
                    }
                }

                // Set new baud rate
                var setReply = client.SetAttribute(new SetAttributeRequest
                {
                    Vi = openReply.Vi,
                    AttributeName = VisaAttribute.AsrlBaud,
                    AttributeValue = new AttributeValueData { ValueU32 = newBaud }
                });

                if (setReply.Status != 0)
                    Console.WriteLine($"SetAttribute failed with status: {setReply.Status}");

                // Read baud rate after change
                uint baudAfter = GetBaudRate(client, openReply.Vi);
                Console.WriteLine($"Baud rate after  change: {baudAfter}");
                Console.WriteLine();

                client.Close(new CloseRequest { Vi = openReply.Vi });
                Console.WriteLine("COM1 closed.");
            }
            else
            {
                Console.WriteLine($"Open failed with status: {openReply.Status}");
            }

            channel.ShutdownAsync().Wait();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void StartGrpcServerIfNeeded()
        {
            const string serviceName = "nigrpcdeviceserver";
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

        static uint GetBaudRate(Visa.VisaClient client, NationalInstruments.Grpc.Device.Session vi)
        {
            var reply = client.GetAttribute(new GetAttributeRequest
            {
                Vi = vi,
                AttributeName = VisaAttribute.AsrlBaud
            });
            if (reply.Status != 0)
            {
                Console.WriteLine($"  GetAttribute(AsrlBaud) error (status {reply.Status})");
                return 0;
            }
            var val = reply.AttributeValue;
            if (val.HasValueU32) return val.ValueU32;
            if (val.HasValueU64) return (uint)val.ValueU64;
            return 0;
        }

        static void PrintSerialAttribute(Visa.VisaClient client, NationalInstruments.Grpc.Device.Session vi, VisaAttribute attribute, string label)
        {
            var reply = client.GetAttribute(new GetAttributeRequest
            {
                Vi = vi,
                AttributeName = attribute
            });

            if (reply.Status != 0)
            {
                Console.WriteLine($"  {label,-14}: error (status {reply.Status})");
                return;
            }

            var val = reply.AttributeValue;
            string display;
            if (val.HasValueU32)        display = FormatAttributeValue(attribute, (long)val.ValueU32);
            else if (val.HasValueI32)   display = FormatAttributeValue(attribute, val.ValueI32);
            else if (val.HasValueU64)   display = FormatAttributeValue(attribute, (long)val.ValueU64);
            else if (val.HasValueU16)   display = FormatAttributeValue(attribute, val.ValueU16);
            else if (val.HasValueU8)    display = FormatAttributeValue(attribute, val.ValueU8);
            else if (val.HasValueBool)  display = val.ValueBool.ToString();
            else if (val.HasValueString) display = val.ValueString;
            else                        display = "(unknown)";

            Console.WriteLine($"  {label,-14}: {display}");
        }

        static string FormatAttributeValue(VisaAttribute attribute, long value)
        {
            if (attribute == VisaAttribute.AsrlParity)
            {
                switch (value)
                {
                    case 0: return $"None ({value})";
                    case 1: return $"Odd ({value})";
                    case 2: return $"Even ({value})";
                    case 3: return $"Mark ({value})";
                    case 4: return $"Space ({value})";
                    default: return value.ToString();
                }
            }
            if (attribute == VisaAttribute.AsrlStopBits)
            {
                switch (value)
                {
                    case 10: return $"1 ({value})";
                    case 15: return $"1.5 ({value})";
                    case 20: return $"2 ({value})";
                    default: return value.ToString();
                }
            }
            if (attribute == VisaAttribute.AsrlFlowCntrl)
            {
                switch (value)
                {
                    case 0: return $"None ({value})";
                    case 1: return $"XON/XOFF ({value})";
                    case 2: return $"RTS/CTS ({value})";
                    case 4: return $"DTR/DSR ({value})";
                    default: return value.ToString();
                }
            }
            return value.ToString();
        }
    }
}
