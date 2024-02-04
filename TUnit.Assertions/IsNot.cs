﻿using TUnit.Assertions.AssertConditions;
using TUnit.Assertions.AssertConditions.Generic;
using TUnit.Assertions.AssertConditions.Operators;

namespace TUnit.Assertions;

public class IsNot<TActual, TAnd, TOr> : NotConnector<TActual, TAnd, TOr>
    where TAnd : And<TActual, TAnd, TOr>, IAnd<TAnd, TActual, TAnd, TOr>
    where TOr : Or<TActual, TAnd, TOr>, IOr<TOr, TActual, TAnd, TOr>
{
    protected internal AssertionBuilder<TActual> AssertionBuilder { get; }
    
    public IsNot(AssertionBuilder<TActual> assertionBuilder, ConnectorType connectorType, BaseAssertCondition<TActual, TAnd, TOr>? otherAssertCondition) : base(connectorType, otherAssertCondition)
    {
        AssertionBuilder = assertionBuilder;
    }
    
    public BaseAssertCondition<TActual, TAnd, TOr> EqualTo(TActual expected) => Invert(new EqualsAssertCondition<TActual, TAnd, TOr>(AssertionBuilder, expected),
        (actual, exception) => $"Expected {actual} to equal {expected}");

    public BaseAssertCondition<TActual, TAnd, TOr> Null => Invert(new NullAssertCondition<TActual, TAnd, TOr>(AssertionBuilder),
        (actual, exception) => $"Expected {actual} to be null");

    public BaseAssertCondition<TActual, TAnd, TOr> TypeOf<TExpected>() => Invert(new TypeOfAssertCondition<TActual, TExpected, TAnd, TOr>(AssertionBuilder),
        (actual, exception) => $"Expected {actual} to not be of type {typeof(TExpected)}");
}