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
        static void Main()
        {
            var serverAddress = "localhost";
            var serverPort = "31763";
            var sessionName = "NI-VISA-Session";
            var resourceName = "ASRL1::INSTR";

            StartGrpcServerIfNeeded();

            Channel channel = new Channel(serverAddress + ":" + serverPort, ChannelCredentials.Insecure);
            var client = new Visa.VisaClient(channel);

            var findReply = client.FindRsrc(new FindRsrcRequest { Expression = "GPIB0::?*::INSTR" });
            if (findReply.Status != 0)
            {
                Console.WriteLine($"FindRsrc failed with status: {findReply.Status}");
            }
            else if (findReply.InstrumentDescriptor.Count == 0)
            {
                Console.WriteLine("No instruments found on GPIB0.");
            }
            else
            {
                Console.WriteLine($"Found {findReply.InstrumentDescriptor.Count} instrument(s) on GPIB0:");
                foreach (var descriptor in findReply.InstrumentDescriptor)
                    Console.WriteLine($"  {descriptor}");
            }
            Console.WriteLine();

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
                    AttributeName = VisaAttribute.SendEndEn,
                    AttributeValue = new AttributeValueData { ValueBool = false }
                });

                client.SetAttribute(new SetAttributeRequest
                {
                    Vi = openReply.Vi,
                    AttributeName = VisaAttribute.Termchar,
                    AttributeValue = new AttributeValueData { ValueU8 = 0x0A }
                });

                Console.Write("Enter string to send: ");
                string message = Console.ReadLine();

                var writeReply = client.Write(new WriteRequest
                {
                    Vi = openReply.Vi,
                    Buffer = Google.Protobuf.ByteString.CopyFromUtf8(message + "\n")
                });

                if (writeReply.Status != 0)
                {
                    Console.WriteLine($"Write failed with status: {writeReply.Status}");
                }
                else
                {
                    Console.WriteLine($"Sent {writeReply.ReturnCount} byte(s).");

                    var readReply = client.Read(new ReadRequest
                    {
                        Vi = openReply.Vi,
                        Count = 4096
                    });

                    if (readReply.Status != 0)
                        Console.WriteLine($"Read failed with status: {readReply.Status}");
                    else
                        Console.WriteLine($"Response: {readReply.Buffer.ToStringUtf8()}");
                }

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
