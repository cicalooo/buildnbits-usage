using System.Runtime.InteropServices;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Core.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace BuildnBits.Usage.Widgets;

[ComVisible(true)]
[Guid("8F3E6B21-4C9A-4D71-9E2B-7A11C0D4E8A1")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class WidgetProvider : IWidgetProvider
{
    private static readonly Dictionary<string, WidgetContext> Running = new();
    private static readonly UsageCache Cache = new();
    private static readonly UsageRefreshService Refresh = CreateRefresh();

    static WidgetProvider()
    {
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            {
                Running[info.WidgetContext.Id] = info.WidgetContext;
                Push(info.WidgetContext.Id, info.WidgetContext.DefinitionId);
            }
        }
        catch
        {
            // Host may call CreateWidget/Activate before COM is fully ready.
        }
    }

    private static UsageRefreshService CreateRefresh()
    {
        var service = new UsageRefreshService(cache: Cache);
        service.StateChanged += (_, _) => UpdateAll();
        return service;
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        Running[widgetContext.Id] = widgetContext;
        Push(widgetContext.Id, widgetContext.DefinitionId);
        _ = Refresh.RefreshNowAsync();
    }

    public void DeleteWidget(string widgetId, string customState) => Running.Remove(widgetId);

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        if (string.Equals(actionInvokedArgs.Verb, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            _ = Refresh.RefreshNowAsync();
            return;
        }

        if (string.Equals(actionInvokedArgs.Verb, "openApp", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "BuildnBits.Usage.Tray.exe",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Tray host may already be running.
            }
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var context = contextChangedArgs.WidgetContext;
        Running[context.Id] = context;
        Push(context.Id, context.DefinitionId);
    }

    public void Activate(WidgetContext widgetContext)
    {
        Running[widgetContext.Id] = widgetContext;
        Push(widgetContext.Id, widgetContext.DefinitionId);
        _ = Refresh.RefreshNowAsync();
    }

    public void Deactivate(string widgetId)
    {
        // Keep last payload; no Explorer hacks.
    }

    private static void UpdateAll()
    {
        foreach (var pair in Running.ToArray())
        {
            Push(pair.Key, pair.Value.DefinitionId);
        }
    }

    private static void Push(string widgetId, string definitionId)
    {
        var state = Refresh.Current;
        if (state.LastAttemptUtc is null)
        {
            state = Cache.Load();
        }
        var options = new WidgetUpdateRequestOptions(widgetId)
        {
            Template = UsageAdaptiveCard.TemplateJson(),
            Data = UsageAdaptiveCard.DataJson(state, DateTimeOffset.UtcNow),
            CustomState = definitionId
        };
        WidgetManager.GetDefault().UpdateWidget(options);
    }
}
