(() => {
  const sectionKeys = new Set(["summary", "responsibilities", "requirements", "preferred", "skills", "tools", "success", "working", "compensation", "organization", "equalOpportunity"]);
  const allowedTags = new Set(["SECTION", "H2", "H3", "P", "DIV", "UL", "OL", "LI", "STRONG", "B", "EM", "I", "BR", "SPAN"]);
  const histories = new WeakMap();

  function clean(root) {
    for (const node of [...root.childNodes]) {
      if (node.nodeType === Node.COMMENT_NODE) {
        node.remove();
        continue;
      }
      if (node.nodeType !== Node.ELEMENT_NODE) continue;
      const element = node;
      if (!allowedTags.has(element.tagName)) {
        element.replaceWith(...element.childNodes);
        continue;
      }
      const sectionKey = element.tagName === "SECTION" ? element.getAttribute("data-section-key") : null;
      const attention = element.tagName === "SECTION" && element.classList.contains("attention");
      const label = element.tagName === "SPAN" && element.classList.contains("jd-attention-label");
      for (const attribute of [...element.attributes]) element.removeAttribute(attribute.name);
      if (element.tagName === "SECTION" && sectionKeys.has(sectionKey)) {
        element.setAttribute("class", `jd-template-section${attention ? " attention" : ""}`);
        element.setAttribute("data-section-key", sectionKey);
      }
      if (label) {
        element.setAttribute("class", "jd-attention-label");
        element.setAttribute("contenteditable", "false");
      }
      clean(element);
    }
  }

  function notifyInput(element, history, inputType) {
    history.applying = true;
    history.lastHtml = element.innerHTML;
    try {
      element.dispatchEvent(new InputEvent("input", { bubbles: true, inputType }));
    } finally {
      history.applying = false;
    }
  }

  function initialize(element) {
    if (histories.has(element)) return histories.get(element);
    const history = { undo: [], redo: [], lastHtml: element.innerHTML, applying: false };
    const remember = () => {
      if (history.applying) return;
      const current = element.innerHTML;
      if (current === history.lastHtml) return;
      history.undo.push(history.lastHtml);
      if (history.undo.length > 100) history.undo.shift();
      history.redo.length = 0;
      history.lastHtml = current;
    };
    element.addEventListener("input", remember);
    element.addEventListener("keydown", event => {
      if (!(event.ctrlKey || event.metaKey)) return;
      const key = event.key.toLowerCase();
      if (key !== "z" && key !== "y") return;
      event.preventDefault();
      restore(element, history, key === "y" || event.shiftKey ? "redo" : "undo");
    });
    element.addEventListener("beforeinput", event => {
      if (event.inputType === "historyUndo" || event.inputType === "historyRedo") {
        event.preventDefault();
        restore(element, history, event.inputType === "historyUndo" ? "undo" : "redo");
      }
    });
    histories.set(element, history);
    return history;
  }

  function restore(element, history, direction) {
    const source = direction === "undo" ? history.undo : history.redo;
    const target = direction === "undo" ? history.redo : history.undo;
    if (!source.length) return;
    target.push(element.innerHTML);
    const restored = source.pop();
    const safe = document.createElement("div");
    safe.innerHTML = restored;
    clean(safe);
    element.innerHTML = safe.innerHTML;
    element.focus({ preventScroll: true });
    const range = document.createRange();
    range.selectNodeContents(element);
    range.collapse(false);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    notifyInput(element, history, direction === "undo" ? "historyUndo" : "historyRedo");
  }

  window.osynixJdEditor = {
    initialize(element) {
      initialize(element);
    },
    read(element) {
      initialize(element);
      const clone = element.cloneNode(true);
      clean(clone);
      return { html: clone.innerHTML, text: clone.innerText || clone.textContent || "" };
    },
    command(element, command) {
      initialize(element);
      element.focus();
      if (command === "heading") document.execCommand("formatBlock", false, "h2");
      else document.execCommand(command, false, null);
      element.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "format" }));
    },
    undo(element) {
      const history = initialize(element);
      restore(element, history, "undo");
    },
    redo(element) {
      const history = initialize(element);
      restore(element, history, "redo");
    }
  };
})();
