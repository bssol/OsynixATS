namespace Osynix.Ats.Services;
public sealed class RecruitmentUpdates {
 public event Action? Changed;
 public void Publish(){if(Changed is null)return;foreach(Action callback in Changed.GetInvocationList())try{callback();}catch{ /* Subscribers may have disconnected. */ }}
}
