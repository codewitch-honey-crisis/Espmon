using HWKit;

using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Espmon;

public sealed class LocalProviderEntry
{
    public LocalProviderController Provider { get; }
    public bool IsStarted { get; }

    public LocalProviderEntry(LocalProviderController provider, bool isStarted)
    {
        Provider = provider;
        IsStarted = isStarted;
    }
}
[SupportedOSPlatform("windows")]
public sealed class LocalProviderController : ProviderController
{
    public IHardwareInfoProvider Provider { get; }
    public override string Name { get; }
    public override string Identifier { get; }
    public override string Description { get; }

    public LocalProviderController(PortController parent, IHardwareInfoProvider provider) : base(parent)
    {
        ArgumentNullException.ThrowIfNull(provider, nameof(provider));
        Provider = provider;
        Name = provider.DisplayName;
        Identifier = provider.Identifier;
        Description = provider.Description;
    }
    protected override void OnStart()
    {
        Provider.Start();
    }

    protected override void OnStop()
    {
        Provider.Stop();
    }

    public static LocalProviderEntry[] FromJson(LocalPortController parent, JsonArray json)
    {
        var basePath = AppContext.BaseDirectory;

        var result = new LocalProviderEntry[json.Count];
        for (var i = 0; i < result.Length; ++i)
        {
            if (json[i] is JsonObject obj)
            {
                if (obj.TryGetValue("type", out var strObj))
                {
                    if (strObj is string str)
                    {
                        var idx = str.IndexOf(',');
                        if(idx>-1)
                        {
                            var asm = str.Substring(idx + 1).Trim();
                            if(!asm.StartsWith("HWKit.dll",StringComparison.OrdinalIgnoreCase))
                            {
                                throw new NotSupportedException("The providers cannot be external");
                            }
                            str = str.Substring(0, idx).TrimEnd();
                        }
                        IHardwareInfoProvider? prov = null;
                        switch(str)
                        {
                            case "HWKit.Win32Provider":
                                prov = new Win32Provider();
                                break;
                            case "HWKit.CoreTempCpuProvider":
                                prov = new CoreTempCpuProvider();
                                break;
                            case "HWKit.DxgiProvider":
                                prov = new DxgiProvider();
                                break;
                            case "HWKit.AmdAdlGpuProvider":
                                prov = new AmdAdlGpuProvider();
                                break;
                            case "HWKit.NvidiaNvmlGpuProvider":
                                prov = new NvidiaNvmlGpuProvider();
                                break;
                            default:
                                throw new NotSupportedException($"The type {str} cannot be dynamically loaded");
                        }
                        if (prov == null)
                        {
                            throw new ArgumentException($"Entry {str} could not be resolved.", nameof(json));
                        }
                        var provider = new LocalProviderController(parent, prov);
                        bool isStarted = false;
                        if (obj.TryGetValue("is_started", out var isObj))
                        {
                            if (isObj is bool isBool)
                            {
                                isStarted = isBool;
                            }
                            else
                            {
                                throw new ArgumentException("Providers contains a provider descriptor entry with an invalid is_started value", nameof(json));
                            }
                        }
                        result[i] = new LocalProviderEntry(provider, isStarted);
                        
                    }
                    else
                    {
                        throw new ArgumentException($"Provider descriptor entry could not be found", nameof(json));
                    }
                }
                else
                {
                    throw new ArgumentException("Providers contains a provider descriptor entry without a type", nameof(json));
                }
            }
            else if (null == json[i])
            {
                throw new ArgumentException("Providers contains a null provider descriptor entry", nameof(json));
            }
            else
            {
                throw new ArgumentException("Providers contains an invalid provider descriptor entry", nameof(json));
            }
        }
        return result;
    }
}

