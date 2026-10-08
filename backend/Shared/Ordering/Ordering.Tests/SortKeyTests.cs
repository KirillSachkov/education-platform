using CSharpFunctionalExtensions;
using SharedKernel;

namespace Ordering.Tests;

public class SortKeyTests
{
    [Fact]
    public void Initial_ReturnsA0()
    {
        SortKey key = SortKey.Initial();
        Assert.Equal("a0", key.Value);
    }

    [Fact]
    public void After_ReturnsKeyGreaterThanPrevious()
    {
        SortKey first = SortKey.Initial();
        SortKey second = SortKey.After(first);

        Assert.True(
            string.CompareOrdinal(second.Value, first.Value) > 0,
            $"After key '{second.Value}' should be > '{first.Value}'");
    }

    [Fact]
    public void Before_ReturnsKeyLessThanNext()
    {
        SortKey first = SortKey.Initial();
        SortKey before = SortKey.Before(first);

        Assert.True(
            string.CompareOrdinal(before.Value, first.Value) < 0,
            $"Before key '{before.Value}' should be < '{first.Value}'");
    }

    [Fact]
    public void Between_ReturnsKeyInRange()
    {
        SortKey a = SortKey.Initial();
        SortKey b = SortKey.After(a);

        Result<SortKey, Error> result = SortKey.Between(a, b);

        Assert.True(result.IsSuccess);
        Assert.True(string.CompareOrdinal(result.Value.Value, a.Value) > 0);
        Assert.True(string.CompareOrdinal(result.Value.Value, b.Value) < 0);
    }

    [Fact]
    public void Between_NullBefore_ReturnsKeyBeforeAfter()
    {
        SortKey b = SortKey.Initial();

        Result<SortKey, Error> result = SortKey.Between(null, b);

        Assert.True(result.IsSuccess);
        Assert.True(string.CompareOrdinal(result.Value.Value, b.Value) < 0);
    }

    [Fact]
    public void Between_NullAfter_ReturnsKeyAfterBefore()
    {
        SortKey a = SortKey.Initial();

        Result<SortKey, Error> result = SortKey.Between(a, null);

        Assert.True(result.IsSuccess);
        Assert.True(string.CompareOrdinal(result.Value.Value, a.Value) > 0);
    }

    [Fact]
    public void Between_BothNull_ReturnsInitialKey()
    {
        Result<SortKey, Error> result = SortKey.Between(null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("a0", result.Value.Value);
    }

    [Fact]
    public void Between_ReversedOrder_ReturnsFailure()
    {
        SortKey a = SortKey.Initial();
        SortKey b = SortKey.After(a);

        Result<SortKey, Error> result = SortKey.Between(b, a);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_ValidKey_ReturnsSuccess()
    {
        Result<SortKey, Error> result = SortKey.Create("a0");
        Assert.True(result.IsSuccess);
        Assert.Equal("a0", result.Value.Value);
    }

    [Fact]
    public void Create_EmptyString_ReturnsFailure()
    {
        Result<SortKey, Error> result = SortKey.Create(string.Empty);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void CreateBatch_GeneratesCorrectCountSorted()
    {
        SortKey[] keys = SortKey.CreateBatch(null, null, 5);

        Assert.Equal(5, keys.Length);

        string[] values = keys.Select(k => k.Value).ToArray();
        string[] sorted = values.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(values, sorted);
    }

    [Fact]
    public void IndependentScopes_SameKeysNoConflict()
    {
        // Simulate two independent scopes generating keys
        SortKey scopeAFirst = SortKey.Initial();
        SortKey scopeBFirst = SortKey.Initial();

        // Same key value — no conflict because scope is external
        Assert.Equal(scopeAFirst.Value, scopeBFirst.Value);

        SortKey scopeASecond = SortKey.After(scopeAFirst);
        SortKey scopeBSecond = SortKey.After(scopeBFirst);

        Assert.Equal(scopeASecond.Value, scopeBSecond.Value);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        SortKey key = SortKey.Initial();
        Assert.Equal(key.Value, key.ToString());
    }
}