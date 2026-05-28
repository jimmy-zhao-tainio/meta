using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class InstanceWindowProviderTests
{
    [Fact]
    public void GetWindow_ReturnsOrderedPage()
    {
        var provider = new InstanceWindowProvider();
        var instance = new GenericInstance();
        var rows = instance.GetOrCreateEntityRecords("Thing");
        rows.Add(new GenericRecord { Id = "3" });
        rows.Add(new GenericRecord { Id = "1" });
        rows.Add(new GenericRecord { Id = "2" });

        var page = provider.GetWindow(instance, "Thing", offset: 1, pageSize: 2);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(1, page.Offset);
        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(["2", "3"], page.Rows.Select(row => row.Id).ToArray());
    }

    [Fact]
    public void GetWindow_ClampsOffsetPastEnd()
    {
        var provider = new InstanceWindowProvider();
        var instance = new GenericInstance();
        var rows = instance.GetOrCreateEntityRecords("Thing");
        rows.Add(new GenericRecord { Id = "1" });
        rows.Add(new GenericRecord { Id = "2" });
        rows.Add(new GenericRecord { Id = "3" });

        var page = provider.GetWindow(instance, "Thing", offset: 999, pageSize: 2);

        Assert.Equal(1, page.Offset);
        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(["2", "3"], page.Rows.Select(row => row.Id).ToArray());
    }
}


