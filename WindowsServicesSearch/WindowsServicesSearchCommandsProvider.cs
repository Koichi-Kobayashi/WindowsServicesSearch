// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WindowsServicesSearch.Resources;

namespace WindowsServicesSearch;

public partial class WindowsServicesSearchCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public WindowsServicesSearchCommandsProvider()
    {
        DisplayName = Strings.Get("Extension.DisplayName");
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new ListItem(new WindowsServicesSearchPage())
            {
                Title = Strings.Get("Command.SearchServices.Title"),
                Subtitle = Strings.Get("Command.SearchServices.Subtitle"),
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
