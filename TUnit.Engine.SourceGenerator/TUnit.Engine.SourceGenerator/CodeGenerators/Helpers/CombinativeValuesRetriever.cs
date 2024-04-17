﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using TUnit.Engine.SourceGenerator.Extensions;
using TUnit.Engine.SourceGenerator.Models;

namespace TUnit.Engine.SourceGenerator.CodeGenerators.Helpers;

internal static class CombinativeValuesRetriever
{
    // We return a List of a List. Inner List is for each test.
    public static IEnumerable<IEnumerable<Argument>> Parse(IMethodSymbol methodSymbol, AttributeData[] methodAndClassAttributes)
    {
        var combinativeValuesAttributes = methodSymbol.Parameters
            .Select(x => x.GetAttributes().FirstOrDefault(x => x.GetFullyQualifiedAttributeTypeName()
                                                               == WellKnownFullyQualifiedClassNames.CombinativeValuesAttribute.WithGlobalPrefix))
            .OfType<AttributeData>()
            .ToList();
        
        var mappedToConstructorArrays = combinativeValuesAttributes
            .Select(x => x.ConstructorArguments.First().Values);

        return GetCombinativeArgumentsList(mappedToConstructorArrays)
            .Select(x =>
                MapToArgumentEnumerable(x, methodAndClassAttributes)
            );
    }

    private static IEnumerable<Argument> MapToArgumentEnumerable(IEnumerable<TypedConstant> x, AttributeData[] methodAndClassAttributes)
    {
        return x.Select(y =>
            new Argument(y.Type!.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix),
                TypedConstantParser.GetTypedConstantValue(y)))
            .WithTimeoutArgument(methodAndClassAttributes);
    }

    private static readonly IEnumerable<IEnumerable<TypedConstant>> Seed = new[] { Enumerable.Empty<TypedConstant>() };
    
    private static IEnumerable<IEnumerable<TypedConstant>> GetCombinativeArgumentsList(IEnumerable<ImmutableArray<TypedConstant>> elements)
    {
        return elements.Aggregate(Seed, (accumulator, enumerable)
            => accumulator.SelectMany(x => enumerable.Select(x.Append)));
    }
}