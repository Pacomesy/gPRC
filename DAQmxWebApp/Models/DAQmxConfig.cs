namespace DAQmxWebApp.Models;

public class DAQmxConfig
{
    public string ServerAddress { get; set; } = "host.docker.internal";
    public int ServerPort { get; set; } = 31763;
    public string SessionName { get; set; } = "DAQmx-WebApp";
    public string PhysicalChannel { get; set; } = "Dev1/ai0";
    public double MinVoltage { get; set; } = -10.0;
    public double MaxVoltage { get; set; } = 10.0;
    public double SampleRate { get; set; } = 1000.0;
    public int SampsPerRead { get; set; } = 100;
    public int BufferMultiplier { get; set; } = 10;

    public string GrpcAddress => $"http://{ServerAddress}:{ServerPort}";
}
