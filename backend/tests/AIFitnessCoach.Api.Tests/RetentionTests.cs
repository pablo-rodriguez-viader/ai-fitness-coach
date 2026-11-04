using AIFitnessCoach.Application.UseCases.Analytics;
using Xunit;
using static AIFitnessCoach.Api.Tests.AnalyticsTestData;

namespace AIFitnessCoach.Api.Tests;

public sealed class RetentionTests
{
    private readonly RetentionAnalyzer analyzer = new();

    [Fact]
    public void ClassicRetentionCountsUsersActiveExactlyOnDayN()
    {
        var events = Sessions("a", 0, 1, 7, 30)        // retained D1, D7, D30
            .Concat(Sessions("b", 0, 2, 8))             // misses D1 and D7 exactly; active later
            .Concat(Sessions("c", 0))                   // never returns
            .ToArray();

        var report = analyzer.Analyze(events, Day(30));

        var d1 = report.Classic.Single(point => point.Day == 1);
        var d7 = report.Classic.Single(point => point.Day == 7);
        var d30 = report.Classic.Single(point => point.Day == 30);
        Assert.Equal((3, 1), (d1.EligibleUsers, d1.RetainedUsers));
        Assert.Equal(0.333, d1.Rate);
        Assert.Equal(1, d7.RetainedUsers);
        Assert.Equal(1, d30.RetainedUsers);
    }

    [Fact]
    public void RollingRetentionCountsUsersStillAroundOnOrAfterDayN()
    {
        var events = Sessions("a", 0, 1).Concat(Sessions("b", 0, 9)).Concat(Sessions("c", 0)).ToArray();

        var report = analyzer.Analyze(events, Day(10));

        var d7 = report.Rolling.Single(point => point.Day == 7);
        Assert.Equal(3, d7.EligibleUsers);
        Assert.Equal(1, d7.RetainedUsers); // only "b" came back on day 7 or later
    }

    [Fact]
    public void UsersAreOnlyEligibleOnceEnoughDaysHavePassed()
    {
        var events = Sessions("old", 0, 1).Concat(Sessions("new", 5)).ToArray();

        var report = analyzer.Analyze(events, Day(6));

        var d1 = report.Classic.Single(point => point.Day == 1);
        var d7 = report.Classic.Single(point => point.Day == 7);
        var d30 = report.Classic.Single(point => point.Day == 30);
        Assert.Equal(2, d1.EligibleUsers);
        Assert.Equal(0, d7.EligibleUsers);
        Assert.Null(d7.Rate);
        Assert.Null(d30.Rate);
    }

    [Fact]
    public void CohortsAreGroupedByFirstActiveDay()
    {
        var events = Sessions("a", 0, 1).Concat(Sessions("b", 0)).Concat(Sessions("c", 3, 4)).ToArray();

        var report = analyzer.Analyze(events, Day(10));

        Assert.Equal([Day(0), Day(3)], report.Cohorts.Select(cohort => cohort.CohortDate));
        Assert.Equal(2, report.Cohorts[0].Users);
        Assert.Equal(0.5, report.Cohorts[0].Points.Single(point => point.Day == 1).Rate);
        Assert.Equal(1.0, report.Cohorts[1].Points.Single(point => point.Day == 1).Rate);
    }

    [Fact]
    public void ActivityAfterTheReportDateIsIgnoredAndEmptyInputIsSafe()
    {
        var events = Sessions("a", 0, 1).ToArray();

        var beforeReturn = analyzer.Analyze(events, Day(0));
        var empty = analyzer.Analyze([], Day(0));

        Assert.All(beforeReturn.Classic, point => Assert.Null(point.Rate));
        Assert.Empty(empty.Cohorts);
        Assert.All(empty.Classic, point => Assert.Equal(0, point.EligibleUsers));
    }
}
