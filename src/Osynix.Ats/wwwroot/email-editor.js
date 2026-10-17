const editors = new WeakMap();
export function set(element, html) {
    dispose(element);
    element.innerHTML = html;
    const state = {range:null};
    state.capture = () => {const selection=window.getSelection();if(selection?.rangeCount&&element.contains(selection.anchorNode)) state.range=selection.getRangeAt(0).cloneRange();};
    state.paste = event => {event.preventDefault();restore(element);document.execCommand('insertText',false,event.clipboardData.getData('text/plain'));state.capture();};
    element.addEventListener('keyup',state.capture);element.addEventListener('mouseup',state.capture);element.addEventListener('input',state.capture);element.addEventListener('paste',state.paste);
    editors.set(element,state);
}
function restore(element){element.focus();const state=editors.get(element);const selection=window.getSelection();if(state?.range&&selection){selection.removeAllRanges();selection.addRange(state.range);}}
export function format(element,command,value){restore(element);document.execCommand(command,false,value);editors.get(element)?.capture();}
export function insert(element,text){format(element,'insertText',text);}
export function get(element){return element.innerHTML;}
export function dispose(element){const state=editors.get(element);if(!state)return;element.removeEventListener('keyup',state.capture);element.removeEventListener('mouseup',state.capture);element.removeEventListener('input',state.capture);element.removeEventListener('paste',state.paste);editors.delete(element);}
