document.querySelectorAll('.password-toggle').forEach(button=>button.addEventListener('click',()=>{
const input=button.parentElement.querySelector('input');const show=input.type==='password';input.type=show?'text':'password';button.textContent=show?'Hide':'Show';button.setAttribute('aria-label',show?'Hide Password':'Show Password');
}));
document.querySelectorAll('form').forEach(form=>form.addEventListener('submit',()=>{const button=form.querySelector('button[type="submit"]');if(button){button.disabled=true;button.setAttribute('aria-busy','true');}}));
