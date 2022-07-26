// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Microsoft.JSInterop;

namespace Microsoft.AspNetCore.Components.WebAssembly.Hosting;

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = "This type loads resx files. We don't expect it's dependencies to be trimmed in the ordinary case.")]
#pragma warning disable CA1852 // Seal internal types
internal class WebAssemblyCultureProvider
#pragma warning restore CA1852 // Seal internal types
{
    internal const string GetSatelliteAssemblies = "window.Blazor._internal.getSatelliteAssemblies";
    internal const string ReadSatelliteAssemblies = "window.Blazor._internal.readSatelliteAssemblies";

    private readonly IJSUnmarshalledRuntime _invoker;

    // For unit testing.
    internal WebAssemblyCultureProvider(IJSUnmarshalledRuntime invoker, CultureInfo initialCulture, CultureInfo initialUICulture)
    {
        _invoker = invoker;
        InitialCulture = initialCulture;
        InitialUICulture = initialUICulture;
    }

    public static WebAssemblyCultureProvider? Instance { get; private set; }

    public CultureInfo InitialCulture { get; }

    public CultureInfo InitialUICulture { get; }

    internal static void Initialize()
    {
        Instance = new WebAssemblyCultureProvider(
            DefaultWebAssemblyJSRuntime.Instance,
            initialCulture: CultureInfo.CurrentCulture,
            initialUICulture: CultureInfo.CurrentUICulture);
    }

    public void ThrowIfCultureChangeIsUnsupported()
    {
        // With ICU sharding enabled, bootstrapping WebAssembly will download a ICU shard based on the browser language.
        // If the application author was to change the culture as part of their Program.MainAsync, we might have
        // incomplete icu data for their culture. We would like to flag this as an error and notify the author to
        // use the combined icu data file instead.
        //
        // The Initialize method is invoked as one of the first steps bootstrapping the app within WebAssemblyHostBuilder.CreateDefault.
        // It allows us to capture the initial .NET culture that is configured based on the browser language.
        // The current method is invoked as part of WebAssemblyHost.RunAsync i.e. after user code in Program.MainAsync has run
        // thus allows us to detect if the culture was changed by user code.
        if (Environment.GetEnvironmentVariable("__BLAZOR_SHARDED_ICU") == "1" &&
            ((!CultureInfo.CurrentCulture.Name.Equals(InitialCulture.Name, StringComparison.Ordinal) ||
              !CultureInfo.CurrentUICulture.Name.Equals(InitialUICulture.Name, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException("Blazor detected a change in the application's culture that is not supported with the current project configuration. " +
                "To change culture dynamically during startup, set <BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData> in the application's project file.");
        }
    }

    public virtual async ValueTask LoadCurrentCultureResourcesAsync()
    {
        var culturesToLoad = GetCultures(CultureInfo.CurrentCulture);

        if (culturesToLoad.Count == 0)
        {
            return;
        }

        // Now that we know the cultures we care about, let WebAssemblyResourceLoader (in JavaScript) load these
        // assemblies. We effectively want to resovle a Task<byte[][]> but there is no way to express this
        // using interop. We'll instead do this in two parts:
        // getSatelliteAssemblies resolves when all satellite assemblies to be loaded in .NET are fetched and available in memory.
#pragma warning disable CS0618 // Type or member is obsolete
        var count = (int)await _invoker.InvokeUnmarshalled<string[], object?, object?, Task<object>>(
            GetSatelliteAssemblies,
            culturesToLoad.ToArray(),
            null,
            null);

        if (count == 0)
        {
            return;
        }

        // readSatelliteAssemblies resolves the assembly bytes
        var assemblies = _invoker.InvokeUnmarshalled<object?, object?, object?, object[]>(
            ReadSatelliteAssemblies,
            null,
            null,
            null);
#pragma warning restore CS0618 // Type or member is obsolete

        for (var i = 0; i < assemblies.Length; i++)
        {
            using var stream = new MemoryStream((byte[])assemblies[i]);
            AssemblyLoadContext.Default.LoadFromStream(stream);
        }
    }

    internal static List<string> GetCultures(CultureInfo cultureInfo)
    {
        var culturesToLoad = new List<string>();

        // Once WASM is ready, we have to use .NET's assembly loading to load additional assemblies.
        // First calculate all possible cultures that the application might want to load. We do this by
        // starting from the current culture and walking up the graph of parents.
        // At the end of the the walk, we'll have a list of culture names that look like
        // [ "fr-FR", "fr" ]
        while (cultureInfo != null && cultureInfo != CultureInfo.InvariantCulture)
        {
            culturesToLoad.Add(cultureInfo.Name);

            if (cultureInfo.Parent == cultureInfo)
            {
                break;
            }

            cultureInfo = cultureInfo.Parent;
        }

        return culturesToLoad;
    }
}
