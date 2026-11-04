using AIFitnessCoach.AI.Services;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class ProgressionEngineTests
{
    [Fact]
    public void ProgressionIsGradualAndRecoverySignalsReduceLoad()
    {
        var progression = new ProgressionEngine();
        Assert.Equal(3, progression.AdjustSets(2, false, false, false, true, maximumSets: 3));
        Assert.Equal(1, progression.AdjustSets(2, true, false, false, false, maximumSets: 3));
        Assert.Equal(3, progression.AdjustSets(8, false, false, false, false, maximumSets: 3));
        Assert.True(new RecoveryEngine().ShouldReduceLoad(5.5, 0.8, false));
        Assert.True(new RecoveryEngine().ShouldReduceLoad(7, 0.2, false));
        Assert.False(new RecoveryEngine().ShouldReduceLoad(7, 0.8, false));
    }
}
