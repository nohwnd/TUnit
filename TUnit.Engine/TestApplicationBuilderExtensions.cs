﻿using System.Reflection;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Capabilities.TestFramework;
using Microsoft.Testing.Platform.MSBuild;
using TUnit.Engine;

namespace TUnit.TestAdapter;

public static class TestApplicationBuilderExtensions
{
    public static void AddTUnit(this ITestApplicationBuilder testApplicationBuilder, Func<IEnumerable<Assembly>> getTestAssemblies)
    {
        TUnitExtension extension = new();
        // testApplicationBuilder.AddRunSettingsService(extension);
        // testApplicationBuilder.AddTestCaseFilterService(extension);
        testApplicationBuilder.AddMSBuild();
        testApplicationBuilder.RegisterTestFramework(
            _ => new TestFrameworkCapabilities(),
            (capabilities, serviceProvider) =>
            {
                return new TUnitTestFramework(extension, getTestAssemblies, serviceProvider, capabilities);
            });
    }
}