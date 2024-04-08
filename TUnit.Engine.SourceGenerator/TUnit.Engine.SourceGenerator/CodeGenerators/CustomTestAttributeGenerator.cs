﻿using System.Linq;
using Microsoft.CodeAnalysis;

namespace TUnit.Engine.SourceGenerator.CodeGenerators;

public static class CustomTestAttributeGenerator
{
    public static string WriteCustomAttributes(INamedTypeSymbol namedTypeSymbol, IMethodSymbol methodSymbol)
    {
        AttributeData[] attributes =
        [
            ..methodSymbol.GetAttributes(),
            ..namedTypeSymbol.GetAttributes()
        ];

        var fullyQualifiedClassName =
            namedTypeSymbol.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix);

        var applyToTestAttributes = attributes.Where(x => x.AttributeClass?.AllInterfaces.Any(i =>
            i.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
            == WellKnownFullyQualifiedClassNames.IApplicableTestAttribute) == true);

        var fullyQualifiedAttributes = applyToTestAttributes.Select(x => x.AttributeClass!.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix));
        
        var getAttributesCalls =
            fullyQualifiedAttributes
                .Select(x =>
                    $"..methodInfo.GetCustomAttributes<{x}>(), ..typeof({fullyQualifiedClassName}).GetCustomAttributes<{x}>()"
                );
        
        return string.Join(", ", getAttributesCalls);
    }
}