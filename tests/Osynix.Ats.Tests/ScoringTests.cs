using Osynix.Ats.Domain;
using Xunit;
namespace Osynix.Ats.Tests;
public class ScoringTests
{
    static List<DecisionRule> Rules=>[new(){MustHaveFit="Pass",MinimumMatch=80,Decision="Shortlist"},new(){MustHaveFit="Partial",MinimumMatch=50,Decision="Consider"}];
    static List<Criterion> Criteria=>[new(){Name="Direct industry",Weight=60,Knockout=true},new(){Name="Leadership",Weight=40}];
    [Fact] public void FailOverridesHighWeightedScore()
    {var r=ScoringEngine.Calculate([new(){Name="Mandatory",Weight=10,Knockout=true},new(){Name="Other",Weight=90}],[new("Mandatory",2,8,"Weak",""),new("Other",10,10,"Strong","")],Rules);Assert.Equal(92,r.Match);Assert.Equal("Reject",r.Decision);}
    [Fact] public void MissingMustHaveRemainsInsufficientEvidence()
    {var r=ScoringEngine.Calculate(Criteria,[new("Leadership",10,10,"Evidence","")],Rules);Assert.Equal(40,r.Match);Assert.Equal("Insufficient Evidence",r.MustHave);Assert.Equal("Insufficient Evidence",r.Decision);}
    [Fact] public void WeightedScoringAndCoreFitMatchLegacy()
    {var r=ScoringEngine.Calculate(Criteria,[new("Direct industry",8,8,"",""),new("Leadership",6,6,"","")],Rules);Assert.Equal(72,r.Match);Assert.Equal(6,r.Core);Assert.Equal(7.2,r.Confidence);Assert.Equal("Hold",r.Decision);}
    [Fact] public void PartialUsesItsOwnThresholds()
    {var r=ScoringEngine.Calculate(Criteria,[new("Direct industry",4,8,"",""),new("Leadership",10,10,"","")],Rules);Assert.Equal(64,r.Match);Assert.Equal("Consider",r.Decision);}
    [Fact] public void AmbiguousDuplicateEvidenceDoesNotEarnCredit()
    {var r=ScoringEngine.Calculate(Criteria,[new("Direct industry",10,10,"",""),new("Direct industry",10,10,"","")],Rules);Assert.Equal(0,r.Match);}
    [Fact] public void InvalidWeightsBlockAssessment()
    {Assert.Throws<InvalidOperationException>(()=>ScoringEngine.Calculate([new(){Name="Test",Weight=90}],[],Rules));}
    [Fact] public void CandidateMessageAsksCompensationOnlyForShortlist()
    {Assert.Contains("current compensation",ScoringEngine.Message("Ali","Manager","Shortlist"));Assert.DoesNotContain("current compensation",ScoringEngine.Message("Ali","Manager","Reject"));Assert.EndsWith("Osynix Group",ScoringEngine.Message("Ali","Manager","Hold"));}
}
