﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Defines a contract that represents the file result of an HTTP endpoint.
/// </summary>
public interface IFileHttpResult
{
    /// <summary>
    /// Gets the Content-Type header for the response.
    /// </summary>
    string? ContentType { get; }

    /// <summary>
    /// Gets the file name that will be used in the Content-Disposition header of the response.
    /// </summary>
    string? FileDownloadName { get; }
}
