// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Linq;
using WindowsServicesSearch.Commands;
using WindowsServicesSearch.Models;
using WindowsServicesSearch.Resources;

namespace WindowsServicesSearch;

internal sealed partial class WindowsServicesSearchPage : DynamicListPage
{
    private readonly ServiceCatalog _catalog = new();
    private string _query = string.Empty;

    public WindowsServicesSearchPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = Strings.Get("Extension.DisplayName");
        Name = Strings.Get("Page.Search.Name");
        PlaceholderText = Strings.Get("Page.Search.Placeholder");
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _query = newSearch;
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        return _catalog.Search(_query).Select(CreateListItem).ToArray();
    }

    private static IListItem CreateListItem(ServiceItem service)
    {
        return new ListItem(new OpenServiceCommand(service))
        {
            Icon = IconHelpers.FromRelativePath("Assets\\ServiceIcon-64.png"),
            Title = service.DisplayName,
            Subtitle = service.Description,
            Details = new Details
            {
                Title = service.DisplayName,
                Body = $"**{Strings.Get("Details.ServiceName")}:** {service.ServiceName}\n\n**{Strings.Get("Details.Status")}:** {service.Status}\n\n**{Strings.Get("Details.StartupType")}:** {service.StartType}\n\n{service.Description}",
            },
        };
    }
}
