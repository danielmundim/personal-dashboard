using Microsoft.Extensions.Options;

namespace Dashboard.Api.Infrastructure;

/// <summary>Resolves (and creates) the folder for data the dashboard owns: shopping list, photos.</summary>
public sealed class DataDirectory
{
    public DataDirectory(IOptions<DashboardOptions> options, IHostEnvironment env)
    {
        Root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.DataPath));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Sub(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
