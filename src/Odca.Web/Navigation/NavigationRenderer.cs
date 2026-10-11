using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Odca.Web.Navigation;

/// <summary>
/// Renderiza os itens do registro central como o mesmo markup usado anteriormente em
/// _Layout.cshtml (span.menu-caption + a.nav-item). O rótulo de recursos de plano é
/// produzido pelo MenuFeature da view (decorateLabel), preservando os sufixos
/// "(fora do plano)", "(bloqueada)" e "(limite atingido)".
/// </summary>
public static class NavigationRenderer
{
    public static IReadOnlyList<IHtmlContent> Render(
        IEnumerable<NavItem> items,
        NavContext ctx,
        IUrlHelper url,
        Func<string, string, string> decorateLabel)
    {
        var output = new List<IHtmlContent>();
        string? lastGroup = null;

        foreach (var item in items)
        {
            if (item.Available is not null && !item.Available(ctx))
            {
                continue;
            }

            if (item.Group is not null && item.Group != lastGroup)
            {
                lastGroup = item.Group;
                var caption = new TagBuilder("span");
                caption.Attributes["class"] = "menu-caption";
                if (!string.IsNullOrEmpty(item.GroupStyle))
                {
                    caption.Attributes["style"] = item.GroupStyle;
                }
                var groupKey = "group_" + item.Group.Trim().ToLowerInvariant().Replace(" ", "_").Replace("&", "").Replace("é", "e").Replace("ê", "e").Replace("á", "a").Replace("â", "a");
                var groupLabel = ctx.Localizer?.Invoke(groupKey) ?? item.Group;
                caption.InnerHtml.Append(groupLabel);
                output.Add(caption);
            }

            var link = new TagBuilder("a");
            link.Attributes["class"] = "nav-item";

            var active = item.IsActive is not null && item.IsActive(ctx);
            if (active)
            {
                link.Attributes["class"] = "nav-item active";
                link.Attributes["aria-current"] = "page";
            }

            var route = item.BuildRoute?.Invoke(ctx);
            var href = route is null
                ? url.Action(item.Action, item.Controller)
                : url.Action(item.Action, item.Controller, route);
            if (!string.IsNullOrEmpty(item.Fragment))
            {
                href += "#" + item.Fragment;
            }
            link.Attributes["href"] = href;
            if (!string.IsNullOrEmpty(item.AriaLabel))
            {
                link.Attributes["aria-label"] = item.AriaLabel;
            }

            var icon = new TagBuilder("span");
            icon.Attributes["aria-hidden"] = "true";
            icon.InnerHtml.Append(item.Icon);
            link.InnerHtml.AppendHtml(icon);

            var baseLabel = ctx.Localizer?.Invoke(item.Key) ?? item.Label;
            var label = item.FeatureCode is null ? baseLabel : decorateLabel(item.FeatureCode, baseLabel);
            var labelSpan = new TagBuilder("span");
            labelSpan.InnerHtml.Append(label);
            link.InnerHtml.AppendHtml(labelSpan);

            output.Add(link);
        }

        return output;
    }
}
