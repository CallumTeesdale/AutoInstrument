using AutoInstrument;

namespace SampleApp.Services;

public class HashService
{
    [Instrument(Name = "yak.compute_hash", Kind = 0)]
    public string ComputeYakHash(string input)
    {
        return Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(input));
    }
}
