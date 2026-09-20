// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsServicesSearch.Models;

internal sealed class ServiceCatalog
{
    private readonly IReadOnlyList<ServiceItem> _services = ServiceManagerReader.ReadServices();

    public IEnumerable<ServiceItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _services;
        }

        return _services.Where(service =>
            service.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            service.ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            service.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }
}
