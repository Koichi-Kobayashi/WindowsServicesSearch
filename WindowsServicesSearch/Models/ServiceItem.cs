// Copyright (c) 2026 Koichi Kobayashi
// Licensed under the MIT License.

namespace WindowsServicesSearch.Models;

internal sealed record ServiceItem(
    string DisplayName,
    string ServiceName,
    string Description,
    string Status,
    string StartType);
