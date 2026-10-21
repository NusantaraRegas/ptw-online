namespace Ptw.Api.IntegrationTests;

/// <summary>Binary fixtures shared by the API scenarios.</summary>
internal static class TestAssets
{
    /// <summary>A small valid PNG accepted by the signature specimen upload.</summary>
    public static readonly byte[] SignaturePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAKAAAAA8CAYAAADha7EVAAACxElEQVR4nO2cQY6DMAxFaTQXyL1m07P0HD1LN3OvHKGjLpAQgpIQ2z82/0mommmxifPjxIH29n6/J0JQJJhnQihAgoYCJFAoQAKFAiRQKEAChQIkUChAAoUCJFAoQAKFAiRQ3Aow35/vb38TH/xMjqHo/JMiCY+C9IdLAZI4uBMgs1wsUrTiIJJAc6C2mAhwDtjndT407C8pr8dtCkhexHL9v0iYTMFagYsuvpnlYI4mQjEBagemxX60ToqMuyJkK/tFyoS5YvBEGmDJQ9AiBfxMO0ugAWaSAT8B0wxajW0N0WoWBLlRfFEGZYqy9tPOEusKv+Xaam1fZZmhmgG/BU2iwxAd8e26kRV+DpAFU4Tsp3W+lI1W+2VHfBGzYLLeFO7p0CP70h1U28azbcoOBoibKVh61KID2zrAWq/3bMVbgmXB5EUcloE/El9vZdq73VIU1tauM2CrOI4CJhlQicy01b6zItTa68tORZgsGtob3Jbze3y1TrtSWbmcsDPSVNwj/u5H8i0W/hacLao+n9l6eKC2WCmC8dvzO3L/NGVAxNQoEVBE1rZ4LrIYiG19SPuoFmDv6LXcWO1dk7aef7QetLzHmzvvuWuKTXQKtliDWPiQFP2WLW3xlR2/yP1YcQFqjYblmsXijkPN9kVPcGvFYDF1loWPUcS2xa3mR8p7p6gaW5I+jnxtvSflc8+2tI81Xu+zH2ZAi7XACF9cGmlbw4oR2nyYAS0yEyITafg78o3cIikDiK25Cva6u966j6fpe/2qSVn4Wh/ToKQrVr5WvizFNzOy2JoEqJn9RgjSCNdAGqpg1LN2Gr4oPmdTcIQOQ0yHJPD3glug+MYFJkBElUicrgEJ0eISUzC50G9E5/vzT9omGZPyevz22mAGJFC4BiRQmAEJFAqQQKEACRQKkEChAAkUCpBAoQAJFAqQTEj+AcBfDu2Urk5nAAAAAElFTkSuQmCC");

    /// <summary>The smallest byte sequence the upload signature check accepts as a PDF.</summary>
    public static readonly byte[] MinimalPdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n");
}
