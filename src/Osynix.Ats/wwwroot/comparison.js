(() => {
 const $=id=>document.getElementById(id),pid=new URLSearchParams(location.search).get("position");
 let selectedId=null,submitted=null,lastPayload="";
 const make=(tag,value)=>{const e=document.createElement(tag);e.textContent=value??"";return e;};
 if(pid){$("back-review").href="/review?position="+encodeURIComponent(pid);$("download-comparison").href="/review/report?position="+encodeURIComponent(pid)+"&format=pdf";}
 function show(row,scroll=true){
  selectedId=row.id;submitted=row.submittedUtc;$("assessment-name").textContent=row.candidateName+" — Assessment Results";$("assessment-hint").hidden=true;
  $("comparison-report").hidden=false;$("comparison-report").src="/review/report?submission="+encodeURIComponent(row.id);
  $("assessment-feedback").hidden=false;$("assessment-feedback").href="/review?position="+encodeURIComponent(pid)+"&profile="+encodeURIComponent(row.id);
  for(const tr of $("comparison-rows").children)tr.classList.toggle("comparison-selected",tr.dataset.id===row.id);
  if(scroll)$("comparison-detail").scrollIntoView({behavior:"smooth",block:"start"});
 }
 async function load(){
  try{
   const response=await fetch("/review/data",{cache:"no-store"});
   if(!response.ok)throw Error("Open your valid client review link to see these reports.");
   const data=await response.json(),rows=data.rows.filter(r=>r.positionId===pid);
   const fingerprint=JSON.stringify(rows);if(lastPayload===fingerprint)return;lastPayload=fingerprint;
   $("comparison-rows").replaceChildren();$("comparison-empty").textContent=rows.length?"":"No profiles have been shared for this position.";
   $("comparison-position").textContent=rows[0]?.positionTitle||"";
   for(const row of rows){
    const tr=document.createElement("tr");tr.dataset.id=row.id;tr.classList.toggle("comparison-selected",row.id===selectedId);
    for(const value of [row.candidateName,row.matchPercent==null?"Unavailable":row.matchPercent+"%",row.mustHaveFit||"Unavailable",row.decision])tr.append(make("td",value));
    const cell=document.createElement("td"),button=make("button","View");button.type="button";button.className="secondary";button.onclick=()=>show(row);cell.append(button);tr.append(cell);$("comparison-rows").append(tr);
   }
   const active=rows.find(r=>r.id===selectedId);
   if(active&&active.submittedUtc!==submitted)show(active,false);
   if(selectedId&&!active){selectedId=null;$("comparison-report").hidden=true;$("comparison-report").removeAttribute("src");$("assessment-hint").hidden=false;$("assessment-feedback").hidden=true;}
  }catch(error){$("notice").textContent=error.message;$("notice").className="error";$("comparison-rows").replaceChildren();$("comparison-report").hidden=true;$("comparison-report").removeAttribute("src");}
 }
 load();setInterval(()=>{if(document.visibilityState==="visible")load();},3000);
})();
