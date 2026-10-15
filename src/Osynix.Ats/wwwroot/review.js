(() => {
 const $=id=>document.getElementById(id);let rows=[],opened=null,busy=false,stopped=false,lastPayload="";
 const text=(tag,value,cls)=>{const e=document.createElement(tag);e.textContent=value??"";if(cls)e.className=cls;return e;};
 function message(value,error=false){$("notice").textContent=value;$("notice").className=error?"error":"";}
 function when(value,zone){if(!value)return "Not Provided";try{return new Intl.DateTimeFormat(undefined,{dateStyle:"full",timeStyle:"short",timeZone:zone||"UTC"}).format(new Date(value.endsWith("Z")?value:value+"Z"))+" · "+(zone||"UTC");}catch{return value+" UTC";}}
 function localValue(value,zone){if(!value)return "";const parts=new Intl.DateTimeFormat("en-CA",{timeZone:zone||"UTC",year:"numeric",month:"2-digit",day:"2-digit",hour:"2-digit",minute:"2-digit",hourCycle:"h23"}).formatToParts(new Date(value.endsWith("Z")?value:value+"Z"));const p=Object.fromEntries(parts.map(x=>[x.type,x.value]));return p.year+"-"+p.month+"-"+p.day+"T"+p.hour+":"+p.minute;}
 function render(){
  const pid=$("position").value;const selected=rows.filter(r=>r.positionId===pid);$("profiles").replaceChildren();$("interviews").replaceChildren();
  $("count").textContent=selected.length+" Submitted Profiles";
  $("empty").textContent=selected.length?"":"No active profiles have been shared for this position.";
  for(const r of selected){
   const tr=document.createElement("tr"),td=document.createElement("td"),button=text("button",r.candidateName,"candidate-link text-button");button.type="button";button.onclick=()=>open(r);td.append(button);tr.append(td);
   for(const value of [r.matchPercent==null?"Unavailable":r.matchPercent+"%",r.decision,r.stage])tr.append(text("td",value));
   const feedback=document.createElement("td"),review=text("button",r.feedback,"secondary");review.type="button";review.onclick=()=>open(r);feedback.append(review);tr.append(feedback);$("profiles").append(tr);
   if(r.availabilityStartUtc||r.interviewStatus==="Scheduled"){
    const item=text("div",null,"review-interview");item.append(text("b",r.candidateName),text("p",r.interviewStatus==="Scheduled"?"Interview Scheduled — Contact Your Recruiter For Confirmed Joining Details":"Proposed: "+when(r.availabilityStartUtc,r.timeZone)),text("small",r.interviewMode+" · "+r.durationMinutes+" Minutes"+(r.interviewLocation?" · "+r.interviewLocation:"")));$("interviews").append(item);
   }
  }
  if(!$("interviews").children.length)$("interviews").append(text("p","No interview updates yet.","empty"));
  if(pid&&selected.length){$("comparison").href="/review/comparison?position="+encodeURIComponent(pid);$("pdf").href="/review/report?position="+encodeURIComponent(pid)+"&format=pdf";}else{$("comparison").removeAttribute("href");$("pdf").removeAttribute("href");}
 }
 async function load(){
  if(stopped)return;try{const res=await fetch("/review/data",{cache:"no-store"});if(res.status===401){stopped=true;rows=[];render();message("Your link is invalid, expired, or revoked. Ask your Recruiter for a new review link.",true);return;}if(!res.ok)throw Error("Review updates are temporarily unavailable.");
   const data=await res.json();const stamp=JSON.stringify(data);if(stamp===lastPayload)return;lastPayload=stamp;rows=data.rows;const old=$("position").value||new URLSearchParams(location.search).get("position");const positions=[...new Map(rows.map(r=>[r.positionId,r])).values()];$("position").replaceChildren(text("option","Choose Position"));$("position").firstChild.value="";
   positions.forEach(r=>{const o=text("option","#"+r.reference+" — "+r.positionTitle);o.value=r.positionId;$("position").append(o);});
   $("position").value=positions.some(r=>r.positionId===old)?old:positions.length===1?positions[0].positionId:"";
   $("welcome").textContent=data.name?data.name+" — Profile Review":"Review Submitted Profiles";render();
   const requested=new URLSearchParams(location.search).get("profile");
   if(requested&&!opened){const row=rows.find(r=>r.id===requested);if(row){history.replaceState(null,"","/review?position="+encodeURIComponent(row.positionId));open(row);}}
  }catch(e){message(e.message,true);}
 }
 function open(r){
  opened={...r};$("profile-name").textContent=r.candidateName;$("report").src="/review/report?submission="+encodeURIComponent(r.id);$("single-pdf").href=$("report").src+"&format=pdf";
  $("status").value=r.feedback==="Awaiting Feedback"?"":r.feedback;$("remarks").value=r.remarks||"";$("zone").value=r.timeZone||Intl.DateTimeFormat().resolvedOptions().timeZone||"UTC";$("mode").value=r.interviewMode||"";$("start").value=localValue(r.availabilityStartUtc,r.timeZone);$("duration").value=r.durationMinutes||30;$("location").value=r.interviewLocation||"";$("form-notice").textContent="";$("day").textContent="";
  $("submit").disabled=r.stage==="Hired"||r.clientStatus==="Hired"||r.stage==="Offer Sent";$("profile").showModal();
 }
 $("position").onchange=render;$("refresh").onclick=()=>{stopped=false;lastPayload="";load();};$("close-profile").onclick=()=>{$("profile").close();$("report").removeAttribute("src");opened=null;};
 $("start").onchange=()=>{$("day").textContent=$("start").value?new Date($("start").value).toLocaleDateString(undefined,{weekday:"long",day:"numeric",month:"long",year:"numeric"}):"";};
 $("feedback").onsubmit=async e=>{
  e.preventDefault();if(busy||!opened)return;busy=true;$("submit").disabled=true;$("form-notice").textContent="";
  try{const response=await fetch("/review/feedback",{method:"POST",headers:{"Content-Type":"application/json","RequestVerificationToken":$("csrf").querySelector("input").value},body:JSON.stringify({submissionId:opened.id,revision:opened.revision,applicationRevision:opened.applicationRevision,status:$("status").value,remarks:$("remarks").value,localStart:$("start").value||null,timeZone:$("zone").value,mode:$("mode").value,durationMinutes:Number($("duration").value),location:$("location").value})});const data=await response.json().catch(()=>({message:"Your session expired. Open your review link again."}));if(!response.ok)throw Error(data.message);$("profile").close();opened=null;message(data.message);await load();}
  catch(err){$("form-notice").textContent=err.message;$("form-notice").className="error";}finally{busy=false;$("submit").disabled=false;}
 };
 load();setInterval(()=>{if(document.visibilityState==="visible"&&!busy)load();},3000);
})();
