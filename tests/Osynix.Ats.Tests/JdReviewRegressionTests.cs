using Osynix.Ats.Domain;
using Osynix.Ats.Services;
using Xunit;

namespace Osynix.Ats.Tests;

public class JdReviewRegressionTests
{
    static Position AnalyzedPosition()
    {
        var p = new Position { ClientId = Guid.NewGuid(), Client = "Client", Title = "Role", JobDescriptionHtml = "<p>Lead the team</p>", JobDescription = "Lead the team", PublicJobDescription = "Lead the team", Criteria = [new() { Name = "Leadership", Weight = 100 }] };
        p.DraftAnalysisSignature = PositionCreationRules.SourceSignature(p);
        return p;
    }

    [Fact]
    public void DeletionAndUndoKeepAnalyzedDraftAvailableForCriteriaReview()
    {
        var p = AnalyzedPosition();
        var original = p.JobDescriptionHtml;
        Assert.True(PositionCreationRules.ApplyReviewedJd(p, "<p>Lead</p>"));
        Assert.Equal(PositionCreationRules.SourceSignature(p), p.DraftAnalysisSignature);
        Assert.True(PositionCreationRules.ApplyReviewedJd(p, original));
        Assert.Equal("Lead the team", p.JobDescription);
        Assert.Equal(p.JobDescription, p.PublicJobDescription);
        Assert.Equal(PositionCreationRules.SourceSignature(p), p.DraftAnalysisSignature);
        Assert.True(PositionCreationRules.CanOpenStep("criteria", p, false, true, true, false));
    }

    [Fact]
    public void FormattingDoesNotCountAsTextChangeAndPreservesCustomPublicCopy()
    {
        var p = AnalyzedPosition();
        p.PublicJobDescription = "Custom public description";
        Assert.False(PositionCreationRules.ApplyReviewedJd(p, "<p><strong>Lead the team</strong></p>"));
        Assert.Equal("Custom public description", p.PublicJobDescription);
        Assert.Equal(PositionCreationRules.SourceSignature(p), p.DraftAnalysisSignature);
    }

    [Fact]
    public void EditingTextDoesNotClearAStaleClientRequirementsChange()
    {
        var p = AnalyzedPosition();
        p.ClientNotes = "Changed requirements need analysis";
        PositionCreationRules.ApplyReviewedJd(p, "<p>Revised text</p>");
        Assert.NotEqual(PositionCreationRules.SourceSignature(p), p.DraftAnalysisSignature);
    }
}
