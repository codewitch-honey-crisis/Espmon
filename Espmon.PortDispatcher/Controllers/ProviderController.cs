using HWKit;
namespace Espmon;

public abstract class ProviderController : ControllerBase
{
    public PortController Parent { get; }
    protected ProviderController(PortController parent) : base(parent)
    {
        Parent = parent;
    }
    public abstract string Identifier { get; }
    public abstract string Name { get ; }
    public abstract string Description { get; }
    public bool IsStarted { get; private set; }

    public string[] Paths { 
        get {
            if(!IsStarted)
            {
                return [];
            }
            var query = Parent.Evaluate(new HardwareInfoMatchExpression());
            var result = new List<string>();
            foreach(var item in query)
            {
                if (item.Path != null && item.Path.StartsWith($"/{Identifier}/",StringComparison.Ordinal))
                {
                    result.Add(item.Path);
                }
            }
            return result.ToArray();
        } 
    }

    protected abstract void OnStart();
    protected abstract void OnStop();

    public void Start()
    {
        if (IsStarted) return;
        OnStart();
        UpdateProperty(nameof(IsStarted), () => IsStarted = true);
    }
    public void Stop()
    {
        if (!IsStarted) return;
        OnStop();
        UpdateProperty(nameof(IsStarted), () => IsStarted = false);
    }

}
