namespace Osynix.Ats.Domain;

public record LifecycleState(string Stage, string Interview, string Client);

public static class Lifecycle
{
    public static LifecycleState Synchronize(string stage, string interview, string client, string? previousClient = null)
    {
        if (!Choices.Stages.Contains(stage) || (interview.Length>0 && !Choices.Interviews.Contains(interview)) || (client.Length>0 && !Choices.ClientStatuses.Contains(client)))
            throw new InvalidOperationException("Choose valid recruitment, interview and client statuses.");
        stage = client switch { "Hired"=>"Hired", "Client Rejected"=>"Rejected", "Offer"=>"Offer Sent", "Client Interview"=>"Client Interview", _=>stage };
        if (client=="Client Interview" && previousClient!="Client Interview" && interview=="Completed") interview="Not Scheduled";
        if (IsInterview(stage) && interview.Length==0) interview="Not Scheduled";
        return new(stage, interview, client);
    }

    public static bool IsInterview(string stage) => stage is "Technical Interview" or "Final Interview" or "Client Interview";

    public static string Classify(Assessment? application, string talentStatus = "")
    {
        if (application is null) return talentStatus=="Placed" ? "Hired" : "";
        if (application.ClientStatus=="Hired" || application.Stage=="Hired" || talentStatus=="Placed") return "Hired";
        if (application.ClientStatus=="Client Rejected" || application.Stage=="Rejected") return "Rejected";
        if (application.ClientStatus is "Offer" or "Submitted" or "Under Review" or "Client Shortlisted" || application.Stage=="Offer Sent" || application.InterviewStatus=="Completed") return "Pending Decision";
        if (IsInterview(application.Stage) || application.ClientStatus=="Client Interview") return "In Interview";
        return application.Stage switch { "Hold"=>"On Hold", "Shortlisted"=>"Shortlisted", _=>"" };
    }

    // Translate statuses created by the earlier Blazor starter, preserving their intent.
    public static LifecycleState UpgradeLegacy(string stage, string interview, string client)
    {
        stage=stage switch { "Screened"=>"HR Screening", "Interview Scheduled" or "Interviewed"=>"Technical Interview", "Submitted to Client"=>"HR Screening", "Offer"=>"Offer Sent", _=>stage };
        if (interview=="Cancelled") interview="Reschedule Required";
        client=client switch { "Not Shared"=>"Not Submitted", "Shared"=>"Submitted", "Selected"=>"Hired", "Rejected"=>"Client Rejected", "On Hold"=>"Under Review", _=>client };
        return new(stage,interview,client);
    }
}
