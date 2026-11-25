using System.IO;
using PlatformContracts = HyperWhisper.Platform.Abstractions;

namespace HyperWhisper.Services.Platform;

public sealed class WindowsGpuInfoProvider : PlatformContracts.IGpuInfoProvider
{
    public PlatformContracts.PlatformResult<PlatformContracts.GpuInfo?> GetBestGpu()
    {
        try
        {
            var gpu = GpuInfoService.GetBestGpu();
            return PlatformContracts.PlatformResult<PlatformContracts.GpuInfo?>.Success(
                gpu == null ? null : ToPlatform(gpu));
        }
        catch (Exception ex)
        {
            LoggingService.Error("WindowsGpuInfoProvider: detection failed", ex);
            return PlatformContracts.PlatformResult<PlatformContracts.GpuInfo?>.Failure(
                "gpu.detection_failed", "Windows could not detect GPU information.");
        }
    }

    public void ClearCache() => GpuInfoService.ClearCache();

    internal static PlatformContracts.GpuInfo ToPlatform(GpuInfoService.GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        return new PlatformContracts.GpuInfo
        {
            Name = gpu.Name,
            DedicatedMemoryBytes = gpu.DedicatedVramBytes,
            SharedMemoryBytes = gpu.SharedMemoryBytes,
            IsDiscrete = gpu.IsDiscrete,
            // The existing Windows detector reports DirectCompute suitability,
            // not Vulkan/CUDA capability. Do not infer unsupported metadata.
            SupportsVulkan = false,
            SupportsCuda = false
        };
    }
}

public sealed class WindowsAppPaths : PlatformContracts.IAppPaths
{
    public string DataDirectory => AppPaths.AppDataRoot;
    public string ConfigDirectory => AppPaths.AppDataRoot;
    public string CacheDirectory => AppPaths.Combine("Cache");
    public string StateDirectory => AppPaths.AppDataRoot;
    public string LogsDirectory => AppPaths.LogsDirectory;
    public string ModelsDirectory => AppPaths.ModelsDirectory;
    public string RecordingsDirectory => AppPaths.ProfileRecordingsDirectory;
    public string RuntimeDirectory => Path.Combine(AppContext.BaseDirectory, "runtimes");
    public string TemporaryDirectory => AppPaths.ProfileTempRecordingsDirectory;
}

public sealed class WindowsDeviceIdentityProvider : PlatformContracts.IDeviceIdentityProvider
{
    public PlatformContracts.PlatformResult<PlatformContracts.DeviceIdentity> GetDeviceIdentity()
    {
        return PlatformContracts.PlatformResult<PlatformContracts.DeviceIdentity>.Failure(
            "device_identity.disabled",
            "Hardware device identity is disabled in this build.");
    }
}
